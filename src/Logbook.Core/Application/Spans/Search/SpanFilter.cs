// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.Entities;

namespace Logbook.Core.Application.Spans.Search;

/// <summary>
///     Represents the filters to search spans. A filter that is null is not applied, and all filters must match.
/// </summary>
/// <param name="From">Only spans that started at or after this point in time.</param>
/// <param name="To">Only spans that started before this point in time.</param>
/// <param name="Kind">Only spans of this kind.</param>
/// <param name="ApplicationId">Only spans of this Application.</param>
/// <param name="Environment">Only spans of this Environment, compared exactly.</param>
/// <param name="Name">Only spans whose name contains this text, not case-sensitive.</param>
/// <param name="TraceId">Only spans of this trace, compared exactly.</param>
/// <param name="Success">Only spans that succeeded, or only those that failed.</param>
/// <param name="MinDurationMs">Only spans that took at least this many milliseconds.</param>
public sealed record SpanFilter(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    SpanKind? Kind = null,
    Guid? ApplicationId = null,
    string? Environment = null,
    string? Name = null,
    string? TraceId = null,
    bool? Success = null,
    double? MinDurationMs = null)
{
    /// <summary>
    ///     Treats the text filters like the stored values, which are trimmed and have no U+0000. PostgreSQL rejects U+0000
    ///     in a parameter.
    /// </summary>
    /// <returns>The filter to pass to the repository.</returns>
    public SpanFilter Normalize()
    {
        return this with
        {
            Environment = NormalizeText(Environment),
            Name = NormalizeText(Name),
            TraceId = NormalizeText(TraceId)
        };
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : LogEventJson.ReplaceNullCharacters(value.Trim());
    }
}
