// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Api.Authorization;
using Logbook.Core.Application.Applications;
using Logbook.Core.Application.Applications.Create;
using Logbook.Core.Application.Applications.Delete;
using Logbook.Core.Application.Applications.Get;
using Logbook.Core.Application.Applications.GetAll;
using Logbook.Core.Application.Applications.IssueApiKey;
using Logbook.Core.Application.Applications.Rename;
using Logbook.Core.Application.Applications.RevokeApiKey;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Logbook.Api.Controllers;

/// <summary>
///     Provides the Endpoints to manage Applications and their API Keys.
/// </summary>
/// <remarks>
///     The actions deliberately have no <see cref="ConsumesAttribute"/>: routing would skip an action for another
///     content type, so the request would end in the catch-all for unknown API routes and return 404 instead of 415.
/// </remarks>
/// <param name="createApplicationService">The Service to create an Application.</param>
/// <param name="deleteApplicationService">The Service to delete an Application.</param>
/// <param name="getAllApplicationsService">The Service to get all Applications.</param>
/// <param name="getApplicationService">The Service to get an Application.</param>
/// <param name="issueApiKeyService">The Service to issue an API Key.</param>
/// <param name="renameApplicationService">The Service to rename an Application.</param>
/// <param name="revokeApiKeyService">The Service to revoke an API Key.</param>
[ApiController]
[Authorize(AuthorizationPolicies.AdminApi)]
[Route("api/v1/applications")]
[Tags("Applications")]
public sealed class ApplicationsController(
    CreateApplicationService createApplicationService,
    DeleteApplicationService deleteApplicationService,
    GetAllApplicationsService getAllApplicationsService,
    GetApplicationService getApplicationService,
    IssueApiKeyService issueApiKeyService,
    RenameApplicationService renameApplicationService,
    RevokeApiKeyService revokeApiKeyService) : ControllerBase
{
    /// <summary>
    ///     Creates an Application.
    /// </summary>
    /// <remarks>
    ///     The Application gets its first API Key, which does not expire and is returned in plain text only in this
    ///     response.
    /// </remarks>
    /// <param name="request">The request to create the Application.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>201 Created with the Id of the Application and its first API Key.</returns>
    /// <response code="201">Responds with the Id of the created Application, its first API Key, and its location in the Location header.</response>
    /// <response code="400">Responds with validation problem details if the request body is invalid, e.g. an empty Name.</response>
    /// <response code="409">Responds with problem details if another Application already has the Name.</response>
    /// <response code="415">Responds with problem details if the request body is not JSON.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    [HttpPost]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<CreateApplicationResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<IActionResult> Create(
        CreateApplicationRequest request,
        CancellationToken cancellationToken)
    {
        var response = await createApplicationService.ExecuteAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    /// <summary>
    ///     Deletes the Application based on the given Id.
    /// </summary>
    /// <remarks>
    ///     The Application is removed together with its API Keys and Log Events, which can take a while for many Log Events.
    ///     This operation is idempotent: deleting an Application that does not exist also succeeds, and repeating an
    ///     interrupted request finishes deleting its Log Events.
    /// </remarks>
    /// <param name="id" example="0199d1a2-7c3e-7b4a-9f10-5c963f66afa6">Id of the Application.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>204 No Content.</returns>
    /// <response code="204">Responds with no content once the Application was deleted, or if no Application with the given Id exists.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        await deleteApplicationService.ExecuteAsync(id, cancellationToken);

        return NoContent();
    }

    /// <summary>
    ///     Gets all Applications, ordered by their Name.
    /// </summary>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>200 OK with all Applications.</returns>
    /// <response code="200">Responds with all Applications, or an empty list if no Applications exist.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<GetAllApplicationsResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var applications = await getAllApplicationsService.ExecuteAsync(cancellationToken);

        return Ok(applications);
    }

    /// <summary>
    ///     Gets the Application based on the given Id.
    /// </summary>
    /// <remarks>
    ///     Includes the metadata of all API Keys. API Keys themselves are never returned.
    /// </remarks>
    /// <param name="id" example="0199d1a2-7c3e-7b4a-9f10-5c963f66afa6">Id of the Application.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>200 OK with the Application, or 404 Not Found if the Application does not exist.</returns>
    /// <response code="200">Responds with the Application matching the given Id.</response>
    /// <response code="404">Responds with problem details if no Application with the given Id exists.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<GetApplicationResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<IActionResult> Get(
        Guid id,
        CancellationToken cancellationToken)
    {
        var application = await getApplicationService.ExecuteAsync(id, cancellationToken);

        if (application is null)
        {
            return NotFound();
        }

        return Ok(application);
    }

    /// <summary>
    ///     Renames the Application based on the given Id.
    /// </summary>
    /// <remarks>
    ///     The API Keys of the Application stay valid. Renaming an Application to its current Name also succeeds.
    /// </remarks>
    /// <param name="id" example="0199d1a2-7c3e-7b4a-9f10-5c963f66afa6">Id of the Application.</param>
    /// <param name="request">The request to rename the Application.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>204 No Content once the Application is renamed.</returns>
    /// <response code="204">Responds with no content once the Application was renamed.</response>
    /// <response code="400">Responds with validation problem details if the request body is invalid, e.g. an empty Name.</response>
    /// <response code="404">Responds with problem details if no Application with the given Id exists.</response>
    /// <response code="409">Responds with problem details if another Application already has the Name.</response>
    /// <response code="415">Responds with problem details if the request body is not JSON.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<IActionResult> Rename(
        Guid id,
        RenameApplicationRequest request,
        CancellationToken cancellationToken)
    {
        await renameApplicationService.ExecuteAsync(id, request, cancellationToken);

        return NoContent();
    }

    /// <summary>
    ///     Issues a new API Key for the Application.
    /// </summary>
    /// <remarks>
    ///     Existing API Keys stay active, so an API Key can be rotated without downtime: issue a new API Key, update the
    ///     application, then revoke the old API Key. The plain-text API Key is returned only in this response. The
    ///     request body is optional.
    /// </remarks>
    /// <param name="id" example="0199d1a2-7c3e-7b4a-9f10-5c963f66afa6">Id of the Application.</param>
    /// <param name="request">The optional request with the expiry of the API Key.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>201 Created with the new API Key.</returns>
    /// <response code="201">Responds with the new API Key, including its plain-text value.</response>
    /// <response code="400">Responds with problem details if the expiry is not in the future.</response>
    /// <response code="404">Responds with problem details if no Application with the given Id exists.</response>
    /// <response code="415">Responds with problem details if a request body is sent that is not JSON.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    [HttpPost("{id:guid}/api-keys")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<IssuedApiKeyResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<IActionResult> IssueApiKey(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] IssueApiKeyRequest? request,
        CancellationToken cancellationToken)
    {
        var apiKey = await issueApiKeyService.ExecuteAsync(
            id,
            request ?? new IssueApiKeyRequest(ExpiresAt: null),
            cancellationToken);

        return CreatedAtAction(nameof(Get), new { id }, apiKey);
    }

    /// <summary>
    ///     Revokes an API Key of the Application.
    /// </summary>
    /// <remarks>
    ///     A revoked API Key can no longer be used, but is kept for history.
    ///     This operation is idempotent: revoking an already revoked API Key also succeeds and keeps the original revocation time.
    /// </remarks>
    /// <param name="id" example="0199d1a2-7c3e-7b4a-9f10-5c963f66afa6">Id of the Application.</param>
    /// <param name="apiKeyId" example="0199d1a2-7c3e-7b4a-9f10-6c963f66afa6">Id of the API Key.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>204 No Content once the API Key is revoked.</returns>
    /// <response code="204">Responds with no content once the API Key was revoked.</response>
    /// <response code="404">Responds with problem details if no Application or no API Key with the given Id exists.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    [HttpPost("{id:guid}/api-keys/{apiKeyId:guid}/revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<IActionResult> RevokeApiKey(
        Guid id,
        Guid apiKeyId,
        CancellationToken cancellationToken)
    {
        await revokeApiKeyService.ExecuteAsync(id, apiKeyId, cancellationToken);

        return NoContent();
    }
}
