// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Api.Authorization;
using Logbook.Core.Application.SavedSearches;
using Logbook.Core.Application.SavedSearches.Change;
using Logbook.Core.Application.SavedSearches.Create;
using Logbook.Core.Application.SavedSearches.Delete;
using Logbook.Core.Application.SavedSearches.GetAll;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Logbook.Api.Controllers;

/// <summary>
///     Provides the Endpoints to manage the saved searches of the log explorer.
/// </summary>
/// <remarks>
///     Saved searches are shared by everyone who can sign in. The actions deliberately have no
///     <see cref="ConsumesAttribute"/>: routing would skip an action for another content type, so the request would end in
///     the catch-all for unknown API routes and return 404 instead of 415.
/// </remarks>
/// <param name="changeSavedSearchService">The Service to change a saved search.</param>
/// <param name="createSavedSearchService">The Service to create a saved search.</param>
/// <param name="deleteSavedSearchService">The Service to delete a saved search.</param>
/// <param name="getAllSavedSearchesService">The Service to get all saved searches.</param>
[ApiController]
[Authorize(AuthorizationPolicies.AdminApi)]
[Route("api/v1/saved-searches")]
[Tags("Saved searches")]
public sealed class SavedSearchesController(
    ChangeSavedSearchService changeSavedSearchService,
    CreateSavedSearchService createSavedSearchService,
    DeleteSavedSearchService deleteSavedSearchService,
    GetAllSavedSearchesService getAllSavedSearchesService) : ControllerBase
{
    /// <summary>
    ///     Gets all saved searches, ordered by their Name.
    /// </summary>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>200 OK with all saved searches.</returns>
    /// <response code="200">Responds with all saved searches, or an empty list if there are none.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    /// <response code="503">Responds with problem details and a Retry-After header if the database cannot be reached.</response>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SavedSearchResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await getAllSavedSearchesService.ExecuteAsync(cancellationToken));
    }

    /// <summary>
    ///     Saves a search of the log explorer under a name.
    /// </summary>
    /// <remarks>
    ///     The query is the query string of the log explorer, which holds all of its filters. Logbook keeps at most 500 saved
    ///     searches.
    /// </remarks>
    /// <param name="request">The Name and the query string.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>201 Created with the saved search.</returns>
    /// <response code="201">Responds with the saved search.</response>
    /// <response code="400">Responds with validation problem details if the request body is invalid, e.g. an empty Name or a query with spaces.</response>
    /// <response code="409">Responds with problem details if another saved search already has the Name, or 500 saved searches exist.</response>
    /// <response code="415">Responds with problem details if the request body is not JSON.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    /// <response code="503">Responds with problem details and a Retry-After header if the database cannot be reached.</response>
    [HttpPost]
    [ProducesResponseType<SavedSearchResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Create(SaveSearchRequest request, CancellationToken cancellationToken)
    {
        var response = await createSavedSearchService.ExecuteAsync(request, cancellationToken);

        return Created($"/api/v1/saved-searches/{response.Id}", response);
    }

    /// <summary>
    ///     Renames a saved search or replaces its query.
    /// </summary>
    /// <param name="id" example="0199d1a2-7c3e-7b4a-9f10-5c963f66afa6">Id of the saved search.</param>
    /// <param name="request">The new Name and query string.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>200 OK with the changed saved search.</returns>
    /// <response code="200">Responds with the changed saved search.</response>
    /// <response code="400">Responds with validation problem details if the request body is invalid.</response>
    /// <response code="404">Responds with problem details if no saved search with the given Id exists.</response>
    /// <response code="409">Responds with problem details if another saved search already has the Name.</response>
    /// <response code="415">Responds with problem details if the request body is not JSON.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    /// <response code="503">Responds with problem details and a Retry-After header if the database cannot be reached.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<SavedSearchResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Change(Guid id, SaveSearchRequest request, CancellationToken cancellationToken)
    {
        return Ok(await changeSavedSearchService.ExecuteAsync(id, request, cancellationToken));
    }

    /// <summary>
    ///     Deletes a saved search.
    /// </summary>
    /// <remarks>
    ///     This operation is idempotent: deleting a saved search that does not exist also succeeds.
    /// </remarks>
    /// <param name="id" example="0199d1a2-7c3e-7b4a-9f10-5c963f66afa6">Id of the saved search.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>204 No Content.</returns>
    /// <response code="204">Responds with no content once the saved search was deleted, or if it does not exist.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    /// <response code="503">Responds with problem details and a Retry-After header if the database cannot be reached.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await deleteSavedSearchService.ExecuteAsync(id, cancellationToken);

        return NoContent();
    }
}
