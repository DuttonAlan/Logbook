// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Sdk;

/// <summary>
///     One request ready to send.
/// </summary>
internal sealed class LogEventBatch
{
    /// <summary>
    ///     The JSON of the events, compressed with gzip. Empty if there are no events.
    /// </summary>
    public byte[] Body { get; set; } = [];

    /// <summary>
    ///     The number of events in the body.
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    ///     The sequence number of the last event this batch covers, including those it left out.
    /// </summary>
    public long LastSequence { get; set; }

    /// <summary>
    ///     The number of events left out because their timestamp is outside of what the server accepts.
    /// </summary>
    public int ExpiredCount { get; set; }

    /// <summary>
    ///     The number of events left out because they are too large, even after cutting their text.
    /// </summary>
    public int TooLargeCount { get; set; }

    /// <summary>
    ///     The number of events in the body whose text was cut.
    /// </summary>
    public int TruncatedCount { get; set; }
}
