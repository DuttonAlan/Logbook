// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.Entities;

using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Logbook.Infrastructure.Persistence;

/// <summary>
///     The DbContext to interact with the Database.
/// </summary>
/// <remarks>
///     Each entity is mapped by an <see cref="IEntityTypeConfiguration{TEntity}"/> in <c>Persistence/Configurations</c>
///     to a table in one of the <see cref="Schemas"/>.
/// </remarks>
public sealed class LogbookContext(DbContextOptions<LogbookContext> options) : DbContext(options), IDataProtectionKeyContext
{
    /// <summary>
    ///     The Applications Table. API Keys are loaded and saved together with their Application.
    /// </summary>
    public DbSet<Application> Applications => Set<Application>();

    /// <summary>
    ///     The Log Events Table, partitioned by day.
    /// </summary>
    public DbSet<LogEvent> LogEvents => Set<LogEvent>();

    /// <summary>
    ///     The Spans Table, partitioned by day.
    /// </summary>
    public DbSet<TraceSpan> Spans => Set<TraceSpan>();

    /// <inheritdoc/>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(LogbookContext).Assembly);
    }
}
