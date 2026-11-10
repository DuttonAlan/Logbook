// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;

namespace Logbook.Core.Application.SavedSearches.GetAll;

/// <summary>
///     The Service to get all saved searches.
/// </summary>
/// <param name="savedSearchRepository">An Instance of a ISavedSearchRepository to read the saved searches.</param>
public sealed class GetAllSavedSearchesService(ISavedSearchRepository savedSearchRepository)
{
    /// <summary>
    ///     Gets all saved searches, ordered by their Name.
    /// </summary>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The saved searches.</returns>
    public async Task<IReadOnlyList<SavedSearchResponse>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var savedSearches = await savedSearchRepository.GetAll(cancellationToken);

        return [.. savedSearches.Select(SavedSearchResponse.From)];
    }
}
