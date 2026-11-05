// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Application.LogEvents.Search;
using Logbook.Core.Domain.Entities;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using NpgsqlTypes;

namespace Logbook.Infrastructure.Persistence.Repositories;

/// <inheritdoc/>
/// <param name="context">The DbContext to access the Log Events.</param>
public sealed class LogEventRepository(LogbookContext context) : ILogEventRepository
{
    private const int DeleteBatchSize = 10_000;

    // A new property of LogEvent must be added here and in AddRange: COPY does not use the EF Core model.
    private const string CopyCommand = """
        COPY telemetry.log_events ("Id", "Timestamp", "ApplicationId", "Level", "Message", "MessageTemplate", "Category", "Environment", "EventId", "Exception", "TraceId", "SpanId", "Properties")
        FROM STDIN (FORMAT BINARY)
        """;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LogEvent>> Search(
        LogEventFilter filter,
        LogEventCursor? after,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = context.LogEvents.AsNoTracking();

        if (filter.From is { } from)
        {
            var fromUtc = from.UtcDateTime;
            query = query.Where(logEvent => logEvent.Timestamp >= fromUtc);
        }

        if (filter.To is { } to)
        {
            var toUtc = to.UtcDateTime;
            query = query.Where(logEvent => logEvent.Timestamp < toUtc);
        }

        if (filter.Levels is { Count: > 0 } levels)
        {
            query = query.Where(logEvent => levels.Contains(logEvent.Level));
        }

        if (filter.ApplicationId is { } applicationId)
        {
            query = query.Where(logEvent => logEvent.ApplicationId == applicationId);
        }

        if (filter.Environment is { } environment)
        {
            query = query.Where(logEvent => logEvent.Environment == environment);
        }

        if (filter.Text is { } text)
        {
            // Must stay the same expression as the index "IX_log_events_Message_FullText", or the index is not used.
            // websearch_to_tsquery accepts any input, unlike to_tsquery, which fails on a syntax error.
            query = query.Where(logEvent => EF.Functions.ToTsVector("simple", logEvent.Message)
                .Matches(EF.Functions.WebSearchToTsQuery("simple", text)));
        }

        if (after is not null)
        {
            var (timestamp, id) = (after.Timestamp, after.Id);

            // A row comparison, because the order is by both columns. The first condition only lets PostgreSQL skip
            // the partitions of newer days, which it does not do for a row comparison.
            query = query
                .Where(logEvent => logEvent.Timestamp <= timestamp)
                .Where(logEvent => EF.Functions.LessThan(ValueTuple.Create(logEvent.Timestamp, logEvent.Id), ValueTuple.Create(timestamp, id)));
        }

        return await query
            .OrderByDescending(logEvent => logEvent.Timestamp)
            .ThenByDescending(logEvent => logEvent.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task AddRange(IReadOnlyCollection<LogEvent> logEvents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logEvents);

        // A row without a partition for its day cannot be inserted. The function only looks up partitions that exist.
        var days = logEvents.Select(logEvent => DateOnly.FromDateTime(logEvent.Timestamp)).Distinct().ToArray();

        await context.Database.ExecuteSqlAsync(
            $"SELECT telemetry.ensure_log_event_partitions({days})",
            cancellationToken);

        await context.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var existing = await LockExistingApplicationsAsync(logEvents, cancellationToken);

            await using var importer = await connection.BeginBinaryImportAsync(CopyCommand, cancellationToken);

            foreach (var logEvent in logEvents.Where(logEvent => existing.Contains(logEvent.ApplicationId)))
            {
                await importer.StartRowAsync(cancellationToken);
                await importer.WriteAsync(logEvent.Id, NpgsqlDbType.Uuid, cancellationToken);
                await importer.WriteAsync(logEvent.Timestamp, NpgsqlDbType.TimestampTz, cancellationToken);
                await importer.WriteAsync(logEvent.ApplicationId, NpgsqlDbType.Uuid, cancellationToken);
                await importer.WriteAsync((short)logEvent.Level, NpgsqlDbType.Smallint, cancellationToken);
                await importer.WriteAsync(logEvent.Message, NpgsqlDbType.Text, cancellationToken);
                await importer.WriteAsync(logEvent.MessageTemplate, NpgsqlDbType.Text, cancellationToken);
                await importer.WriteAsync(logEvent.Category, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(logEvent.Environment, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(logEvent.EventId, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(logEvent.Exception, NpgsqlDbType.Jsonb, cancellationToken);
                await importer.WriteAsync(logEvent.TraceId, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(logEvent.SpanId, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(logEvent.Properties, NpgsqlDbType.Jsonb, cancellationToken);
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
            deleted = await context.LogEvents
                .Where(logEvent => logEvent.ApplicationId == applicationId)
                .Take(DeleteBatchSize)
                .ExecuteDeleteAsync(cancellationToken);

            total += deleted;
        }
        while (deleted == DeleteBatchSize);

        return total;
    }

    /// <summary>
    ///     Finds which Applications of the Log Events still exist, and keeps them from being deleted until the transaction
    ///     ends. Log Events wait in the queue for a while, so their Application can be deleted in the meantime. The lock
    ///     makes the deletion wait for this write, so it also removes these Log Events, while a later write finds no
    ///     Application and discards them. Either way, none remain without an Application.
    /// </summary>
    private async Task<HashSet<Guid>> LockExistingApplicationsAsync(IReadOnlyCollection<LogEvent> logEvents, CancellationToken cancellationToken)
    {
        var ids = logEvents.Select(logEvent => logEvent.ApplicationId).Distinct().ToArray();

        var existing = await context.Database
            .SqlQuery<Guid>($"""SELECT "Id" AS "Value" FROM config.applications WHERE "Id" = ANY({ids}) FOR KEY SHARE""")
            .ToListAsync(cancellationToken);

        return [.. existing];
    }
}
