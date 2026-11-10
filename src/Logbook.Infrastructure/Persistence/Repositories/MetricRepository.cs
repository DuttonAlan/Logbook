// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Application.Charts;
using Logbook.Core.Application.Metrics.GetNames;
using Logbook.Core.Application.Metrics.GetSeries;
using Logbook.Core.Domain.Entities;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using NpgsqlTypes;

namespace Logbook.Infrastructure.Persistence.Repositories;

/// <inheritdoc/>
/// <remarks>
///     Metric points expire and are deleted like Log Events, so the reasoning in <see cref="LogEventRepository"/> applies
///     here too.
/// </remarks>
/// <param name="context">The DbContext to access the metric points.</param>
public sealed class MetricRepository(LogbookContext context) : IMetricRepository
{
    private const int DeleteBatchSize = 10_000;

    // A new property of MetricPoint must be added here and in AddRange: COPY does not use the EF Core model.
    private const string CopyCommand = """
        COPY telemetry.metrics ("Id", "Timestamp", "ApplicationId", "Name", "Environment", "Count", "Sum", "Min", "Max", "Dimensions")
        FROM STDIN (FORMAT BINARY)
        """;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MetricNameResponse>> GetNames(
        DateTime from,
        DateTime to,
        Guid? applicationId,
        string? environment,
        int take,
        CancellationToken cancellationToken = default)
    {
        var query = context.Metrics.AsNoTracking().Where(metric => metric.Timestamp >= from && metric.Timestamp < to);

        if (applicationId is { } id)
        {
            query = query.Where(metric => metric.ApplicationId == id);
        }

        if (environment is not null)
        {
            query = query.Where(metric => metric.Environment == environment);
        }

        return await query
            .GroupBy(metric => metric.Name)
            .OrderBy(group => group.Key)
            .Take(take)
            .Select(group => new MetricNameResponse(group.Key, group.Max(metric => metric.Timestamp)))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MetricBucketResponse>> GetSeries(
        ChartRange range,
        string name,
        Guid? applicationId,
        string? environment,
        string? dimensionsJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(range);

        var (from, to, width, origin) = (range.From, range.To, range.BucketWidth, ChartRange.Origin);

        // date_bin must get the same width and origin as ChartRange, so that the buckets match the ones it lists.
        var rows = await context.Database
            .SqlQuery<MetricBucketRow>($"""
                SELECT date_bin({width}, m."Timestamp", {origin}) AS "Bucket",
                    sum(m."Count")::bigint AS "Count",
                    sum(m."Sum") AS "Sum",
                    min(m."Min") AS "Min",
                    max(m."Max") AS "Max"
                FROM telemetry.metrics m
                WHERE m."Name" = {name}
                    AND m."Timestamp" >= {from} AND m."Timestamp" < {to}
                    AND ({applicationId}::uuid IS NULL OR m."ApplicationId" = {applicationId})
                    AND ({environment}::text IS NULL OR m."Environment" = {environment})
                    AND ({dimensionsJson}::jsonb IS NULL OR m."Dimensions" @> {dimensionsJson}::jsonb)
                GROUP BY 1
                ORDER BY 1
                """)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new MetricBucketResponse(row.Bucket, row.Count, row.Sum, row.Min, row.Max))];
    }

    /// <inheritdoc/>
    public async Task AddRange(IReadOnlyCollection<MetricPoint> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        // A row without a partition for its day cannot be inserted. The function only looks up partitions that exist.
        var days = records.Select(metric => DateOnly.FromDateTime(metric.Timestamp)).Distinct().ToArray();

        await context.Database.ExecuteSqlAsync(
            $"SELECT telemetry.ensure_metric_partitions({days})",
            cancellationToken);

        await context.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var existing = await LockExistingApplicationsAsync(records, cancellationToken);

            await using var importer = await connection.BeginBinaryImportAsync(CopyCommand, cancellationToken);

            foreach (var metric in records.Where(metric => existing.Contains(metric.ApplicationId)))
            {
                await importer.StartRowAsync(cancellationToken);
                await importer.WriteAsync(metric.Id, NpgsqlDbType.Uuid, cancellationToken);
                await importer.WriteAsync(metric.Timestamp, NpgsqlDbType.TimestampTz, cancellationToken);
                await importer.WriteAsync(metric.ApplicationId, NpgsqlDbType.Uuid, cancellationToken);
                await importer.WriteAsync(metric.Name, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(metric.Environment, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(metric.Count, NpgsqlDbType.Bigint, cancellationToken);
                await importer.WriteAsync(metric.Sum, NpgsqlDbType.Double, cancellationToken);
                await importer.WriteAsync(metric.Min, NpgsqlDbType.Double, cancellationToken);
                await importer.WriteAsync(metric.Max, NpgsqlDbType.Double, cancellationToken);
                await importer.WriteAsync(metric.Dimensions, NpgsqlDbType.Jsonb, cancellationToken);
            }

            await importer.CompleteAsync(cancellationToken);
            await importer.CloseAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    /// <inheritdoc/>
    public async Task<long> DeleteByApplication(Guid applicationId, CancellationToken cancellationToken = default)
    {
        var total = 0L;
        int deleted;

        // In small transactions: progress is kept if the request ends early, and other work is not blocked for long.
        do
        {
            deleted = await context.Metrics
                .Where(metric => metric.ApplicationId == applicationId)
                .Take(DeleteBatchSize)
                .ExecuteDeleteAsync(cancellationToken);

            total += deleted;
        }
        while (deleted == DeleteBatchSize);

        return total;
    }

    /// <inheritdoc/>
    public async Task<long> DeleteExpired(Guid applicationId, int defaultRetentionDays, DateTime now, CancellationToken cancellationToken = default)
    {
        var total = 0L;
        int deleted;

        do
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            // FOR SHARE, as a change of the retention then waits for this step, and the next step reads the new value.
            var retention = await context.Database
                .SqlQuery<int?>($"""SELECT "RetentionDays" AS "Value" FROM config.applications WHERE "Id" = {applicationId} FOR SHARE""")
                .ToListAsync(cancellationToken);

            if (retention.Count == 0)
            {
                return total;
            }

            var before = now.AddDays(-(retention[0] ?? defaultRetentionDays));

            deleted = await context.Metrics
                .Where(metric => metric.ApplicationId == applicationId && metric.Timestamp < before)
                .Take(DeleteBatchSize)
                .ExecuteDeleteAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            total += deleted;
        }
        while (deleted == DeleteBatchSize);

        return total;
    }

    /// <inheritdoc/>
    public async Task<DateOnly?> DropOldestExpiredPartition(int defaultRetentionDays, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // FOR SHARE, as a change of a retention then waits for this drop, and the next one reads the new value. A new
        // Application is not held up, and has no old metric points to lose.
        var retentions = await context.Database
            .SqlQuery<int?>($"""SELECT "RetentionDays" AS "Value" FROM config.applications FOR SHARE""")
            .ToListAsync(cancellationToken);

        var longestDays = retentions.Count > 0
            ? retentions.Max(days => days ?? defaultRetentionDays)
            : defaultRetentionDays;

        var firstDayToKeep = DateOnly.FromDateTime(now.AddDays(-longestDays));

        var dropped = await context.Database
            .SqlQuery<DateOnly?>($"""SELECT telemetry.drop_oldest_metric_partition_before({firstDayToKeep}) AS "Value" """)
            .ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return dropped[0];
    }

    /// <summary>
    ///     Finds which Applications of the metric points still exist, and keeps them from being deleted until the
    ///     transaction ends, for the reasons given in <see cref="SpanRepository"/>.
    /// </summary>
    private async Task<HashSet<Guid>> LockExistingApplicationsAsync(IReadOnlyCollection<MetricPoint> metrics, CancellationToken cancellationToken)
    {
        var ids = metrics.Select(metric => metric.ApplicationId).Distinct().ToArray();

        var existing = await context.Database
            .SqlQuery<Guid>($"""SELECT "Id" AS "Value" FROM config.applications WHERE "Id" = ANY({ids}) FOR KEY SHARE""")
            .ToListAsync(cancellationToken);

        return [.. existing];
    }
}
