// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logbook.Infrastructure.Persistence.Configurations;

/// <summary>
///     Configures the database mapping of the saved search.
/// </summary>
public sealed class SavedSearchConfiguration : IEntityTypeConfiguration<SavedSearch>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<SavedSearch> builder)
    {
        builder.ToTable("saved_searches", Schemas.Config);

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(SavedSearch.NameMaxLength);

        builder.HasIndex(x => x.Name)
            .IsUnique();

        builder.Property(x => x.Query)
            .IsRequired()
            .HasMaxLength(SavedSearch.QueryMaxLength);

        builder.Property(x => x.CreatedAt)
            .IsRequired();
    }
}
