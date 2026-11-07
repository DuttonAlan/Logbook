// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.ComponentModel.DataAnnotations;

using Logbook.Api.Authorization;
using Logbook.Core.Application.ExceptionGroups.Search;
using Logbook.Core.Domain.Entities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Logbook.Api.Controllers;

/// <summary>
///     Provides the Endpoint for the exceptions in the Log Events, grouped by type and stack fingerprint.
/// </summary>
/// <param name="searchExceptionGroupsService">The Service to find the groups.</param>
[ApiController]
[Route("api/v1/exceptions")]
[Tags("Exceptions")]
[Authorize(AuthorizationPolicies.AdminApi)]
public sealed class ExceptionsController(SearchExceptionGroupsService searchExceptionGroupsService) : ControllerBase
{
    private const int MaxLimit = 200;

    /// <summary>
    ///     Finds the groups of the exceptions in the Log Events.
    /// </summary>
    /// <remarks>
    ///     A group holds the exceptions of one Application with the same type and stack fingerprint. To see each occurrence,
    ///     search the Log Events with <c>exceptionFingerprint</c>.
    /// </remarks>
    /// <param name="from" example="2026-11-04T00:00:00Z">Only count occurrences at or after this point in time.</param>
    /// <param name="to" example="2026-11-05T00:00:00Z">Only count occurrences before this point in time.</param>
    /// <param name="applicationId" example="0199d1a2-7c3e-7b4a-9f10-5c963f66afa6">Only groups of this Application.</param>
    /// <param name="environment" example="Production">Only occurrences in this Environment, compared exactly.</param>
    /// <param name="sort" example="Count">The order of the groups: LastOccurrence, or Count.</param>
    /// <param name="limit" example="50">The maximum number of groups, at most 200.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>200 OK with the groups.</returns>
    /// <response code="200">Responds with the groups.</response>
    /// <response code="400">Responds with problem details if a parameter is not valid, e.g. an unknown sort or an empty time range.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    /// <response code="503">Responds with problem details and a Retry-After header if the database cannot be reached.</response>
    [HttpGet]
    [ProducesResponseType<SearchExceptionGroupsResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Search(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] Guid? applicationId = null,
        [FromQuery, StringLength(LogEvent.EnvironmentMaxLength)] string? environment = null,
        [FromQuery] ExceptionGroupSort sort = ExceptionGroupSort.LastOccurrence,
        [FromQuery, Range(1, MaxLimit)] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        return Ok(await searchExceptionGroupsService.ExecuteAsync(from, to, applicationId, environment, sort, limit, cancellationToken));
    }
}
