// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Logbook.Infrastructure.Ingestion;

/// <summary>
///     Log messages for writing what Applications send. Its content is never logged, only Ids and counts.
/// </summary>
internal static partial class TelemetryWriterLog
{
    [LoggerMessage(EventId = 2101, Level = LogLevel.Warning, Message = "The queue of received {Kind} is full ({MaxQueuedBytes} bytes). Requests are rejected until {Kind} are written.")]
    public static partial void QueueFull(this ILogger logger, string kind, long maxQueuedBytes);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Error, Message = "Writing {Count} {Kind} failed with {ExceptionType} (SQL state {SqlState}). Trying again in {Delay}.")]
    public static partial void WriteFailed(this ILogger logger, string kind, int count, string exceptionType, string? sqlState, TimeSpan delay);

    [LoggerMessage(EventId = 2103, Level = LogLevel.Error, Message = "Record {RecordId} of kind {Kind} of Application {ApplicationId} was dropped, because PostgreSQL cannot store it (SQL state {SqlState}).")]
    public static partial void RecordDropped(this ILogger logger, string kind, Guid recordId, Guid applicationId, string sqlState);

    [LoggerMessage(EventId = 2104, Level = LogLevel.Error, Message = "{Count} {Kind} could not be written before the shutdown and are lost.")]
    public static partial void LostOnShutdown(this ILogger logger, string kind, int count);
}
