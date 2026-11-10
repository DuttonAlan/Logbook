// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;

using Microsoft.Extensions.Logging;

namespace Logbook.Core.Application.SavedSearches.Delete;

/// <summary>
///     The Service to delete a saved search.
/// </summary>
/// <param name="savedSearchRepository">An Instance of a ISavedSearchRepository to delete the saved search.</param>
/// <param name="logger">The Logger to record the deletion.</param>
public sealed class DeleteSavedSearchService(
    ISavedSearchRepository savedSearchRepository,
    ILogger<DeleteSavedSearchService> logger)
{
    /// <summary>
    ///     Deletes a saved search. Deleting one that does not exist also succeeds.
    /// </summary>
    /// <param name="id">Id of the saved search.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>A Task that completes once the saved search is deleted.</returns>
    public async Task ExecuteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var savedSearch = await savedSearchRepository.GetById(id, cancellationToken);

        if (savedSearch is null)
        {
            return;
        }

        await savedSearchRepository.Delete(savedSearch, cancellationToken);

        logger.SavedSearchDeleted(savedSearch.Id);
    }
}
