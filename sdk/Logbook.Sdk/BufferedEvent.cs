// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Sdk;

/// <summary>
///     An event in the <see cref="LogEventBuffer"/>.
/// </summary>
/// <param name="logEvent">The event.</param>
/// <param name="sequence">Its position among all events ever added, which only grows.</param>
/// <param name="bytes">The memory it was counted with.</param>
/// <param name="enqueuedAt">The timestamp of the clock when it was added.</param>
internal readonly struct BufferedEvent(LogbookEvent logEvent, long sequence, long bytes, long enqueuedAt)
{
    /// <summary>
    ///     The event.
    /// </summary>
    public LogbookEvent Event { get; } = logEvent;

    /// <summary>
    ///     Its position among all events ever added, which only grows.
    /// </summary>
    public long Sequence { get; } = sequence;

    /// <summary>
    ///     The memory it was counted with.
    /// </summary>
    public long Bytes { get; } = bytes;

    /// <summary>
    ///     The timestamp of the clock when it was added.
    /// </summary>
    public long EnqueuedAt { get; } = enqueuedAt;
}
