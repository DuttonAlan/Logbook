// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Application.Exceptions;
using Logbook.Core.Domain.Exceptions;

using Microsoft.Extensions.Logging;

namespace Logbook.Core.Application.SavedSearches.Change;

/// <summary>
///     The Service to rename a saved search or replace its query.
/// </summary>
/// <param name="savedSearchRepository">An Instance of a ISavedSearchRepository to change the saved search.</param>
/// <param name="timeProvider">The TimeProvider for the modification time.</param>
/// <param name="logger">The Logger to record the change.</param>
public sealed class ChangeSavedSearchService(
    ISavedSearchRepository savedSearchRepository,
    TimeProvider timeProvider,
    ILogger<ChangeSavedSearchService> logger)
{
    /// <summary>
    ///     Changes the Name and the Query of a saved search.
    /// </summary>
    /// <param name="id">Id of the saved search.</param>
    /// <param name="request">The new Name and query string.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The changed saved search.</returns>
    /// <exception cref="NotFoundException">Thrown when the saved search does not exist.</exception>
    /// <exception cref="DomainValidationException">Thrown when the Name or the Query is not valid.</exception>
    /// <exception cref="ConflictException">Thrown when another saved search has the Name.</exception>
    public async Task<SavedSearchResponse> ExecuteAsync(Guid id, SaveSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var savedSearch = await savedSearchRepository.GetById(id, cancellationToken)
            ?? throw new NotFoundException($"Saved search '{id}' was not found.");

        savedSearch.Change(request.Name, request.Query, timeProvider.GetUtcNow().UtcDateTime);

        if (await savedSearchRepository.NameExists(savedSearch.Name, savedSearch.Id, cancellationToken))
        {
            throw new ConflictException($"A saved search named '{savedSearch.Name}' already exists.");
        }

        await savedSearchRepository.SaveChangesAsync(cancellationToken);

        logger.SavedSearchChanged(savedSearch.Id);

        return SavedSearchResponse.From(savedSearch);
    }
}
