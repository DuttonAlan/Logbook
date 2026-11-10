// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.ComponentModel.DataAnnotations;

using Logbook.Api.Authorization;
using Logbook.Core.Application.Dashboard.GetOverview;
using Logbook.Core.Domain.Entities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Logbook.Api.Controllers;

/// <summary>
///     Provides the Endpoint for the overview on the dashboard.
/// </summary>
/// <param name="getOverviewService">The Service to get the overview.</param>
[ApiController]
[Route("api/v1/overview")]
[Tags("Overview")]
[Authorize(AuthorizationPolicies.AdminApi)]
public sealed class OverviewController(GetOverviewService getOverviewService) : ControllerBase
{
    /// <summary>
    ///     Gets the overview of a time range: Log Events over time by level, requests over time with those that failed, and
    ///     the slowest requests.
    /// </summary>
    /// <remarks>
    ///     The width of a bucket depends on the length of the time range, as for metrics. The error rate is the number of
    ///     failed requests divided by the number of requests. The most frequent exceptions come from
    ///     <c>GET /api/v1/exceptions?sort=Count</c>.
    /// </remarks>
    /// <param name="from" example="2026-11-04T00:00:00Z">The start of the range. Default: one day before <c>to</c>.</param>
    /// <param name="to" example="2026-11-05T00:00:00Z">The end of the range. Default: now.</param>
    /// <param name="applicationId" example="0199d1a2-7c3e-7b4a-9f10-5c963f66afa6">Only data of this Application.</param>
    /// <param name="environment" example="Production">Only data of this Environment, compared exactly.</param>
    /// <param name="slowest" example="10">The number of slowest requests to return, at most 50.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>200 OK with the overview.</returns>
    /// <response code="200">Responds with the overview.</response>
    /// <response code="400">Responds with problem details if a parameter is not valid, e.g. an empty or too long time range.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    /// <response code="503">Responds with problem details and a Retry-After header if the database cannot be reached.</response>
    [HttpGet]
    [ProducesResponseType<GetOverviewResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Get(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] Guid? applicationId = null,
        [FromQuery, StringLength(LogEvent.EnvironmentMaxLength)] string? environment = null,
        [FromQuery, Range(0, GetOverviewService.MaxSlowestRequests)] int slowest = 10,
        CancellationToken cancellationToken = default)
    {
        return Ok(await getOverviewService.ExecuteAsync(from, to, applicationId, environment, slowest, cancellationToken));
    }
}
