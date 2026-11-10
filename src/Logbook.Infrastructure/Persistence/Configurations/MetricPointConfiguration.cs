// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logbook.Infrastructure.Persistence.Configurations;

/// <summary>
///     Configures the database mapping of the metric point.
/// </summary>
/// <remarks>
///     EF Core cannot express the partitioning by day, so the migration <c>AddMetricsAndSavedSearches</c> creates it by hand,
///     with the functions that create and drop partitions. Like the Log Events, metric points deliberately have no foreign
///     key to the Application, see <see cref="LogEventConfiguration"/>.
/// </remarks>
public sealed class MetricPointConfiguration : IEntityTypeConfiguration<MetricPoint>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<MetricPoint> builder)
    {
        builder.ToTable("metrics", Schemas.Telemetry);

        // The primary key of a partitioned table must contain the partition key.
        builder.HasKey(x => new { x.Timestamp, x.Id });

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(MetricPoint.NameMaxLength);

        builder.Property(x => x.Environment)
            .IsRequired()
            .HasMaxLength(LogEvent.EnvironmentMaxLength);

        builder.Property(x => x.Dimensions)
            .HasColumnType("jsonb");

        // A chart reads one metric over a time range.
        builder.HasIndex(x => new { x.Name, x.Timestamp });

        // Deleting the metric points of an Application, and its retention.
        builder.HasIndex(x => new { x.ApplicationId, x.Timestamp });
    }
}
