// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Runtime.CompilerServices;

using Logbook.Core.Abstractions;
using Logbook.Core.Application.LogEvents.Search;

namespace Logbook.Core.Application.LogEvents.Tail;

/// <summary>
///     The Service to follow new Log Events as they are stored.
/// </summary>
/// <remarks>
///     Each written batch is looked up in the database rather than matched in memory, so that the filters mean exactly
///     the same as in the search, including the full-text search of PostgreSQL. It also finds a Log Event by when it was
///     stored, whereas following by timestamp would miss every Log Event that arrives late.
/// </remarks>
/// <param name="feed">The feed of written batches.</param>
/// <param name="logEventRepository">An Instance of a ILogEventRepository to read the Log Events.</param>
public sealed class TailLogEventsService(ILogEventFeed feed, ILogEventRepository logEventRepository)
{
    // Keeps a burst from loading too much into memory at once, as a Log Event can be hundreds of kilobytes.
    private const int MaxLogEventsPerBatch = 500;

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     Follows the Log Events matching the filter until cancelled.
    /// </summary>
    /// <param name="filter">The filters to apply. The time range is not used.</param>
    /// <param name="cancellationToken">CancellationToken to stop following.</param>
    /// <returns>The messages, starting with <see cref="LogEventTailMessageKind.Connected"/>.</returns>
    public async IAsyncEnumerable<LogEventTailMessage> ExecuteAsync(
        LogEventFilter filter,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var normalized = filter.Normalize();

        using var subscription = feed.Subscribe();

        yield return new LogEventTailMessage(LogEventTailMessageKind.Connected);

        while (true)
        {
            var written = await subscription.ReadAsync(HeartbeatInterval, cancellationToken);

            if (written is null)
            {
                yield return new LogEventTailMessage(LogEventTailMessageKind.Heartbeat);
                continue;
            }

            if (written.FollowsGap)
            {
                yield return new LogEventTailMessage(LogEventTailMessageKind.Gap);
            }

            // One more than the limit tells whether some were left out, without counting them.
            var logEvents = await logEventRepository.GetWritten(written, normalized, MaxLogEventsPerBatch + 1, cancellationToken);

            if (logEvents.Count > MaxLogEventsPerBatch)
            {
                yield return new LogEventTailMessage(LogEventTailMessageKind.Gap);
            }

            // Oldest first, like the lines of a log file.
            foreach (var logEvent in logEvents.Take(MaxLogEventsPerBatch).Reverse())
            {
                yield return new LogEventTailMessage(LogEventTailMessageKind.LogEvent, LogEventResponse.From(logEvent));
            }
        }
    }
}
