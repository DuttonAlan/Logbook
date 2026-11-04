// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Application.Exceptions;
using Logbook.Core.Domain.Exceptions;

using Microsoft.Extensions.Logging;

namespace Logbook.Core.Application.Applications.IssueApiKey;

/// <summary>
///     The Service to issue a new API Key for an Application.
/// </summary>
/// <param name="applicationRepository">An Instance of a IApplicationRepository to change the Application.</param>
/// <param name="apiKeyIssuer">The issuer for the new API Key.</param>
/// <param name="logger">The Logger to record the issue.</param>
public sealed class IssueApiKeyService(
    IApplicationRepository applicationRepository,
    ApiKeyIssuer apiKeyIssuer,
    ILogger<IssueApiKeyService> logger)
{
    /// <summary>
    ///     Issues a new API Key for an Application. Existing API Keys stay active, which allows rotation.
    /// </summary>
    /// <param name="id">Id of the Application.</param>
    /// <param name="request">The request to issue the API Key.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The new API Key including its plain-text value, which is only returned once.</returns>
    /// <exception cref="NotFoundException">Thrown when the Application does not exist.</exception>
    /// <exception cref="DomainValidationException">Thrown when the expiry is not in the future.</exception>
    public async Task<IssuedApiKeyResponse> ExecuteAsync(
        Guid id,
        IssueApiKeyRequest request,
        CancellationToken cancellationToken = default)
    {
        var application = await applicationRepository.GetApplicationById(id, cancellationToken)
            ?? throw new NotFoundException($"Application '{id}' was not found.");

        var apiKey = apiKeyIssuer.Issue(application, request.ExpiresAt?.UtcDateTime);

        await applicationRepository.SaveChangesAsync(cancellationToken);

        logger.ApiKeyIssued(application.Id, apiKey.Id);

        return apiKey;
    }
}
