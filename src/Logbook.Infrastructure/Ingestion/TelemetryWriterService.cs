// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Application.LogEvents;
using Logbook.Core.Domain.Entities;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logbook.Infrastructure.Ingestion;

/// <summary>
///     Writes the records from a <see cref="TelemetryQueue{T}"/> to the database in batches.
/// </summary>
/// <remarks>
///     On shutdown it writes what is left in the queue. The web server stops first, so nothing new arrives by then, but it
///     can use up much of the shutdown timeout of the host. Writing stops when the token of the host says the time is up, and
///     stopping only ends after any loss is logged, as the process can end right after that.
/// </remarks>
/// <typeparam name="T">The type of the records.</typeparam>
/// <typeparam name="TStore">The repository the batch writer writes with.</typeparam>
/// <param name="kind">What the records are called in the logs.</param>
/// <param name="queue">The queue to read from.</param>
/// <param name="writer">The writer of a batch.</param>
/// <param name="options">The size and timing of a batch.</param>
/// <param name="timeProvider">The clock for the timing of a batch.</param>
/// <param name="logger">The Logger to record what could not be written before the shutdown.</param>
public abstract class TelemetryWriterService<T, TStore>(
    string kind,
    TelemetryQueue<T> queue,
    TelemetryBatchWriter<T, TStore> writer,
    IOptions<LogIngestionOptions> options,
    TimeProvider timeProvider,
    ILogger logger) : BackgroundService
    where T : ITelemetryRecord
    where TStore : ITelemetryStore<T>
{
    private readonly List<T> _batch = new(options.Value.WriteBatchSize);

    private CancellationToken _shutdownDeadline = CancellationToken.None;

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _shutdownDeadline = cancellationToken;

        // Without the token: draining ends at the deadline itself, and must get to log the loss before this returns.
        await base.StopAsync(CancellationToken.None);

        // If the host stops right after starting, ExecuteAsync may never have run, and neither has its draining.
        // ExecuteAsync itself never ends as cancelled, because it handles cancellation.
        if (ExecuteTask is { IsCanceled: true })
        {
            await DrainAsync();
        }
    }

    /// <summary>
    ///     Called after a batch is stored, before its space in the queue is freed.
    /// </summary>
    /// <param name="batch">The stored records.</param>
    protected virtual void OnWritten(IReadOnlyList<T> batch)
    {
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await queue.WaitToReadAsync(stoppingToken))
            {
                await CollectBatchAsync(stoppingToken);
                await WriteBatchAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping. A batch that was being written is still in _batch.
        }

        await DrainAsync();
    }

    private async Task CollectBatchAsync(CancellationToken stoppingToken)
    {
        var batchSize = options.Value.WriteBatchSize;

        queue.Fill(_batch, batchSize);

        if (_batch.Count < batchSize)
        {
            await Task.Delay(options.Value.WriteInterval, timeProvider, stoppingToken);

            queue.Fill(_batch, batchSize);
        }
    }

    private async Task WriteBatchAsync(CancellationToken cancellationToken)
    {
        await writer.WriteAsync(_batch, cancellationToken);

        OnWritten(_batch);

        queue.Release(_batch);
        _batch.Clear();
    }

    private async Task DrainAsync()
    {
        try
        {
            queue.Fill(_batch, options.Value.WriteBatchSize);

            while (_batch.Count > 0)
            {
                await WriteBatchAsync(_shutdownDeadline);

                queue.Fill(_batch, options.Value.WriteBatchSize);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LostOnShutdown(kind, _batch.Count + queue.CountWaiting());
        }
    }
}
