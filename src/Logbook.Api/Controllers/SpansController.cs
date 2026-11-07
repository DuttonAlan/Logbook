// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;

using Logbook.Api.Authentication;
using Logbook.Api.Authorization;
using Logbook.Api.LogEvents;
using Logbook.Api.RateLimiting;
using Logbook.Core.Application.Spans.Ingest;
using Logbook.Core.Application.Spans.Search;
using Logbook.Core.Domain.Entities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Logbook.Api.Controllers;

/// <summary>
///     Provides the Endpoints for the spans of Applications: the requests they receive and the dependencies they call.
/// </summary>
/// <remarks>
///     The actions deliberately have no <see cref="ConsumesAttribute"/>: routing would skip an action for another content
///     type, so the request would end in the catch-all for unknown API routes and return 404 instead of 415.
/// </remarks>
/// <param name="ingestSpansService">The Service to receive spans.</param>
/// <param name="searchSpansService">The Service to search spans.</param>
[ApiController]
[Route("api/v1/spans")]
[Tags("Spans")]
public sealed class SpansController(
    IngestSpansService ingestSpansService,
    SearchSpansService searchSpansService) : ControllerBase
{
    private const int MaxLimit = 1000;
    private const int MaxCursorLength = 64;

    /// <summary>
    ///     Searches spans, newest first.
    /// </summary>
    /// <remarks>
    ///     All filters are optional and must all match. To get the next page, send the same filters again with the
    ///     <c>nextCursor</c> of the response. This requires a session: an API Key can only send spans.
    /// </remarks>
    /// <param name="from" example="2026-11-04T00:00:00Z">Only spans that started at or after this point in time.</param>
    /// <param name="to" example="2026-11-05T00:00:00Z">Only spans that started before this point in time.</param>
    /// <param name="kind" example="Request">Only spans of this kind: Request or Dependency.</param>
    /// <param name="applicationId" example="0199d1a2-7c3e-7b4a-9f10-5c963f66afa6">Only spans of this Application.</param>
    /// <param name="environment" example="Production">Only spans of this Environment, compared exactly.</param>
    /// <param name="name" example="/orders">Only spans whose name contains this text, not case-sensitive.</param>
    /// <param name="traceId" example="4bf92f3577b34da6a3ce929d0e0e4736">Only spans of this trace, compared exactly.</param>
    /// <param name="success" example="false">Only spans that succeeded (true), or only those that failed (false).</param>
    /// <param name="minDurationMs" example="500">Only spans that took at least this many milliseconds.</param>
    /// <param name="cursor">The <c>nextCursor</c> of the previous page.</param>
    /// <param name="limit" example="100">The maximum number of spans on the page, at most 1000.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>200 OK with one page of spans.</returns>
    /// <response code="200">Responds with the spans on the page and the cursor to the next page.</response>
    /// <response code="400">Responds with problem details if a parameter is not valid, e.g. an unknown kind, a cursor that was not returned by Logbook, or an empty time range.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    /// <response code="503">Responds with problem details and a Retry-After header if the database cannot be reached.</response>
    [HttpGet]
    [Authorize(AuthorizationPolicies.AdminApi)]
    [ProducesResponseType<SearchSpansResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Search(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] SpanKind? kind = null,
        [FromQuery] Guid? applicationId = null,
        [FromQuery, StringLength(LogEvent.EnvironmentMaxLength)] string? environment = null,
        [FromQuery, StringLength(TraceSpan.NameMaxLength)] string? name = null,
        [FromQuery, StringLength(LogEvent.TraceFieldMaxLength)] string? traceId = null,
        [FromQuery] bool? success = null,
        [FromQuery, Range(0, double.MaxValue)] double? minDurationMs = null,
        [FromQuery, StringLength(MaxCursorLength)] string? cursor = null,
        [FromQuery, Range(1, MaxLimit)] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var filter = new SpanFilter(from, to, kind, applicationId, environment, name, traceId, success, minDurationMs);

        return Ok(await searchSpansService.ExecuteAsync(filter, cursor, limit, cancellationToken));
    }

    /// <summary>
    ///     Receives a batch of spans from an Application.
    /// </summary>
    /// <remarks>
    ///     The body is a JSON array of spans and can be compressed with gzip (<c>Content-Encoding: gzip</c>). The Application
    ///     is taken from the API Key in the header <c>X-Logbook-Key</c>. If one span is not valid, none are accepted. Accepted
    ///     spans are written to the database shortly afterwards.
    /// </remarks>
    /// <param name="spans">The spans.</param>
    /// <returns>202 Accepted with the number of accepted spans.</returns>
    /// <response code="202">Responds with the number of accepted spans, which are queued for writing.</response>
    /// <response code="400">Responds with problem details if the body is not valid, e.g. malformed JSON or gzip, an empty batch, or a span that is not valid.</response>
    /// <response code="413">Responds with problem details if the body is too large, also after decompression.</response>
    /// <response code="415">Responds with problem details if the body is not JSON or the content encoding is not gzip.</response>
    /// <response code="429">Responds with problem details and a Retry-After header if the API Key sends too many requests. None of the spans were accepted, so the batch can be sent again.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    /// <response code="503">Responds with problem details and a Retry-After header if the queue is full or the database cannot be reached to check the API Key. None of the spans were accepted, so the batch can be sent again.</response>
    [HttpPost]
    [Authorize(AuthorizationPolicies.ApplicationApiKey)]
    [EnableRateLimiting(RateLimitPolicies.ApiKey)]
    [ServiceFilter<LogRequestBodyFilter>]
    [ProducesResponseType<IngestSpansResponse>(StatusCodes.Status202Accepted, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Ingest([FromBody] IReadOnlyList<SpanRequest?> spans)
    {
        var applicationId = Guid.Parse(User.FindFirstValue(ApiKeyClaimTypes.ApplicationId)!, CultureInfo.InvariantCulture);

        return Accepted(await ingestSpansService.ExecuteAsync(applicationId, spans));
    }
}
