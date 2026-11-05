// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Domain.Exceptions;

namespace Logbook.Core.Application.Applications;

/// <summary>
///     Generates a new API Key and adds its hash to the Application.
/// </summary>
/// <param name="apiKeyGenerator">The generator for the plain-text API Key.</param>
/// <param name="apiKeyHasher">The hasher for the stored API Key.</param>
/// <param name="timeProvider">The TimeProvider for the creation time.</param>
public sealed class ApiKeyIssuer(IApiKeyGenerator apiKeyGenerator, IApiKeyHasher apiKeyHasher, TimeProvider timeProvider)
{
    /// <summary>
    ///     Generates a new API Key and adds its hash to the Application. The Application still has to be saved.
    /// </summary>
    /// <param name="application">The Application to add the API Key to.</param>
    /// <param name="expiresAt">Point in time (UTC) when the API Key expires, or null if it does not expire.</param>
    /// <returns>The new API Key including its plain-text value.</returns>
    /// <exception cref="DomainValidationException">Thrown when the expiry is not in the future.</exception>
    public IssuedApiKeyResponse Issue(Domain.Entities.Application application, DateTime? expiresAt)
    {
        ArgumentNullException.ThrowIfNull(application);

        var plainTextKey = apiKeyGenerator.Generate();
        var apiKey = application.AddApiKey(apiKeyHasher.Hash(plainTextKey), timeProvider.GetUtcNow().UtcDateTime, expiresAt);

        return new IssuedApiKeyResponse(apiKey.Id, plainTextKey, apiKey.CreatedAt, apiKey.ExpiresAt);
    }
}
