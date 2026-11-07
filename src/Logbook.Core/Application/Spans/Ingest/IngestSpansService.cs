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

namespace Logbook.Core.Application.Spans.Ingest;

/// <summary>
///     The Service to receive the spans of an Application.
/// </summary>
/// <param name="spanQueue">An Instance of a ISpanQueue that takes the spans for writing.</param>
/// <param name="options">The limits for received data, which spans share with Log Events.</param>
/// <param name="timeProvider">The TimeProvider to check the timestamps.</param>
/// <param name="logger">The Logger to record the number of spans. Their content is never logged.</param>
public sealed class IngestSpansService(
    ISpanQueue spanQueue,
    IOptions<LogIngestionOptions> options,
    TimeProvider timeProvider,
    ILogger<IngestSpansService> logger)
{
    /// <summary>
    ///     Validates a batch of spans and queues it for writing: if one span is not valid, none are accepted.
    /// </summary>
    /// <param name="applicationId">Id of the Application, taken from its API Key, never from the request body.</param>
    /// <param name="spans">The spans to accept.</param>
    /// <returns>The number of accepted spans.</returns>
    /// <exception cref="DomainValidationException">Thrown when the batch is empty or a span is not valid, with the position of the span.</exception>
    /// <exception cref="ServiceBusyException">Thrown when the queue is full. None of the spans were accepted.</exception>
    public Task<IngestSpansResponse> ExecuteAsync(
        Guid applicationId,
        IReadOnlyList<SpanRequest?> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);

        if (spans.Count == 0)
        {
            throw new DomainValidationException("The batch must contain at least one span.", nameof(spans));
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var created = new List<TraceSpan>(spans.Count);

        for (var index = 0; index < spans.Count; index++)
        {
            try
            {
                created.Add(CreateSpan(applicationId, spans[index], now));
            }
            catch (DomainValidationException exception)
            {
                throw new DomainValidationException($"Span {index}: {exception.Message}", exception);
            }
        }

        if (!spanQueue.TryEnqueue(created))
        {
            throw new ServiceBusyException("Logbook is receiving more spans than it can write. Send them again later.");
        }

        logger.SpansReceived(applicationId, created.Count);

        return Task.FromResult(new IngestSpansResponse(created.Count));
    }

    private TraceSpan CreateSpan(Guid applicationId, SpanRequest? request, DateTime now)
    {
        if (request is null)
        {
            throw new DomainValidationException("The span must not be null.");
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

        return TraceSpan.Create(
            applicationId,
            timestamp,
            request.Kind ?? throw new DomainValidationException("The kind is required.", nameof(request.Kind)),
            request.Name,
            request.DurationMs ?? throw new DomainValidationException("The duration is required.", nameof(request.DurationMs)),
            request.Success ?? throw new DomainValidationException("The success is required.", nameof(request.Success)),
            request.TraceId,
            request.SpanId,
            request.ParentSpanId,
            request.Environment,
            request.StatusCode,
            request.DependencyType,
            request.Target,
            request.Data);
    }
}
