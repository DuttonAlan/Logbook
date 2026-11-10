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
///     Writes the metric points from the <see cref="MetricQueue"/> to the database in batches.
/// </summary>
/// <param name="queue">The queue to read from.</param>
/// <param name="writer">The writer of a batch.</param>
/// <param name="options">The size and timing of a batch.</param>
/// <param name="timeProvider">The clock for the timing of a batch.</param>
/// <param name="logger">The Logger to record metric points that could not be written before the shutdown.</param>
public sealed class MetricWriterService(
    MetricQueue queue,
    MetricBatchWriter writer,
    IOptions<LogIngestionOptions> options,
    TimeProvider timeProvider,
    ILogger<MetricWriterService> logger)
    : TelemetryWriterService<MetricPoint, IMetricRepository>(MetricBatchWriter.KindName, queue, writer, options, timeProvider, logger)
{
}
