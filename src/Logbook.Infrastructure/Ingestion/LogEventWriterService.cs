// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Application.LogEvents;
using Logbook.Core.Domain.Entities;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logbook.Infrastructure.Ingestion;

/// <summary>
///     Writes the Log Events from the <see cref="LogEventQueue"/> to the database in batches, and tells the live tail.
/// </summary>
/// <param name="queue">The queue to read from.</param>
/// <param name="writer">The writer of a batch.</param>
/// <param name="feed">The feed to tell about written batches, for the live tail.</param>
/// <param name="options">The size and timing of a batch.</param>
/// <param name="timeProvider">The clock for the timing of a batch.</param>
/// <param name="logger">The Logger to record Log Events that could not be written before the shutdown.</param>
public sealed class LogEventWriterService(
    LogEventQueue queue,
    LogEventBatchWriter writer,
    LogEventFeed feed,
    IOptions<LogIngestionOptions> options,
    TimeProvider timeProvider,
    ILogger<LogEventWriterService> logger)
    : TelemetryWriterService<LogEvent, ILogEventRepository>(LogEventBatchWriter.KindName, queue, writer, options, timeProvider, logger)
{
    /// <inheritdoc/>
    protected override void OnWritten(IReadOnlyList<LogEvent> batch) => feed.Publish(batch);
}
