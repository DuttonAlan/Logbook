// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logbook.Infrastructure.Persistence.Configurations;

/// <summary>
///     Configures the database mapping of the Application, including the relationship to its API Keys.
/// </summary>
public sealed class ApplicationConfiguration : IEntityTypeConfiguration<Application>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Application> builder)
    {
        builder.ToTable("applications", Schemas.Config);

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(Application.NameMaxLength);

        builder.HasIndex(x => x.Name)
            .IsUnique();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasMany(x => x.ApiKeys)
            .WithOne()
            .HasForeignKey(x => x.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.ApiKeys)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
