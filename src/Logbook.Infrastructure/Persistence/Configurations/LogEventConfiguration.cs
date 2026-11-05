// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logbook.Infrastructure.Persistence.Configurations;

/// <summary>
///     Configures the database mapping of the Log Event.
/// </summary>
/// <remarks>
///     EF Core cannot express parts of this table, so the migration <c>AddLogEvents</c> creates them by hand: the
///     partitioning by day, the function that creates partitions, and the full-text index on the Message.
///     There is deliberately no foreign key to the Application: it would be checked for every inserted row, and deleting
///     an Application would have to search all partitions. Log Events are removed by retention instead.
/// </remarks>
public sealed class LogEventConfiguration : IEntityTypeConfiguration<LogEvent>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<LogEvent> builder)
    {
        builder.ToTable("log_events", Schemas.Telemetry);

        // The primary key of a partitioned table must contain the partition key.
        builder.HasKey(x => new { x.Timestamp, x.Id });

        builder.Property(x => x.Level)
            .HasConversion<short>();

        builder.Property(x => x.Message)
            .IsRequired();

        builder.Property(x => x.Category)
            .HasMaxLength(LogEvent.CategoryMaxLength);

        builder.Property(x => x.Environment)
            .IsRequired()
            .HasMaxLength(LogEvent.EnvironmentMaxLength);

        builder.Property(x => x.TraceId)
            .HasMaxLength(LogEvent.TraceFieldMaxLength);

        builder.Property(x => x.SpanId)
            .HasMaxLength(LogEvent.TraceFieldMaxLength);

        builder.Property(x => x.Exception)
            .HasColumnType("jsonb");

        builder.Property(x => x.Properties)
            .HasColumnType("jsonb");

        // jsonb_path_ops supports searching by containment (@>) with a smaller and faster index than the default.
        builder.HasIndex(x => x.Properties)
            .HasMethod("gin")
            .HasOperators("jsonb_path_ops");

        builder.HasIndex(x => x.Exception)
            .HasMethod("gin")
            .HasOperators("jsonb_path_ops");

        // Searching reads the primary key backwards until a page is full, so the fewer rows match a filter, the longer
        // it takes, up to the whole table if nothing matches. These indexes serve the filters that can match few rows.
        builder.HasIndex(x => new { x.ApplicationId, x.Timestamp, x.Id });

        builder.HasIndex(x => new { x.Environment, x.Timestamp, x.Id });

        // 3 is LogEventLevel.Warning. These levels are rare but what people look for, and the index stays very small.
        builder.HasIndex(x => new { x.Timestamp, x.Id })
            .HasDatabaseName("IX_log_events_Timestamp_Id_WarningOrAbove")
            .HasFilter("\"Level\" >= 3");
    }
}
