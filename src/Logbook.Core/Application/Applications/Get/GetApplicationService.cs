// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;

namespace Logbook.Core.Application.Applications.Get;

/// <summary>
///     The Service to get an Application.
/// </summary>
/// <param name="applicationRepository">An Instance of a IApplicationRepository to get an Application.</param>
/// <param name="timeProvider">The TimeProvider to decide whether API Keys are active.</param>
public sealed class GetApplicationService(
    IApplicationRepository applicationRepository,
    TimeProvider timeProvider)
{
    /// <summary>
    ///     Gets an Application based on the Id, including the metadata of its API Keys.
    /// </summary>
    /// <param name="id">Id of the Application.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The Application, or null if the Application does not exist.</returns>
    public async Task<GetApplicationResponse?> ExecuteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var application = await applicationRepository.GetApplicationById(id, cancellationToken);

        if (application is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        var apiKeys = application.ApiKeys
            .OrderBy(apiKey => apiKey.CreatedAt)
            .ThenBy(apiKey => apiKey.Id)
            .Select(apiKey => new ApiKeyResponse(
                apiKey.Id,
                apiKey.CreatedAt,
                apiKey.ExpiresAt,
                apiKey.RevokedAt,
                apiKey.IsActive(now)))
            .ToList();

        return new GetApplicationResponse(
            application.Id,
            application.Name,
            apiKeys,
            application.CreatedAt,
            application.ModifiedAt);
    }
}
