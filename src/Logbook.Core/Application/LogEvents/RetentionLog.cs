// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Logbook.Core.Application.LogEvents;

/// <summary>
///     Log messages of the retention of Log Events. Their content is never logged, only Ids and counts.
/// </summary>
internal static partial class RetentionLog
{
    [LoggerMessage(EventId = 2301, Level = LogLevel.Information, Message = "The partition of the Log Events of {Day} has expired and was dropped.")]
    public static partial void PartitionDropped(this ILogger logger, DateOnly day);

    [LoggerMessage(EventId = 2302, Level = LogLevel.Information, Message = "{Count} expired Log Events of Application {ApplicationId} were deleted.")]
    public static partial void ExpiredLogEventsDeleted(this ILogger logger, Guid applicationId, long count);

    [LoggerMessage(EventId = 2303, Level = LogLevel.Information, Message = "The partition of the spans of {Day} has expired and was dropped.")]
    public static partial void SpanPartitionDropped(this ILogger logger, DateOnly day);

    [LoggerMessage(EventId = 2304, Level = LogLevel.Information, Message = "{Count} expired spans of Application {ApplicationId} were deleted.")]
    public static partial void ExpiredSpansDeleted(this ILogger logger, Guid applicationId, long count);
}
