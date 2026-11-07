// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.Entities;
using Logbook.Core.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logbook.Infrastructure.Persistence.Configurations;

/// <summary>
///     Configures the database mapping of the Log Event.
/// </summary>
/// <remarks>
///     EF Core cannot express parts of this table, so the migration <c>AddLogEvents</c> creates them by hand: the
///     partitioning by day, the function that creates partitions, and the full-text index on the Message.
///     There is deliberately no foreign key to the Application: a cascade would delete all its Log Events in one long
///     transaction. <c>DeleteApplicationService</c> deletes them in steps instead, and <c>LogEventRepository</c> checks
///     that the Application exists once per batch rather than for every row.
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

        builder.Property(x => x.ExceptionFingerprint)
            .HasMaxLength(ExceptionFingerprint.Length);

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

        // Filtered, as a trace is only ever looked up by its Id, and many Log Events have none.
        builder.HasIndex(x => new { x.TraceId, x.Timestamp, x.Id })
            .HasFilter("\"TraceId\" IS NOT NULL");

        // Filtered, as only few Log Events have an exception. This one lists the groups of a time range.
        builder.HasIndex(x => new { x.Timestamp, x.ApplicationId, x.ExceptionFingerprint })
            .HasDatabaseName("IX_log_events_Timestamp_ExceptionGroup")
            .HasFilter("\"ExceptionFingerprint\" IS NOT NULL");

        // This one finds the occurrences of a group.
        builder.HasIndex(x => new { x.ExceptionFingerprint, x.Timestamp, x.Id })
            .HasFilter("\"ExceptionFingerprint\" IS NOT NULL");

        // 3 is LogEventLevel.Warning. These levels are rare but what people look for, and the index stays very small.
        builder.HasIndex(x => new { x.Timestamp, x.Id })
            .HasDatabaseName("IX_log_events_Timestamp_Id_WarningOrAbove")
            .HasFilter("\"Level\" >= 3");
    }
}
