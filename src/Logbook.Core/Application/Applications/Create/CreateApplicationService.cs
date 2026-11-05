// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Application.Exceptions;
using Logbook.Core.Domain.Exceptions;

using Microsoft.Extensions.Logging;

namespace Logbook.Core.Application.Applications.Create;

/// <summary>
///     The Service to create a new Application.
/// </summary>
/// <param name="applicationRepository">An Instance of a IApplicationRepository to create the Application.</param>
/// <param name="apiKeyIssuer">The issuer for the first API Key.</param>
/// <param name="timeProvider">The TimeProvider for the creation time.</param>
/// <param name="logger">The Logger to record the creation.</param>
public sealed class CreateApplicationService(
    IApplicationRepository applicationRepository,
    ApiKeyIssuer apiKeyIssuer,
    TimeProvider timeProvider,
    ILogger<CreateApplicationService> logger)
{
    /// <summary>
    ///     Creates a new Application with its first API Key, which does not expire.
    /// </summary>
    /// <param name="request">The request to create the Application.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The Id of the Application and its first plain-text API Key, which is only returned once.</returns>
    /// <exception cref="DomainValidationException">Thrown when the Name is invalid.</exception>
    /// <exception cref="ConflictException">Thrown when the Name is already taken.</exception>
    public async Task<CreateApplicationResponse> ExecuteAsync(
        CreateApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        var application = Domain.Entities.Application.Create(request.Name, timeProvider.GetUtcNow().UtcDateTime);

        if (await applicationRepository.NameExists(application.Name, cancellationToken: cancellationToken))
        {
            throw new ConflictException($"An Application named '{application.Name}' already exists.");
        }

        var apiKey = apiKeyIssuer.Issue(application, expiresAt: null);

        await applicationRepository.CreateApplication(application, cancellationToken);

        logger.ApplicationCreated(application.Id, application.Name);
        logger.ApiKeyIssued(application.Id, apiKey.Id);

        return new CreateApplicationResponse(application.Id, apiKey);
    }
}
