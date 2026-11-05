// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.Entities;

namespace Logbook.Core.Application.LogEvents.Search;

/// <summary>
///     Represents the filters to search Log Events. A filter that is null is not applied, and all filters must match.
/// </summary>
/// <param name="From">Only Log Events at or after this point in time.</param>
/// <param name="To">Only Log Events before this point in time.</param>
/// <param name="Levels">Only Log Events with one of these levels. Empty means all levels.</param>
/// <param name="ApplicationId">Only Log Events of this Application.</param>
/// <param name="Environment">Only Log Events of this Environment, compared exactly.</param>
/// <param name="Text">Only Log Events whose message matches this full-text search.</param>
public sealed record LogEventFilter(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    IReadOnlyList<LogEventLevel>? Levels = null,
    Guid? ApplicationId = null,
    string? Environment = null,
    string? Text = null);
