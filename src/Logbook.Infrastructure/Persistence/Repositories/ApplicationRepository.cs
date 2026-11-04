// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace Logbook.Infrastructure.Persistence.Repositories;

/// <inheritdoc/>
/// <param name="context">The DbContext to access the Applications.</param>
public sealed class ApplicationRepository(LogbookContext context) : IApplicationRepository
{
    /// <inheritdoc/>
    public async Task CreateApplication(Application application, CancellationToken cancellationToken = default)
    {
        context.Applications.Add(application);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Application?> GetApplicationById(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.Applications
            .Include(application => application.ApiKeys)
            .FirstOrDefaultAsync(application => application.Id == id, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Application>> GetApplications(CancellationToken cancellationToken = default)
    {
        return await context.Applications
            .AsNoTracking()
            .OrderBy(application => application.Name)
            .ThenBy(application => application.Id)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> NameExists(string name, Guid? excludedId = null, CancellationToken cancellationToken = default)
    {
        return await context.Applications
            .AnyAsync(application => application.Name == name && application.Id != excludedId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteApplication(Application application, CancellationToken cancellationToken = default)
    {
        // API Keys are removed with the Application (cascade delete).
        context.Applications.Remove(application);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await context.SaveChangesAsync(cancellationToken);
    }
}
