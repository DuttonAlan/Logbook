// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logbook.Infrastructure.Persistence.Configurations;

/// <summary>
///     Configures the database mapping of the span.
/// </summary>
/// <remarks>
///     EF Core cannot express the partitioning by day, so the migration <c>AddSpansAndExceptionFingerprint</c> creates it by
///     hand, with the functions that create and drop partitions. Like the Log Events, spans deliberately have no foreign key
///     to the Application, see <see cref="LogEventConfiguration"/>.
/// </remarks>
public sealed class TraceSpanConfiguration : IEntityTypeConfiguration<TraceSpan>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<TraceSpan> builder)
    {
        builder.ToTable("spans", Schemas.Telemetry);

        // The primary key of a partitioned table must contain the partition key.
        builder.HasKey(x => new { x.Timestamp, x.Id });

        builder.Property(x => x.Kind)
            .HasConversion<short>();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(TraceSpan.NameMaxLength);

        builder.Property(x => x.Environment)
            .IsRequired()
            .HasMaxLength(LogEvent.EnvironmentMaxLength);

        builder.Property(x => x.TraceId)
            .IsRequired()
            .HasMaxLength(LogEvent.TraceFieldMaxLength);

        builder.Property(x => x.SpanId)
            .IsRequired()
            .HasMaxLength(LogEvent.TraceFieldMaxLength);

        builder.Property(x => x.ParentSpanId)
            .HasMaxLength(LogEvent.TraceFieldMaxLength);

        builder.Property(x => x.DependencyType)
            .HasMaxLength(TraceSpan.DependencyTypeMaxLength);

        builder.Property(x => x.Target)
            .HasMaxLength(TraceSpan.TargetMaxLength);

        builder.Property(x => x.Data)
            .HasMaxLength(TraceSpan.DataMaxLength);

        // A trace is what a transaction is looked up by.
        builder.HasIndex(x => new { x.TraceId, x.Timestamp, x.Id });

        // Searching reads the primary key backwards until a page is full, so the fewer rows match a filter, the longer it
        // takes. These indexes serve the filters that can match few rows.
        builder.HasIndex(x => new { x.ApplicationId, x.Timestamp, x.Id });

        builder.HasIndex(x => new { x.Kind, x.Timestamp, x.Id });

        builder.HasIndex(x => new { x.Timestamp, x.Id })
            .HasDatabaseName("IX_spans_Timestamp_Id_Failed")
            .HasFilter("NOT \"Success\"");
    }
}
