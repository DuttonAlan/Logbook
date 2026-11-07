// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;

using Logbook.Api.Authentication;
using Logbook.Api.Authorization;
using Logbook.Api.LogEvents;
using Logbook.Api.RateLimiting;
using Logbook.Core.Application.LogEvents.Ingest;
using Logbook.Core.Application.LogEvents.Search;
using Logbook.Core.Application.LogEvents.Tail;
using Logbook.Core.Domain.Entities;
using Logbook.Core.Domain.ValueObjects;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Logbook.Api.Controllers;

/// <summary>
///     Provides the Endpoints for the Log Events of Applications.
/// </summary>
/// <remarks>
///     The actions deliberately have no <see cref="ConsumesAttribute"/>: routing would skip an action for another
///     content type, so the request would end in the catch-all for unknown API routes and return 404 instead of 415.
/// </remarks>
/// <param name="ingestLogEventsService">The Service to receive Log Events.</param>
/// <param name="searchLogEventsService">The Service to search Log Events.</param>
/// <param name="tailLogEventsService">The Service to follow new Log Events.</param>
/// <param name="applicationLifetime">Tells when the server stops, which must end the live tails.</param>
/// <param name="jsonOptions">The JSON settings of the responses, which the live tail must share with the search.</param>
/// <param name="timeProvider">The clock, to end a live tail when its session expires.</param>
[ApiController]
[Route("api/v1/logs")]
[Tags("Logs")]
public sealed class LogsController(
    IngestLogEventsService ingestLogEventsService,
    SearchLogEventsService searchLogEventsService,
    TailLogEventsService tailLogEventsService,
    IHostApplicationLifetime applicationLifetime,
    IOptions<JsonOptions> jsonOptions,
    TimeProvider timeProvider) : ControllerBase
{
    private const int MaxLimit = 1000;
    private const int MaxSearchTextLength = 500;
    private const int MaxCursorLength = 64;
    private const int MaxPropertyFilters = 10;

    /// <summary>
    ///     Searches Log Events, newest first.
    /// </summary>
    /// <remarks>
    ///     All filters are optional and must all match. To get the next page, send the same filters again with the
    ///     <c>nextCursor</c> of the response. This requires a session: an API Key can only send Log Events.
    /// </remarks>
    /// <param name="from" example="2026-11-04T00:00:00Z">Only Log Events at or after this point in time.</param>
    /// <param name="to" example="2026-11-05T00:00:00Z">Only Log Events before this point in time.</param>
    /// <param name="levels" example="Error">Only Log Events with this level. Can be repeated to allow several levels.</param>
    /// <param name="applicationId" example="0199d1a2-7c3e-7b4a-9f10-5c963f66afa6">Only Log Events of this Application.</param>
    /// <param name="environment" example="Production">Only Log Events of this Environment, compared exactly.</param>
    /// <param name="text" example="order failed">Only Log Events whose message contains these words. Supports "exact phrases", OR, and -excluded words.</param>
    /// <param name="traceId" example="4bf92f3577b34da6a3ce929d0e0e4736">Only Log Events of this trace, compared exactly.</param>
    /// <param name="properties" example="OrderId=42">Only Log Events with this top-level property, as Name=Value. Can be repeated, and all must match.</param>
    /// <param name="exceptionFingerprint" example="9f86d081884c7d659a2feaa0c55ad015">Only Log Events with an exception of this group, compared exactly.</param>
    /// <param name="cursor">The <c>nextCursor</c> of the previous page.</param>
    /// <param name="limit" example="100">The maximum number of Log Events on the page, at most 1000.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>200 OK with one page of Log Events.</returns>
    /// <response code="200">Responds with the Log Events on the page and the cursor to the next page.</response>
    /// <response code="400">Responds with problem details if a parameter is not valid, e.g. an unknown level, a cursor that was not returned by Logbook, or an empty time range.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    /// <response code="503">Responds with problem details and a Retry-After header if the database cannot be reached.</response>
    [HttpGet]
    [Authorize(AuthorizationPolicies.AdminApi)]
    [ProducesResponseType<SearchLogEventsResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Search(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery(Name = "level")] LogEventLevel[]? levels = null,
        [FromQuery] Guid? applicationId = null,
        [FromQuery, StringLength(LogEvent.EnvironmentMaxLength)] string? environment = null,
        [FromQuery, StringLength(MaxSearchTextLength)] string? text = null,
        [FromQuery, StringLength(LogEvent.TraceFieldMaxLength)] string? traceId = null,
        [FromQuery(Name = "property"), MaxLength(MaxPropertyFilters)] string?[]? properties = null,
        [FromQuery, StringLength(ExceptionFingerprint.Length)] string? exceptionFingerprint = null,
        [FromQuery, StringLength(MaxCursorLength)] string? cursor = null,
        [FromQuery, Range(1, MaxLimit)] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var filter = new LogEventFilter(from, to, levels, applicationId, environment, text, traceId, ParseProperties(properties), exceptionFingerprint);

        return Ok(await searchLogEventsService.ExecuteAsync(filter, cursor, limit, cancellationToken));
    }

    /// <summary>
    ///     Follows new Log Events as they are stored, as server-sent events.
    /// </summary>
    /// <remarks>
    ///     The filters work as in the search. The stream sends the event <c>connected</c> once it is following, <c>log</c>
    ///     with a Log Event in the format of the search, <c>gap</c> if Log Events were skipped because they arrive too fast,
    ///     and <c>heartbeat</c> every 15 seconds when nothing else is sent. It ends when the server stops or the session expires.
    ///     This requires a session.
    /// </remarks>
    /// <param name="levels" example="Error">Only Log Events with this level. Can be repeated to allow several levels.</param>
    /// <param name="applicationId" example="0199d1a2-7c3e-7b4a-9f10-5c963f66afa6">Only Log Events of this Application.</param>
    /// <param name="environment" example="Production">Only Log Events of this Environment, compared exactly.</param>
    /// <param name="text" example="order failed">Only Log Events whose message contains these words. Supports "exact phrases", OR, and -excluded words.</param>
    /// <param name="traceId" example="4bf92f3577b34da6a3ce929d0e0e4736">Only Log Events of this trace, compared exactly.</param>
    /// <param name="properties" example="OrderId=42">Only Log Events with this top-level property, as Name=Value. Can be repeated, and all must match.</param>
    /// <param name="exceptionFingerprint" example="9f86d081884c7d659a2feaa0c55ad015">Only Log Events with an exception of this group, compared exactly.</param>
    /// <returns>200 OK with the stream.</returns>
    /// <response code="200">Responds with the stream of server-sent events.</response>
    /// <response code="400">Responds with problem details if a parameter is not valid, e.g. an unknown level.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    [HttpGet("stream")]
    [Authorize(AuthorizationPolicies.AdminApi)]
    [ProducesResponseType<string>(StatusCodes.Status200OK, "text/event-stream")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<IResult> Stream(
        [FromQuery(Name = "level")] LogEventLevel[]? levels = null,
        [FromQuery] Guid? applicationId = null,
        [FromQuery, StringLength(LogEvent.EnvironmentMaxLength)] string? environment = null,
        [FromQuery, StringLength(MaxSearchTextLength)] string? text = null,
        [FromQuery, StringLength(LogEvent.TraceFieldMaxLength)] string? traceId = null,
        [FromQuery(Name = "property"), MaxLength(MaxPropertyFilters)] string?[]? properties = null,
        [FromQuery, StringLength(ExceptionFingerprint.Length)] string? exceptionFingerprint = null)
    {
        var filter = new LogEventFilter(null, null, levels, applicationId, environment, text, traceId, ParseProperties(properties), exceptionFingerprint);

        // Without this, nginx holds the events back in its buffer by default, and the stream looks silent.
        Response.Headers["X-Accel-Buffering"] = "no";

        var session = await HttpContext.AuthenticateAsync(SessionAuthentication.SchemeName);

        return TypedResults.ServerSentEvents(ReadTailAsync(filter, session.Properties?.ExpiresUtc, HttpContext.RequestAborted));
    }

    /// <summary>
    ///     Receives a batch of Log Events from an Application.
    /// </summary>
    /// <remarks>
    ///     The body is a JSON array of Log Events and can be compressed with gzip (<c>Content-Encoding: gzip</c>). The
    ///     Application is taken from the API Key in the header <c>X-Logbook-Key</c>. If one Log Event is not valid, none
    ///     are accepted. Accepted Log Events are written to the database shortly afterwards.
    /// </remarks>
    /// <param name="logEvents">The Log Events.</param>
    /// <returns>202 Accepted with the number of accepted Log Events.</returns>
    /// <response code="202">Responds with the number of accepted Log Events, which are queued for writing.</response>
    /// <response code="400">Responds with problem details if the body is not valid, e.g. malformed JSON or gzip, an empty batch, or a Log Event that is not valid.</response>
    /// <response code="413">Responds with problem details if the body is too large, also after decompression.</response>
    /// <response code="415">Responds with problem details if the body is not JSON or the content encoding is not gzip.</response>
    /// <response code="429">Responds with problem details and a Retry-After header if the API Key sends too many requests. None of the Log Events were accepted, so the batch can be sent again.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    /// <response code="503">Responds with problem details and a Retry-After header if the queue is full or the database cannot be reached to check the API Key. None of the Log Events were accepted, so the batch can be sent again.</response>
    [HttpPost]
    [Authorize(AuthorizationPolicies.ApplicationApiKey)]
    [EnableRateLimiting(RateLimitPolicies.ApiKey)]
    [ServiceFilter<LogRequestBodyFilter>]
    [ProducesResponseType<IngestLogEventsResponse>(StatusCodes.Status202Accepted, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Ingest([FromBody] IReadOnlyList<LogEventRequest?> logEvents)
    {
        var applicationId = Guid.Parse(User.FindFirstValue(ApiKeyClaimTypes.ApplicationId)!, CultureInfo.InvariantCulture);

        var response = await ingestLogEventsService.ExecuteAsync(applicationId, logEvents);

        return Accepted(response);
    }

    private static LogEventPropertyFilter[]? ParseProperties(string?[]? properties) => properties?.Select(LogEventPropertyFilter.Parse).ToArray();

    private async IAsyncEnumerable<SseItem<string>> ReadTailAsync(
        LogEventFilter filter,
        DateTimeOffset? sessionExpiresAt,
        [EnumeratorCancellation] CancellationToken requestAborted)
    {
        // A stream never ends by itself, so it would hold up a graceful shutdown until the time for it is over, which is
        // also the time the server needs to write the Log Events that are left in the queue.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, applicationLifetime.ApplicationStopping);

        // Authorization only ran when the stream started, and a session must not read logs for longer than it lasts.
        if (sessionExpiresAt is { } expiresAt)
        {
            stop.CancelAfter(TimeSpan.FromTicks(Math.Max(0, (expiresAt - timeProvider.GetUtcNow()).Ticks)));
        }

        await using var messages = tailLogEventsService.ExecuteAsync(filter, stop.Token).GetAsyncEnumerator(stop.Token);

        while (true)
        {
            try
            {
                if (!await messages.MoveNextAsync())
                {
                    yield break;
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                yield break;
            }

            yield return ToServerSentEvent(messages.Current);
        }
    }

    private SseItem<string> ToServerSentEvent(LogEventTailMessage message)
    {
        return message.Kind switch
        {
            LogEventTailMessageKind.LogEvent => new SseItem<string>(JsonSerializer.Serialize(message.LogEvent, jsonOptions.Value.JsonSerializerOptions), "log"),
            LogEventTailMessageKind.Connected => new SseItem<string>("{}", "connected"),
            LogEventTailMessageKind.Gap => new SseItem<string>("{}", "gap"),
            _ => new SseItem<string>("{}", "heartbeat")
        };
    }
}
