// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Sdk;

/// <summary>
///     One request ready to send.
/// </summary>
/// <param name="body">The JSON of the events, compressed with gzip.</param>
/// <param name="count">The number of events in the body. Zero if the first event was too large for a request.</param>
/// <param name="lastSequence">The sequence number of the last event this batch covers.</param>
internal readonly struct LogEventBatch(byte[] body, int count, long lastSequence)
{
    /// <summary>
    ///     The JSON of the events, compressed with gzip.
    /// </summary>
    public byte[] Body { get; } = body;

    /// <summary>
    ///     The number of events in the body. Zero if the first event was too large for a request.
    /// </summary>
    public int Count { get; } = count;

    /// <summary>
    ///     The sequence number of the last event this batch covers.
    /// </summary>
    public long LastSequence { get; } = lastSequence;
}
