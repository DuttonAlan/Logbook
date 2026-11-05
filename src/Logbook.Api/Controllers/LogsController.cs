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
using Logbook.Core.Application.LogEvents.Ingest;
using Logbook.Core.Application.LogEvents.Search;
using Logbook.Core.Domain.Entities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
[ApiController]
[Route("api/v1/logs")]
[Tags("Logs")]
public sealed class LogsController(
    IngestLogEventsService ingestLogEventsService,
    SearchLogEventsService searchLogEventsService) : ControllerBase
{
    private const int MaxLimit = 1000;
    private const int MaxSearchTextLength = 500;
    private const int MaxCursorLength = 64;

    /// <summary>
    ///     Searches Log Events, newest first.
    /// </summary>
    /// <remarks>
    ///     All filters are optional and must all match. To get the next page, send the same filters again with the
    ///     <c>nextCursor</c> of the response. This requires the admin key: an API Key can only send Log Events.
    /// </remarks>
    /// <param name="from" example="2026-11-04T00:00:00Z">Only Log Events at or after this point in time.</param>
    /// <param name="to" example="2026-11-05T00:00:00Z">Only Log Events before this point in time.</param>
    /// <param name="levels" example="Error">Only Log Events with this level. Can be repeated to allow several levels.</param>
    /// <param name="applicationId" example="0199d1a2-7c3e-7b4a-9f10-5c963f66afa6">Only Log Events of this Application.</param>
    /// <param name="environment" example="Production">Only Log Events of this Environment, compared exactly.</param>
    /// <param name="text" example="order failed">Only Log Events whose message contains these words. Supports "exact phrases", OR, and -excluded words.</param>
    /// <param name="cursor">The <c>nextCursor</c> of the previous page.</param>
    /// <param name="limit" example="100">The maximum number of Log Events on the page, at most 1000.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>200 OK with one page of Log Events.</returns>
    /// <response code="200">Responds with the Log Events on the page and the cursor to the next page.</response>
    /// <response code="400">Responds with problem details if a parameter is not valid, e.g. an unknown level, a cursor that was not returned by Logbook, or an empty time range.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    [HttpGet]
    [Authorize(AuthorizationPolicies.AdminApi)]
    [ProducesResponseType<SearchLogEventsResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<IActionResult> Search(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery(Name = "level")] LogEventLevel[]? levels = null,
        [FromQuery] Guid? applicationId = null,
        [FromQuery, StringLength(LogEvent.EnvironmentMaxLength)] string? environment = null,
        [FromQuery, StringLength(MaxSearchTextLength)] string? text = null,
        [FromQuery, StringLength(MaxCursorLength)] string? cursor = null,
        [FromQuery, Range(1, MaxLimit)] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var filter = new LogEventFilter(from, to, levels, applicationId, environment, text);

        return Ok(await searchLogEventsService.ExecuteAsync(filter, cursor, limit, cancellationToken));
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
    /// <response code="503">Responds with problem details and a Retry-After header if the queue is full. None of the Log Events were accepted, so the batch can be sent again.</response>
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
}
