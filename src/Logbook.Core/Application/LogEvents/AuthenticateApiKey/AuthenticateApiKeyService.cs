// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;

namespace Logbook.Core.Application.LogEvents.AuthenticateApiKey;

/// <summary>
///     The Service to authenticate a request by the API Key of an Application.
/// </summary>
/// <param name="applicationRepository">An Instance of a IApplicationRepository to find the API Key.</param>
/// <param name="apiKeyHasher">The hasher to compute the hash to look up.</param>
/// <param name="timeProvider">The TimeProvider to check the expiry.</param>
public sealed class AuthenticateApiKeyService(
    IApplicationRepository applicationRepository,
    IApiKeyHasher apiKeyHasher,
    TimeProvider timeProvider)
{
    /// <summary>
    ///     Finds the active API Key with the given plain-text value.
    /// </summary>
    /// <param name="apiKey">The plain-text API Key from the request.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The API Key, or null if it is unknown, revoked or expired.</returns>
    public async Task<AuthenticatedApiKey?> ExecuteAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        var found = await applicationRepository.GetApiKeyByHash(apiKeyHasher.Hash(apiKey), cancellationToken);

        return found is not null && found.IsActive(timeProvider.GetUtcNow().UtcDateTime)
            ? new AuthenticatedApiKey(found.Id, found.ApplicationId)
            : null;
    }
}
