// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Threading.Channels;

using Logbook.Core.Abstractions;
using Logbook.Core.Application.LogEvents;
using Logbook.Core.Domain.Entities;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logbook.Infrastructure.Ingestion;

/// <summary>
///     The in-memory queue between an endpoint that receives records and the <see cref="TelemetryWriterService{T, TStore}"/>.
/// </summary>
/// <remarks>
///     The queue is limited by bytes, not by the number of records, because one Log Event can be hundreds of kilobytes. The
///     channel itself is unbounded, because a bounded channel could accept only a part of a batch. Records count until
///     <see cref="Release"/> is called after writing, as they stay in memory until then.
/// </remarks>
/// <typeparam name="T">The type of the records.</typeparam>
/// <param name="kind">What the records are called in the logs.</param>
/// <param name="options">The limit of the queue.</param>
/// <param name="logger">The Logger to record that the queue is full.</param>
public abstract class TelemetryQueue<T>(string kind, IOptions<LogIngestionOptions> options, ILogger logger) : ITelemetryQueue<T>
    where T : ITelemetryRecord
{
    // Not created with SingleReader, because that channel does not support Reader.Count.
    private readonly Channel<T> _channel = Channel.CreateUnbounded<T>();

    private long _pendingBytes;
    private int _isFull;

    /// <inheritdoc/>
    public bool TryEnqueue(IReadOnlyCollection<T> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var size = SizeOf(records);
        var limit = options.Value.MaxQueuedBytes;
        long current;

        do
        {
            current = Volatile.Read(ref _pendingBytes);

            // An empty queue takes any batch, so a batch that is larger than the limit is not rejected forever.
            if (current > 0 && current + size > limit)
            {
                // Once per overload, as this happens on every request while the queue is full.
                if (Interlocked.Exchange(ref _isFull, 1) == 0)
                {
                    logger.QueueFull(kind, limit);
                }

                return false;
            }
        }
        while (Interlocked.CompareExchange(ref _pendingBytes, current + size, current) != current);

        Volatile.Write(ref _isFull, 0);

        foreach (var record in records)
        {
            _channel.Writer.TryWrite(record);
        }

        return true;
    }

    /// <summary>
    ///     Waits until records can be read.
    /// </summary>
    /// <param name="cancellationToken">CancellationToken to stop waiting.</param>
    /// <returns><see langword="true"/> when records can be read.</returns>
    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken) => _channel.Reader.WaitToReadAsync(cancellationToken);

    /// <summary>
    ///     Moves waiting records into a batch, without waiting for more.
    /// </summary>
    /// <param name="batch">The batch to add to.</param>
    /// <param name="maxCount">The maximum size of the batch.</param>
    public void Fill(List<T> batch, int maxCount)
    {
        ArgumentNullException.ThrowIfNull(batch);

        while (batch.Count < maxCount && _channel.Reader.TryRead(out var record))
        {
            batch.Add(record);
        }
    }

    /// <summary>
    ///     Frees the space of records that were read and are finished.
    /// </summary>
    /// <param name="records">The finished records.</param>
    public void Release(IReadOnlyCollection<T> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        Interlocked.Add(ref _pendingBytes, -SizeOf(records));
    }

    /// <summary>
    ///     Gets the number of records waiting to be read.
    /// </summary>
    /// <returns>The number of records.</returns>
    public int CountWaiting() => _channel.Reader.Count;

    private static long SizeOf(IReadOnlyCollection<T> records)
    {
        return records.Sum(record => record.GetMemoryBytes());
    }
}
