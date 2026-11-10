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
using Logbook.Core.Application.Metrics.GetNames;
using Logbook.Core.Application.Metrics.GetSeries;
using Logbook.Core.Application.Metrics.Ingest;
using Logbook.Core.Domain.Entities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Logbook.Api.Controllers;

/// <summary>
///     Provides the Endpoints for the custom metrics of Applications.
/// </summary>
/// <remarks>
///     The actions deliberately have no <see cref="ConsumesAttribute"/>: routing would skip an action for another content
///     type, so the request would end in the catch-all for unknown API routes and return 404 instead of 415.
/// </remarks>
/// <param name="ingestMetricsService">The Service to receive metric points.</param>
/// <param name="getMetricNamesService">The Service to find the names of the metrics.</param>
/// <param name="getMetricSeriesService">The Service to read a metric for a chart.</param>
[ApiController]
[Route("api/v1/metrics")]
[Tags("Metrics")]
public sealed class MetricsController(
    IngestMetricsService ingestMetricsService,
    GetMetricNamesService getMetricNamesService,
    GetMetricSeriesService getMetricSeriesService) : ControllerBase
{
    /// <summary>
    ///     Reads one metric over a time range, combined into buckets of equal width for a chart.
    /// </summary>
    /// <remarks>
    ///     The width of a bucket depends on the length of the time range, from one minute to one week, so that a range of up
    ///     to 120 days has at most 120 buckets. Every bucket is returned, also those without measurements. The average of a bucket is its sum divided
    ///     by its count. This requires a session: an API Key can only send metric points.
    /// </remarks>
    /// <param name="name" example="orders.placed">The name of the metric, compared exactly.</param>
    /// <param name="from" example="2026-11-04T00:00:00Z">The start of the range. Default: one day before <c>to</c>.</param>
    /// <param name="to" example="2026-11-05T00:00:00Z">The end of the range. Default: now.</param>
    /// <param name="applicationId" example="0199d1a2-7c3e-7b4a-9f10-5c963f66afa6">Only metric points of this Application.</param>
    /// <param name="environment" example="Production">Only metric points of this Environment, compared exactly.</param>
    /// <param name="dimensions" example="Region=EU">Only metric points with this dimension, in the form <c>Name=Value</c>. Can be repeated up to 10 times; all must match.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>200 OK with the buckets.</returns>
    /// <response code="200">Responds with every bucket of the time range.</response>
    /// <response code="400">Responds with problem details if a parameter is not valid, e.g. a missing name, an empty or too long time range, or a dimension filter without <c>=</c>.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    /// <response code="503">Responds with problem details and a Retry-After header if the database cannot be reached.</response>
    [HttpGet]
    [Authorize(AuthorizationPolicies.AdminApi)]
    [ProducesResponseType<GetMetricSeriesResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> GetSeries(
        [FromQuery, Required, StringLength(MetricPoint.NameMaxLength)] string name,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] Guid? applicationId = null,
        [FromQuery, StringLength(LogEvent.EnvironmentMaxLength)] string? environment = null,
        [FromQuery(Name = "dimension"), MaxLength(GetMetricSeriesService.MaxDimensionFilters)] string?[]? dimensions = null,
        CancellationToken cancellationToken = default)
    {
        return Ok(await getMetricSeriesService.ExecuteAsync(name, from, to, applicationId, environment, dimensions, cancellationToken));
    }

    /// <summary>
    ///     Gets the names of the metrics that have metric points in a time range.
    /// </summary>
    /// <remarks>
    ///     At most 1000 names, ordered by name. This requires a session.
    /// </remarks>
    /// <param name="from" example="2026-11-04T00:00:00Z">The start of the range. Default: 7 days before <c>to</c>.</param>
    /// <param name="to" example="2026-11-05T00:00:00Z">The end of the range. Default: now.</param>
    /// <param name="applicationId" example="0199d1a2-7c3e-7b4a-9f10-5c963f66afa6">Only metrics of this Application.</param>
    /// <param name="environment" example="Production">Only metrics of this Environment, compared exactly.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>200 OK with the names.</returns>
    /// <response code="200">Responds with the names of the metrics.</response>
    /// <response code="400">Responds with problem details if a parameter is not valid, e.g. an empty or too long time range.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    /// <response code="503">Responds with problem details and a Retry-After header if the database cannot be reached.</response>
    [HttpGet("names")]
    [Authorize(AuthorizationPolicies.AdminApi)]
    [ProducesResponseType<GetMetricNamesResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> GetNames(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] Guid? applicationId = null,
        [FromQuery, StringLength(LogEvent.EnvironmentMaxLength)] string? environment = null,
        CancellationToken cancellationToken = default)
    {
        return Ok(await getMetricNamesService.ExecuteAsync(from, to, applicationId, environment, cancellationToken));
    }

    /// <summary>
    ///     Receives a batch of metric points from an Application.
    /// </summary>
    /// <remarks>
    ///     A metric point holds the measurements of one metric over a short interval, aggregated by the sender: their count,
    ///     sum, minimum and maximum. The body is a JSON array of metric points and can be compressed with gzip
    ///     (<c>Content-Encoding: gzip</c>). The Application is taken from the API Key in the header <c>X-Logbook-Key</c>. If one
    ///     metric point is not valid, none are accepted. Accepted metric points are written to the database shortly afterwards.
    /// </remarks>
    /// <param name="metrics">The metric points.</param>
    /// <returns>202 Accepted with the number of accepted metric points.</returns>
    /// <response code="202">Responds with the number of accepted metric points, which are queued for writing.</response>
    /// <response code="400">Responds with problem details if the body is not valid, e.g. malformed JSON or gzip, an empty batch, or a metric point that is not valid.</response>
    /// <response code="413">Responds with problem details if the body is too large, also after decompression.</response>
    /// <response code="415">Responds with problem details if the body is not JSON or the content encoding is not gzip.</response>
    /// <response code="429">Responds with problem details and a Retry-After header if the API Key sends too many requests. None of the metric points were accepted, so the batch can be sent again.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    /// <response code="503">Responds with problem details and a Retry-After header if the queue is full or the database cannot be reached to check the API Key. None of the metric points were accepted, so the batch can be sent again.</response>
    [HttpPost]
    [Authorize(AuthorizationPolicies.ApplicationApiKey)]
    [EnableRateLimiting(RateLimitPolicies.ApiKey)]
    [ServiceFilter<LogRequestBodyFilter>]
    [ProducesResponseType<IngestMetricsResponse>(StatusCodes.Status202Accepted, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Ingest([FromBody] IReadOnlyList<MetricRequest?> metrics)
    {
        var applicationId = Guid.Parse(User.FindFirstValue(ApiKeyClaimTypes.ApplicationId)!, CultureInfo.InvariantCulture);

        return Accepted(await ingestMetricsService.ExecuteAsync(applicationId, metrics));
    }
}
