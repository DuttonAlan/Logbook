// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Logbook.Infrastructure.Retention;

/// <summary>
///     Log messages of the background service for the retention of Log Events.
/// </summary>
internal static partial class LogEventRetentionLog
{
    [LoggerMessage(EventId = 2311, Level = LogLevel.Error, Message = "A retention run failed with {ExceptionType} (SQL state {SqlState}). Expired Log Events stay until the next run in {Interval}.")]
    public static partial void RunFailed(this ILogger logger, string exceptionType, string? sqlState, TimeSpan interval);
}
