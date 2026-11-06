// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.LogEvents.Tail;

/// <summary>
///     Identifies a batch of Log Events that was just written. Only Ids, so that a follower that falls behind does not
///     keep the content of Log Events in memory.
/// </summary>
/// <param name="Ids">The Ids of the Log Events.</param>
/// <param name="Oldest">The earliest point in time (UTC) of the Log Events.</param>
/// <param name="Newest">The latest point in time (UTC) of the Log Events.</param>
/// <param name="FollowsGap">Whether earlier batches were skipped, because the follower was too slow to read them.</param>
public sealed record WrittenLogEvents(IReadOnlyList<Guid> Ids, DateTime Oldest, DateTime Newest, bool FollowsGap = false);
