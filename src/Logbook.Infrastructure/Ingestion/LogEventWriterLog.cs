// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Logbook.Infrastructure.Ingestion;

/// <summary>
///     Log messages for writing received Log Events. Their content is never logged, only Ids and counts.
/// </summary>
internal static partial class LogEventWriterLog
{
    [LoggerMessage(EventId = 2101, Level = LogLevel.Warning, Message = "The queue of received Log Events is full ({MaxQueuedBytes} bytes). Requests are rejected until Log Events are written.")]
    public static partial void QueueFull(this ILogger logger, long maxQueuedBytes);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Error, Message = "Writing {Count} Log Events failed with {ExceptionType} (SQL state {SqlState}). Trying again in {Delay}.")]
    public static partial void WriteFailed(this ILogger logger, int count, string exceptionType, string? sqlState, TimeSpan delay);

    [LoggerMessage(EventId = 2103, Level = LogLevel.Error, Message = "Log Event {LogEventId} of Application {ApplicationId} was dropped, because PostgreSQL cannot store it (SQL state {SqlState}).")]
    public static partial void LogEventDropped(this ILogger logger, Guid logEventId, Guid applicationId, string sqlState);

    [LoggerMessage(EventId = 2104, Level = LogLevel.Error, Message = "{Count} Log Events could not be written before the shutdown and are lost.")]
    public static partial void LogEventsLostOnShutdown(this ILogger logger, int count);
}
