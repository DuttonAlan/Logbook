// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Domain.Exceptions;

namespace Logbook.Core.Application.LogEvents.Search;

/// <summary>
///     The Service to search stored Log Events.
/// </summary>
/// <param name="logEventRepository">An Instance of a ILogEventRepository to read the Log Events.</param>
public sealed class SearchLogEventsService(ILogEventRepository logEventRepository)
{
    /// <summary>
    ///     Gets one page of the Log Events matching the filter, newest first.
    /// </summary>
    /// <param name="filter">The filters to apply.</param>
    /// <param name="cursor">The cursor from the previous page, or null for the first page.</param>
    /// <param name="limit">The maximum number of Log Events on the page.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The page, with the cursor to the next one.</returns>
    /// <exception cref="DomainValidationException">Thrown when the time range is empty or the cursor is not valid.</exception>
    public async Task<SearchLogEventsResponse> ExecuteAsync(
        LogEventFilter filter,
        string? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (filter.From >= filter.To)
        {
            throw new DomainValidationException("'from' must be earlier than 'to'.", nameof(filter));
        }

        LogEventCursor? after = null;

        if (cursor is not null && !LogEventCursor.TryDecode(cursor, out after))
        {
            throw new DomainValidationException("The cursor is not valid. Use the value of 'nextCursor' from the previous page.", nameof(cursor));
        }

        // One more than requested tells whether there is another page, without counting all matches.
        var logEvents = await logEventRepository.Search(filter.Normalize(), after, limit + 1, cancellationToken);

        var page = logEvents.Take(limit).ToList();
        var nextCursor = logEvents.Count > limit ? new LogEventCursor(page[^1].Timestamp, page[^1].Id).Encode() : null;

        return new SearchLogEventsResponse([.. page.Select(LogEventResponse.From)], nextCursor);
    }
}
