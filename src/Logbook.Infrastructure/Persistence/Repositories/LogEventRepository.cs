// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Domain.Entities;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using NpgsqlTypes;

namespace Logbook.Infrastructure.Persistence.Repositories;

/// <inheritdoc/>
/// <param name="context">The DbContext to access the Log Events.</param>
public sealed class LogEventRepository(LogbookContext context) : ILogEventRepository
{
    // A new property of LogEvent must be added here and in AddRange: COPY does not use the EF Core model.
    private const string CopyCommand = """
        COPY telemetry.log_events ("Id", "Timestamp", "ApplicationId", "Level", "Message", "MessageTemplate", "Category", "Environment", "EventId", "Exception", "TraceId", "SpanId", "Properties")
        FROM STDIN (FORMAT BINARY)
        """;

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

            // All rows are stored by CompleteAsync, or none if it is not reached.
            await using var importer = await connection.BeginBinaryImportAsync(CopyCommand, cancellationToken);

            foreach (var logEvent in logEvents)
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
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}
