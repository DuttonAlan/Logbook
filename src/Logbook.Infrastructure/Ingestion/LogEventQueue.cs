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
///     The in-memory queue between the log endpoint and <see cref="LogEventWriterService"/>.
/// </summary>
/// <remarks>
///     The queue is limited by bytes, not by the number of Log Events, because one Log Event can be hundreds of
///     kilobytes. The channel itself is unbounded, because a bounded channel could accept only a part of a batch.
///     Log Events count until <see cref="Release"/> is called after writing, as they stay in memory until then.
/// </remarks>
/// <param name="options">The limit of the queue.</param>
/// <param name="logger">The Logger to record that the queue is full.</param>
public sealed class LogEventQueue(IOptions<LogIngestionOptions> options, ILogger<LogEventQueue> logger) : ILogEventQueue
{
    // Roughly what the object and its short strings use besides the content counted by GetContentBytes.
    private const long EventOverheadBytes = 1024;

    // Not created with SingleReader, because that channel does not support Reader.Count.
    private readonly Channel<LogEvent> _channel = Channel.CreateUnbounded<LogEvent>();

    private long _pendingBytes;
    private int _isFull;

    /// <inheritdoc/>
    public bool TryEnqueue(IReadOnlyCollection<LogEvent> logEvents)
    {
        ArgumentNullException.ThrowIfNull(logEvents);

        var size = SizeOf(logEvents);
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
                    logger.QueueFull(limit);
                }

                return false;
            }
        }
        while (Interlocked.CompareExchange(ref _pendingBytes, current + size, current) != current);

        Volatile.Write(ref _isFull, 0);

        foreach (var logEvent in logEvents)
        {
            _channel.Writer.TryWrite(logEvent);
        }

        return true;
    }

    /// <summary>
    ///     Waits until Log Events can be read.
    /// </summary>
    /// <param name="cancellationToken">CancellationToken to stop waiting.</param>
    /// <returns><see langword="true"/> when Log Events can be read.</returns>
    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken) => _channel.Reader.WaitToReadAsync(cancellationToken);

    /// <summary>
    ///     Moves waiting Log Events into a batch, without waiting for more.
    /// </summary>
    /// <param name="batch">The batch to add to.</param>
    /// <param name="maxCount">The maximum size of the batch.</param>
    public void Fill(List<LogEvent> batch, int maxCount)
    {
        ArgumentNullException.ThrowIfNull(batch);

        while (batch.Count < maxCount && _channel.Reader.TryRead(out var logEvent))
        {
            batch.Add(logEvent);
        }
    }

    /// <summary>
    ///     Frees the space of Log Events that were read and are finished.
    /// </summary>
    /// <param name="logEvents">The finished Log Events.</param>
    public void Release(IReadOnlyCollection<LogEvent> logEvents)
    {
        ArgumentNullException.ThrowIfNull(logEvents);

        Interlocked.Add(ref _pendingBytes, -SizeOf(logEvents));
    }

    /// <summary>
    ///     Gets the number of Log Events waiting to be read.
    /// </summary>
    /// <returns>The number of Log Events.</returns>
    public int CountWaiting() => _channel.Reader.Count;

    private static long SizeOf(IReadOnlyCollection<LogEvent> logEvents)
    {
        return logEvents.Sum(logEvent => logEvent.GetContentBytes() + EventOverheadBytes);
    }
}
