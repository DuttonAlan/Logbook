// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace Logbook.Infrastructure.Persistence.Repositories;

/// <inheritdoc/>
/// <param name="context">The DbContext to access the saved searches.</param>
public sealed class SavedSearchRepository(LogbookContext context) : ISavedSearchRepository
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<SavedSearch>> GetAll(CancellationToken cancellationToken = default)
    {
        return await context.SavedSearches
            .AsNoTracking()
            .OrderBy(savedSearch => savedSearch.Name)
            .ThenBy(savedSearch => savedSearch.Id)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SavedSearch?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.SavedSearches.FirstOrDefaultAsync(savedSearch => savedSearch.Id == id, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> Count(CancellationToken cancellationToken = default)
    {
        return await context.SavedSearches.CountAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> NameExists(string name, Guid? excludedId = null, CancellationToken cancellationToken = default)
    {
        return await context.SavedSearches
            .AnyAsync(savedSearch => savedSearch.Name == name && savedSearch.Id != excludedId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task Add(SavedSearch savedSearch, CancellationToken cancellationToken = default)
    {
        context.SavedSearches.Add(savedSearch);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task Delete(SavedSearch savedSearch, CancellationToken cancellationToken = default)
    {
        context.SavedSearches.Remove(savedSearch);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await context.SaveChangesAsync(cancellationToken);
    }
}
