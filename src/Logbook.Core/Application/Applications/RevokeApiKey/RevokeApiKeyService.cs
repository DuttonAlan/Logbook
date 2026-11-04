// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Application.Exceptions;

using Microsoft.Extensions.Logging;

namespace Logbook.Core.Application.Applications.RevokeApiKey;

/// <summary>
///     The Service to revoke an API Key of an Application.
/// </summary>
/// <param name="applicationRepository">An Instance of a IApplicationRepository to change the Application.</param>
/// <param name="timeProvider">The TimeProvider for the revocation time.</param>
/// <param name="logger">The Logger to record the revocation.</param>
public sealed class RevokeApiKeyService(
    IApplicationRepository applicationRepository,
    TimeProvider timeProvider,
    ILogger<RevokeApiKeyService> logger)
{
    /// <summary>
    ///     Revokes an API Key of an Application, so it can no longer be used. Revoking an already revoked API Key keeps
    ///     its original revocation time. The API Key is kept for history.
    /// </summary>
    /// <param name="applicationId">Id of the Application.</param>
    /// <param name="apiKeyId">Id of the API Key.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>A Task that completes once the API Key is revoked.</returns>
    /// <exception cref="NotFoundException">Thrown when the Application or the API Key does not exist.</exception>
    public async Task ExecuteAsync(
        Guid applicationId,
        Guid apiKeyId,
        CancellationToken cancellationToken = default)
    {
        var application = await applicationRepository.GetApplicationById(applicationId, cancellationToken)
            ?? throw new NotFoundException($"Application '{applicationId}' was not found.");

        var apiKey = application.ApiKeys.FirstOrDefault(apiKey => apiKey.Id == apiKeyId)
            ?? throw new NotFoundException($"API Key '{apiKeyId}' was not found.");

        if (apiKey.RevokedAt is not null)
        {
            return;
        }

        application.RevokeApiKey(apiKey.Id, timeProvider.GetUtcNow().UtcDateTime);

        await applicationRepository.SaveChangesAsync(cancellationToken);

        logger.ApiKeyRevoked(application.Id, apiKey.Id);
    }
}
