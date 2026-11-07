// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Domain.Entities;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Npgsql;

namespace Logbook.Infrastructure.Ingestion;

/// <summary>
///     Writes a batch of records to the database, and decides what happens when that fails.
/// </summary>
/// <remarks>
///     A failure that says nothing about the records, e.g. the database being unavailable, is retried until it succeeds or
///     is cancelled: no record is lost, and the queue filling up tells senders to slow down. A failure caused by the data is
///     not retried, as it would block all writing forever. The batch is split to find the record that causes it, and only
///     that one is dropped.
///     Exceptions are deliberately not logged, only their type and SQL state: the context of a PostgreSQL error can contain
///     the row, and received data must never end up in Logbook's own logs.
/// </remarks>
/// <typeparam name="T">The type of the records.</typeparam>
/// <typeparam name="TStore">The repository to write with, resolved per attempt.</typeparam>
/// <param name="kind">What the records are called in the logs.</param>
/// <param name="scopeFactory">Creates a scope with its own DbContext per attempt.</param>
/// <param name="timeProvider">The clock for the delay between attempts.</param>
/// <param name="logger">The Logger to record failures.</param>
public abstract class TelemetryBatchWriter<T, TStore>(
    string kind,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger logger)
    where T : ITelemetryRecord
    where TStore : ITelemetryStore<T>
{
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Writes records, retrying as long as necessary. Writing the same record twice stores it once.
    /// </summary>
    /// <param name="records">The records to write.</param>
    /// <param name="cancellationToken">CancellationToken to stop retrying.</param>
    /// <returns>A Task that completes once every record is stored or dropped.</returns>
    /// <exception cref="OperationCanceledException">Thrown when cancelled before all records are finished.</exception>
    public async Task WriteAsync(IReadOnlyList<T> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);

        var delay = InitialRetryDelay;

        while (true)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<TStore>();

                await store.AddRange(records, cancellationToken);

                return;
            }
            catch (PostgresException exception) when (IsCausedByTheData(exception.SqlState))
            {
                await HandleDataFailureAsync(records, exception.SqlState, cancellationToken);

                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.WriteFailed(kind, records.Count, exception.GetType().Name, (exception as PostgresException)?.SqlState, delay);
            }

            await Task.Delay(delay, timeProvider, cancellationToken);

            delay = delay * 2 < MaxRetryDelay ? delay * 2 : MaxRetryDelay;
        }
    }

    private static bool IsCausedByTheData(string sqlState)
    {
        // Class 22 is a data exception, class 54 a program limit, e.g. a text too long for the full-text index.
        // Of the integrity errors only this one: "no partition for the row" (23514) goes away by trying again.
        return sqlState.StartsWith("22", StringComparison.Ordinal)
            || sqlState.StartsWith("54", StringComparison.Ordinal)
            || sqlState == PostgresErrorCodes.UniqueViolation;
    }

    private async Task HandleDataFailureAsync(IReadOnlyList<T> records, string sqlState, CancellationToken cancellationToken)
    {
        if (records.Count > 1)
        {
            var middle = records.Count / 2;

            await WriteAsync([.. records.Take(middle)], cancellationToken);
            await WriteAsync([.. records.Skip(middle)], cancellationToken);

            return;
        }

        // The primary key contains the Id, so this record is already stored: an earlier attempt succeeded, but its
        // confirmation did not arrive. Nothing is lost.
        if (sqlState == PostgresErrorCodes.UniqueViolation)
        {
            return;
        }

        logger.RecordDropped(kind, records[0].Id, records[0].ApplicationId, sqlState);
    }
}
