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
///     Writes a batch of Log Events to the database, and decides what happens when that fails.
/// </summary>
/// <remarks>
///     A failure that says nothing about the Log Events, e.g. the database being unavailable, is retried until it
///     succeeds or is cancelled: no Log Event is lost, and the queue filling up tells senders to slow down. A failure
///     caused by the data is not retried, as it would block all writing forever. The batch is split to find the Log
///     Event that causes it, and only that one is dropped.
///     Exceptions are deliberately not logged, only their type and SQL state: the context of a PostgreSQL error can
///     contain the row, and received Log Events must never end up in Logbook's own logs.
/// </remarks>
/// <param name="scopeFactory">Creates a scope with its own DbContext per attempt.</param>
/// <param name="timeProvider">The clock for the delay between attempts.</param>
/// <param name="logger">The Logger to record failures.</param>
public sealed class LogEventBatchWriter(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<LogEventBatchWriter> logger)
{
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Writes Log Events, retrying as long as necessary. Writing the same Log Event twice stores it once.
    /// </summary>
    /// <param name="logEvents">The Log Events to write.</param>
    /// <param name="cancellationToken">CancellationToken to stop retrying.</param>
    /// <returns>A Task that completes once every Log Event is stored or dropped.</returns>
    /// <exception cref="OperationCanceledException">Thrown when cancelled before all Log Events are finished.</exception>
    public async Task WriteAsync(IReadOnlyList<LogEvent> logEvents, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(logEvents);

        var delay = InitialRetryDelay;

        while (true)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<ILogEventRepository>();

                await repository.AddRange(logEvents, cancellationToken);

                return;
            }
            catch (PostgresException exception) when (IsCausedByTheData(exception.SqlState))
            {
                await HandleDataFailureAsync(logEvents, exception.SqlState, cancellationToken);

                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.WriteFailed(logEvents.Count, exception.GetType().Name, (exception as PostgresException)?.SqlState, delay);
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

    private async Task HandleDataFailureAsync(IReadOnlyList<LogEvent> logEvents, string sqlState, CancellationToken cancellationToken)
    {
        if (logEvents.Count > 1)
        {
            var middle = logEvents.Count / 2;

            await WriteAsync([.. logEvents.Take(middle)], cancellationToken);
            await WriteAsync([.. logEvents.Skip(middle)], cancellationToken);

            return;
        }

        // The primary key contains the Id, so this Log Event is already stored: an earlier attempt succeeded, but its
        // confirmation did not arrive. Nothing is lost.
        if (sqlState == PostgresErrorCodes.UniqueViolation)
        {
            return;
        }

        logger.LogEventDropped(logEvents[0].Id, logEvents[0].ApplicationId, sqlState);
    }
}
