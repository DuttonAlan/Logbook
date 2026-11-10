// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Logbook.Core.Application.LogEvents;

/// <summary>
///     Log messages for receiving Log Events. The content of received Log Events is never logged.
/// </summary>
internal static partial class LogIngestionLog
{
    [LoggerMessage(EventId = 2001, Level = LogLevel.Debug, Message = "Received {Count} Log Events from Application {ApplicationId}.")]
    public static partial void LogEventsReceived(this ILogger logger, Guid applicationId, int count);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Debug, Message = "Received {Count} spans from Application {ApplicationId}.")]
    public static partial void SpansReceived(this ILogger logger, Guid applicationId, int count);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Debug, Message = "Received {Count} metric points from Application {ApplicationId}.")]
    public static partial void MetricsReceived(this ILogger logger, Guid applicationId, int count);
}
