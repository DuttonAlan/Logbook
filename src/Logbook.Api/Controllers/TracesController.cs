// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.ComponentModel.DataAnnotations;

using Logbook.Api.Authorization;
using Logbook.Core.Application.Traces.Get;
using Logbook.Core.Domain.Entities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Logbook.Api.Controllers;

/// <summary>
///     Provides the Endpoint for transactions: everything stored for one trace.
/// </summary>
/// <param name="getTraceService">The Service to get a trace.</param>
[ApiController]
[Route("api/v1/traces")]
[Tags("Traces")]
[Authorize(AuthorizationPolicies.AdminApi)]
public sealed class TracesController(GetTraceService getTraceService) : ControllerBase
{
    /// <summary>
    ///     Gets the requests, dependencies and Log Events of a trace, oldest first, across all Applications and Environments.
    /// </summary>
    /// <param name="traceId" example="4bf92f3577b34da6a3ce929d0e0e4736">The W3C Trace Id, compared exactly.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>200 OK with the trace.</returns>
    /// <response code="200">Responds with the trace, with at most 1000 spans and 1000 Log Events.</response>
    /// <response code="400">Responds with problem details if the Trace Id is too long.</response>
    /// <response code="404">Responds with problem details if nothing is stored for the trace.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    /// <response code="503">Responds with problem details and a Retry-After header if the database cannot be reached.</response>
    [HttpGet("{traceId}")]
    [ProducesResponseType<GetTraceResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Get(
        [FromRoute, StringLength(LogEvent.TraceFieldMaxLength)] string traceId,
        CancellationToken cancellationToken = default)
    {
        return Ok(await getTraceService.ExecuteAsync(traceId, cancellationToken));
    }
}
