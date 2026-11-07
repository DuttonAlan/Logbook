// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Application.LogEvents.Search;
using Logbook.Core.Application.Spans.Search;
using Logbook.Core.Domain.Entities;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using NpgsqlTypes;

namespace Logbook.Infrastructure.Persistence.Repositories;

/// <inheritdoc/>
/// <remarks>
///     Spans expire and are deleted like Log Events, so the reasoning in <see cref="LogEventRepository"/> applies here too.
/// </remarks>
/// <param name="context">The DbContext to access the spans.</param>
public sealed class SpanRepository(LogbookContext context) : ISpanRepository
{
    private const int DeleteBatchSize = 10_000;

    // A new property of TraceSpan must be added here and in AddRange: COPY does not use the EF Core model.
    private const string CopyCommand = """
        COPY telemetry.spans ("Id", "Timestamp", "ApplicationId", "Kind", "Name", "DurationMs", "Success", "Environment", "TraceId", "SpanId", "ParentSpanId", "StatusCode", "DependencyType", "Target", "Data")
        FROM STDIN (FORMAT BINARY)
        """;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TraceSpan>> Search(
        SpanFilter filter,
        LogEventCursor? after,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = ApplyFilter(context.Spans.AsNoTracking(), filter);

        if (after is not null)
        {
            var (timestamp, id) = (after.Timestamp, after.Id);

            // A row comparison, because the order is by both columns. The first condition only lets PostgreSQL skip the
            // partitions of newer days, which it does not do for a row comparison.
            query = query
                .Where(span => span.Timestamp <= timestamp)
                .Where(span => EF.Functions.LessThan(ValueTuple.Create(span.Timestamp, span.Id), ValueTuple.Create(timestamp, id)));
        }

        return await query
            .OrderByDescending(span => span.Timestamp)
            .ThenByDescending(span => span.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TraceSpan>> GetByTrace(string traceId, int take, CancellationToken cancellationToken = default)
    {
        return await context.Spans.AsNoTracking()
            .Where(span => span.TraceId == traceId)
            .OrderBy(span => span.Timestamp)
            .ThenBy(span => span.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task AddRange(IReadOnlyCollection<TraceSpan> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        // A row without a partition for its day cannot be inserted. The function only looks up partitions that exist.
        var days = records.Select(span => DateOnly.FromDateTime(span.Timestamp)).Distinct().ToArray();

        await context.Database.ExecuteSqlAsync(
            $"SELECT telemetry.ensure_span_partitions({days})",
            cancellationToken);

        await context.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var existing = await LockExistingApplicationsAsync(records, cancellationToken);

            await using var importer = await connection.BeginBinaryImportAsync(CopyCommand, cancellationToken);

            foreach (var span in records.Where(span => existing.Contains(span.ApplicationId)))
            {
                await importer.StartRowAsync(cancellationToken);
                await importer.WriteAsync(span.Id, NpgsqlDbType.Uuid, cancellationToken);
                await importer.WriteAsync(span.Timestamp, NpgsqlDbType.TimestampTz, cancellationToken);
                await importer.WriteAsync(span.ApplicationId, NpgsqlDbType.Uuid, cancellationToken);
                await importer.WriteAsync((short)span.Kind, NpgsqlDbType.Smallint, cancellationToken);
                await importer.WriteAsync(span.Name, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(span.DurationMs, NpgsqlDbType.Double, cancellationToken);
                await importer.WriteAsync(span.Success, NpgsqlDbType.Boolean, cancellationToken);
                await importer.WriteAsync(span.Environment, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(span.TraceId, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(span.SpanId, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(span.ParentSpanId, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(span.StatusCode, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(span.DependencyType, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(span.Target, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(span.Data, NpgsqlDbType.Varchar, cancellationToken);
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
            deleted = await context.Spans
                .Where(span => span.ApplicationId == applicationId)
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

            deleted = await context.Spans
                .Where(span => span.ApplicationId == applicationId && span.Timestamp < before)
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
        // Application is not held up, and has no old spans to lose.
        var retentions = await context.Database
            .SqlQuery<int?>($"""SELECT "RetentionDays" AS "Value" FROM config.applications FOR SHARE""")
            .ToListAsync(cancellationToken);

        var longestDays = retentions.Count > 0
            ? retentions.Max(days => days ?? defaultRetentionDays)
            : defaultRetentionDays;

        var firstDayToKeep = DateOnly.FromDateTime(now.AddDays(-longestDays));

        var dropped = await context.Database
            .SqlQuery<DateOnly?>($"""SELECT telemetry.drop_oldest_span_partition_before({firstDayToKeep}) AS "Value" """)
            .ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return dropped[0];
    }

    private static IQueryable<TraceSpan> ApplyFilter(IQueryable<TraceSpan> query, SpanFilter filter)
    {
        if (filter.From is { } from)
        {
            var fromUtc = from.UtcDateTime;
            query = query.Where(span => span.Timestamp >= fromUtc);
        }

        if (filter.To is { } to)
        {
            var toUtc = to.UtcDateTime;
            query = query.Where(span => span.Timestamp < toUtc);
        }

        if (filter.Kind is { } kind)
        {
            query = query.Where(span => span.Kind == kind);
        }

        if (filter.ApplicationId is { } applicationId)
        {
            query = query.Where(span => span.ApplicationId == applicationId);
        }

        if (filter.Environment is { } environment)
        {
            query = query.Where(span => span.Environment == environment);
        }

        if (filter.Name is { } name)
        {
            var pattern = $"%{name.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal)}%";

            query = query.Where(span => EF.Functions.ILike(span.Name, pattern, @"\"));
        }

        if (filter.TraceId is { } traceId)
        {
            query = query.Where(span => span.TraceId == traceId);
        }

        if (filter.Success is { } success)
        {
            query = query.Where(span => span.Success == success);
        }

        if (filter.MinDurationMs is { } minDurationMs)
        {
            query = query.Where(span => span.DurationMs >= minDurationMs);
        }

        return query;
    }

    /// <summary>
    ///     Finds which Applications of the spans still exist, and keeps them from being deleted until the transaction ends.
    ///     Spans wait in the queue for a while, so their Application can be deleted in the meantime. The lock makes the
    ///     deletion wait for this write, so it also removes these spans, while a later write finds no Application and discards
    ///     them. Either way, none remain without an Application.
    /// </summary>
    private async Task<HashSet<Guid>> LockExistingApplicationsAsync(IReadOnlyCollection<TraceSpan> spans, CancellationToken cancellationToken)
    {
        var ids = spans.Select(span => span.ApplicationId).Distinct().ToArray();

        var existing = await context.Database
            .SqlQuery<Guid>($"""SELECT "Id" AS "Value" FROM config.applications WHERE "Id" = ANY({ids}) FOR KEY SHARE""")
            .ToListAsync(cancellationToken);

        return [.. existing];
    }
}
