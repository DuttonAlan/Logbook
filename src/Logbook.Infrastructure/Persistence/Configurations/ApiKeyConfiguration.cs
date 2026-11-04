// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.Entities;
using Logbook.Core.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logbook.Infrastructure.Persistence.Configurations;

/// <summary>
///     Configures the database mapping of the API Key.
///     The relationship to the Application is configured in <see cref="ApplicationConfiguration"/>.
/// </summary>
public sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys", Schemas.Config);

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.KeyHash)
            .HasConversion(hash => hash.Value, value => ApiKeyHash.FromHashedValue(value))
            .IsRequired()
            .HasMaxLength(256);

        // An incoming API Key is looked up by its hash.
        builder.HasIndex(x => x.KeyHash)
            .IsUnique();

        builder.Property(x => x.CreatedAt)
            .IsRequired();
    }
}
