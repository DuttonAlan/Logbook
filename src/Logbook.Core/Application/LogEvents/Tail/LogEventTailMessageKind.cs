// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.LogEvents.Tail;

/// <summary>
///     The kinds of messages of a live tail.
/// </summary>
public enum LogEventTailMessageKind
{
    /// <summary>
    ///     Following has started: every Log Event stored from now on that matches is sent.
    /// </summary>
    Connected,

    /// <summary>
    ///     A Log Event that was just stored.
    /// </summary>
    LogEvent,

    /// <summary>
    ///     Log Events were skipped, because they arrive faster than they can be sent. They can still be searched.
    /// </summary>
    Gap,

    /// <summary>
    ///     Nothing has happened for a while, but the connection is alive.
    /// </summary>
    Heartbeat
}
