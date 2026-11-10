// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.Entities;

namespace Logbook.Core.Abstractions;

/// <summary>
///     Defines a contract for operations on saved searches.
/// </summary>
/// <remarks>
///     Saved searches are changed by loading them tracked via <see cref="GetById"/>, changing them through their methods and
///     calling <see cref="SaveChangesAsync"/>.
/// </remarks>
public interface ISavedSearchRepository
{
    /// <summary>
    ///     Gets all saved searches, ordered by their Name.
    /// </summary>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The untracked saved searches.</returns>
    public Task<IReadOnlyList<SavedSearch>> GetAll(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets a saved search by Id.
    /// </summary>
    /// <param name="id">Id of the saved search.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The tracked saved search, or null if it does not exist.</returns>
    public Task<SavedSearch?> GetById(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Checks whether another saved search already has the given Name.
    /// </summary>
    /// <param name="name">The Name, compared character by character.</param>
    /// <param name="excludedId">Id of a saved search to ignore, e.g. the one being changed.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>True if the Name is taken.</returns>
    public Task<bool> NameExists(string name, Guid? excludedId = null, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stores a new saved search, unless there are already as many as allowed. Counting and storing are one step, so
    ///     that requests at the same time cannot together go over the limit.
    /// </summary>
    /// <param name="savedSearch">The saved search.</param>
    /// <param name="maxCount">The most saved searches there may be.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns><see langword="true"/> if it was stored, or <see langword="false"/> if the limit is reached.</returns>
    public Task<bool> TryAdd(SavedSearch savedSearch, int maxCount, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes a saved search.
    /// </summary>
    /// <param name="savedSearch">The tracked saved search.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>A Task that completes once the saved search is deleted.</returns>
    public Task Delete(SavedSearch savedSearch, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Saves all changes made to tracked saved searches.
    /// </summary>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>A Task that completes once the changes are saved.</returns>
    public Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
