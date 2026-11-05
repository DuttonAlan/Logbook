// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Application.LogEvents;
using Logbook.Core.Domain.Entities;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logbook.Infrastructure.Ingestion;

/// <summary>
///     Writes the Log Events from the <see cref="LogEventQueue"/> to the database in batches.
/// </summary>
/// <remarks>
///     On shutdown it writes what is left in the queue. The web server stops first, so nothing new arrives by then.
///     This has to finish within the shutdown timeout of the host, after which the process ends.
/// </remarks>
/// <param name="queue">The queue to read from.</param>
/// <param name="writer">The writer of a batch.</param>
/// <param name="options">The size and timing of a batch.</param>
/// <param name="hostOptions">The shutdown timeout of the host.</param>
/// <param name="timeProvider">The clock for the timing of a batch.</param>
/// <param name="logger">The Logger to record Log Events that could not be written before the shutdown.</param>
public sealed class LogEventWriterService(
    LogEventQueue queue,
    LogEventBatchWriter writer,
    IOptions<LogIngestionOptions> options,
    IOptions<HostOptions> hostOptions,
    TimeProvider timeProvider,
    ILogger<LogEventWriterService> logger) : BackgroundService
{
    private readonly List<LogEvent> _batch = new(options.Value.WriteBatchSize);

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        // If the host stops right after starting, ExecuteAsync may never have run, and neither has its draining.
        // ExecuteAsync itself never ends as cancelled, because it handles cancellation.
        if (ExecuteTask is { IsCanceled: true })
        {
            await DrainAsync();
        }
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

        queue.Release(_batch);
        _batch.Clear();
    }

    private async Task DrainAsync()
    {
        // Stops a little before the host gives up on this service, so the loss can still be logged.
        using var deadline = new CancellationTokenSource(hostOptions.Value.ShutdownTimeout * 0.8, timeProvider);

        try
        {
            queue.Fill(_batch, options.Value.WriteBatchSize);

            while (_batch.Count > 0)
            {
                await WriteBatchAsync(deadline.Token);

                queue.Fill(_batch, options.Value.WriteBatchSize);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogEventsLostOnShutdown(_batch.Count + queue.CountWaiting());
        }
    }
}
