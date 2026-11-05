// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Domain.Entities;
using Logbook.Core.Domain.Exceptions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logbook.Core.Application.LogEvents.Ingest;

/// <summary>
///     The Service to receive the Log Events of an Application.
/// </summary>
/// <param name="logEventRepository">An Instance of a ILogEventRepository to store the Log Events.</param>
/// <param name="options">The limits for received Log Events.</param>
/// <param name="timeProvider">The TimeProvider to check the timestamps.</param>
/// <param name="logger">The Logger to record the number of Log Events. Their content is never logged.</param>
public sealed class IngestLogEventsService(
    ILogEventRepository logEventRepository,
    IOptions<LogIngestionOptions> options,
    TimeProvider timeProvider,
    ILogger<IngestLogEventsService> logger)
{
    /// <summary>
    ///     Validates and stores a batch of Log Events: if one Log Event is not valid, none are stored.
    /// </summary>
    /// <param name="applicationId">Id of the Application, taken from its API Key, never from the request body.</param>
    /// <param name="logEvents">The Log Events to store.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The number of accepted Log Events.</returns>
    /// <exception cref="DomainValidationException">Thrown when the batch is empty or a Log Event is not valid, with the position of the Log Event.</exception>
    public async Task<IngestLogEventsResponse> ExecuteAsync(
        Guid applicationId,
        IReadOnlyList<LogEventRequest?> logEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logEvents);

        if (logEvents.Count == 0)
        {
            throw new DomainValidationException("The batch must contain at least one Log Event.", nameof(logEvents));
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var created = new List<LogEvent>(logEvents.Count);

        for (var index = 0; index < logEvents.Count; index++)
        {
            try
            {
                created.Add(CreateLogEvent(applicationId, logEvents[index], now));
            }
            catch (DomainValidationException exception)
            {
                throw new DomainValidationException($"Log Event {index}: {exception.Message}", exception);
            }
        }

        await logEventRepository.AddRange(created, cancellationToken);

        logger.LogEventsReceived(applicationId, created.Count);

        return new IngestLogEventsResponse(created.Count);
    }

    private LogEvent CreateLogEvent(Guid applicationId, LogEventRequest? request, DateTime now)
    {
        if (request is null)
        {
            throw new DomainValidationException("The Log Event must not be null.");
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

        var logEvent = LogEvent.Create(
            applicationId,
            timestamp,
            request.Level ?? throw new DomainValidationException("The level is required.", nameof(request.Level)),
            request.Message,
            request.MessageTemplate,
            request.Category,
            request.Environment,
            request.EventId,
            request.Exception,
            request.TraceId,
            request.SpanId,
            request.Properties);

        if (logEvent.GetContentBytes() > limits.MaxEventBytes)
        {
            throw new DomainValidationException($"The Log Event must not be larger than {limits.MaxEventBytes} bytes.");
        }

        return logEvent;
    }
}
