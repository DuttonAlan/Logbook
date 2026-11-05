// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.LogEvents.Search;

/// <summary>
///     Represents one page of search results.
/// </summary>
/// <param name="Items">The Log Events on this page, newest first.</param>
/// <param name="NextCursor" example="AN4cD2u1QAABmdGifD57Sp8QfJY_Zq-m">The value for the parameter <c>cursor</c> to get the next page, or null if this is the last page.</param>
public sealed record SearchLogEventsResponse(IReadOnlyList<LogEventResponse> Items, string? NextCursor);
