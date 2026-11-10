// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Logbook.Core.Application.SavedSearches;

/// <summary>
///     Log messages for changes to saved searches. Their query is never logged, as it can contain what was searched for.
/// </summary>
internal static partial class SavedSearchLog
{
    [LoggerMessage(EventId = 1101, Level = LogLevel.Information, Message = "Saved search {SavedSearchId} was created.")]
    public static partial void SavedSearchCreated(this ILogger logger, Guid savedSearchId);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Information, Message = "Saved search {SavedSearchId} was changed.")]
    public static partial void SavedSearchChanged(this ILogger logger, Guid savedSearchId);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Information, Message = "Saved search {SavedSearchId} was deleted.")]
    public static partial void SavedSearchDeleted(this ILogger logger, Guid savedSearchId);
}
