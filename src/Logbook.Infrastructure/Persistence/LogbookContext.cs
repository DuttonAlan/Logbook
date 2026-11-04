// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace Logbook.Infrastructure.Persistence;

/// <summary>
///     The DbContext to interact with the Database.
/// </summary>
/// <remarks>
///     Each entity is mapped by an <see cref="IEntityTypeConfiguration{TEntity}"/> in <c>Persistence/Configurations</c>
///     to a table in one of the <see cref="Schemas"/>.
/// </remarks>
public sealed class LogbookContext(DbContextOptions<LogbookContext> options) : DbContext(options)
{
    /// <summary>
    ///     The Applications Table. API Keys are loaded and saved together with their Application.
    /// </summary>
    public DbSet<Application> Applications => Set<Application>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(LogbookContext).Assembly);
    }
}
