// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Application.Exceptions;
using Logbook.Core.Domain.Entities;
using Logbook.Core.Domain.Exceptions;

using Microsoft.Extensions.Logging;

namespace Logbook.Core.Application.SavedSearches.Create;

/// <summary>
///     The Service to save a search of the log explorer.
/// </summary>
/// <param name="savedSearchRepository">An Instance of a ISavedSearchRepository to store the saved search.</param>
/// <param name="timeProvider">The TimeProvider for the creation time.</param>
/// <param name="logger">The Logger to record the creation.</param>
public sealed class CreateSavedSearchService(
    ISavedSearchRepository savedSearchRepository,
    TimeProvider timeProvider,
    ILogger<CreateSavedSearchService> logger)
{
    /// <summary>
    ///     The most saved searches Logbook keeps, so that the list stays usable and bounded.
    /// </summary>
    public const int MaxSavedSearches = 500;

    /// <summary>
    ///     Saves a search under a name.
    /// </summary>
    /// <param name="request">The name and the query string.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The saved search.</returns>
    /// <exception cref="DomainValidationException">Thrown when the Name or the Query is not valid.</exception>
    /// <exception cref="ConflictException">Thrown when another saved search has the Name, or the limit is reached.</exception>
    public async Task<SavedSearchResponse> ExecuteAsync(SaveSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var savedSearch = SavedSearch.Create(request.Name, request.Query, timeProvider.GetUtcNow().UtcDateTime);

        if (await savedSearchRepository.NameExists(savedSearch.Name, cancellationToken: cancellationToken))
        {
            throw new ConflictException($"A saved search named '{savedSearch.Name}' already exists.");
        }

        if (await savedSearchRepository.Count(cancellationToken) >= MaxSavedSearches)
        {
            throw new ConflictException($"Logbook keeps at most {MaxSavedSearches} saved searches. Delete one first.");
        }

        await savedSearchRepository.Add(savedSearch, cancellationToken);

        logger.SavedSearchCreated(savedSearch.Id);

        return SavedSearchResponse.From(savedSearch);
    }
}
