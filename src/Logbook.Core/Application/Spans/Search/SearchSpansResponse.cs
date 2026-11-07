// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.Spans.Search;

/// <summary>
///     Represents one page of spans, newest first.
/// </summary>
/// <param name="Items">The spans on the page.</param>
/// <param name="NextCursor">The cursor to the next page, or null if this is the last one.</param>
public sealed record SearchSpansResponse(IReadOnlyList<SpanResponse> Items, string? NextCursor);
