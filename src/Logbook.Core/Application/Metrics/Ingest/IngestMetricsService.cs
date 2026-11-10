// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Application.Exceptions;
using Logbook.Core.Application.LogEvents;
using Logbook.Core.Domain.Entities;
using Logbook.Core.Domain.Exceptions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logbook.Core.Application.Metrics.Ingest;

/// <summary>
///     The Service to receive the metric points of an Application.
/// </summary>
/// <param name="metricQueue">An Instance of a IMetricQueue that takes the metric points for writing.</param>
/// <param name="options">The limits for received data, which metric points share with Log Events.</param>
/// <param name="timeProvider">The TimeProvider to check the timestamps.</param>
/// <param name="logger">The Logger to record the number of metric points. Their content is never logged.</param>
public sealed class IngestMetricsService(
    IMetricQueue metricQueue,
    IOptions<LogIngestionOptions> options,
    TimeProvider timeProvider,
    ILogger<IngestMetricsService> logger)
{
    /// <summary>
    ///     Validates a batch of metric points and queues it for writing: if one is not valid, none are accepted.
    /// </summary>
    /// <param name="applicationId">Id of the Application, taken from its API Key, never from the request body.</param>
    /// <param name="metrics">The metric points to accept.</param>
    /// <returns>The number of accepted metric points.</returns>
    /// <exception cref="DomainValidationException">Thrown when the batch is empty or a metric point is not valid, with its position.</exception>
    /// <exception cref="ServiceBusyException">Thrown when the queue is full. None of the metric points were accepted.</exception>
    public Task<IngestMetricsResponse> ExecuteAsync(
        Guid applicationId,
        IReadOnlyList<MetricRequest?> metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        if (metrics.Count == 0)
        {
            throw new DomainValidationException("The batch must contain at least one metric point.", nameof(metrics));
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var created = new List<MetricPoint>(metrics.Count);

        for (var index = 0; index < metrics.Count; index++)
        {
            try
            {
                created.Add(CreateMetricPoint(applicationId, metrics[index], now));
            }
            catch (DomainValidationException exception)
            {
                throw new DomainValidationException($"Metric point {index}: {exception.Message}", exception);
            }
        }

        if (!metricQueue.TryEnqueue(created))
        {
            throw new ServiceBusyException("Logbook is receiving more metric points than it can write. Send them again later.");
        }

        logger.MetricsReceived(applicationId, created.Count);

        return Task.FromResult(new IngestMetricsResponse(created.Count));
    }

    private MetricPoint CreateMetricPoint(Guid applicationId, MetricRequest? request, DateTime now)
    {
        if (request is null)
        {
            throw new DomainValidationException("The metric point must not be null.");
        }

        var limits = options.Value;
        var timestamp = request.Timestamp?.UtcDateTime
            ?? throw new DomainValidationException("The timestamp is required.", nameof(request.Timestamp));

        // Every day in this window can get a partition, so the window limits how many tables a sender can cause.
        if (timestamp < now - limits.MaxEventAge || timestamp > now + limits.MaxFutureOffset)
        {
            throw new DomainValidationException(
                $"The timestamp must not be older than {limits.MaxEventAge} or later than {limits.MaxFutureOffset} from now.",
                nameof(request.Timestamp));
        }

        return MetricPoint.Create(
            applicationId,
            timestamp,
            request.Name,
            request.Count ?? throw new DomainValidationException("The count is required.", nameof(request.Count)),
            request.Sum ?? throw new DomainValidationException("The sum is required.", nameof(request.Sum)),
            request.Min ?? throw new DomainValidationException("The minimum is required.", nameof(request.Min)),
            request.Max ?? throw new DomainValidationException("The maximum is required.", nameof(request.Max)),
            request.Environment,
            request.Dimensions);
    }
}
