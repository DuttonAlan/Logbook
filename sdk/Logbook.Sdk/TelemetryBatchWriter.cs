// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.IO.Compression;
using System.Text.Json;

namespace Logbook.Sdk;

/// <summary>
///     Writes events as the body of one request, a JSON array compressed with gzip.
/// </summary>
internal static class TelemetryBatchWriter
{
    /// <summary>
    ///     Decides whether an event can be sent, and in what form.
    /// </summary>
    /// <typeparam name="T">The type of the event.</typeparam>
    /// <param name="item">The event.</param>
    /// <param name="guarded">The event to send, or null if it cannot be sent.</param>
    /// <returns>The decision.</returns>
    public delegate LogEventGuardResult CheckItem<T>(T item, out T? guarded);

    /// <summary>
    ///     Writes as many of the events as fit into one request, in order, and compresses them. Events the server would
    ///     reject are left out, because one of them would cost the whole batch.
    /// </summary>
    /// <typeparam name="T">The type of the events.</typeparam>
    /// <param name="items">The events, oldest first. Must not be empty.</param>
    /// <param name="maxRequestBytes">Maximum size of the JSON of one request.</param>
    /// <param name="check">Decides whether an event can be sent.</param>
    /// <param name="writeItem">Writes one event.</param>
    /// <returns>The batch. It has no events if none of them can be sent.</returns>
    public static LogEventBatch Write<T>(
        IReadOnlyList<BufferedItem<T>> items,
        int maxRequestBytes,
        CheckItem<T> check,
        Action<Utf8JsonWriter, T> writeItem)
    {
        using var json = new MemoryStream();
        using var writer = new Utf8JsonWriter(json, LogEventGuard.WriterOptions);

        json.WriteByte((byte)'[');

        var count = 0;
        var expired = 0;
        var tooLarge = 0;
        var truncated = 0;
        var lastSequence = 0L;

        foreach (var bufferedItem in items)
        {
            var result = check(bufferedItem.Item, out var guarded);

            if (result is LogEventGuardResult.Expired or LogEventGuardResult.TooLarge)
            {
                expired += result == LogEventGuardResult.Expired ? 1 : 0;
                tooLarge += result == LogEventGuardResult.TooLarge ? 1 : 0;
                lastSequence = bufferedItem.Sequence;
                continue;
            }

            var start = json.Length;

            if (count > 0)
            {
                json.WriteByte((byte)',');
            }

            writeItem(writer, guarded!);
            writer.Flush();
            writer.Reset();

            // One byte is still needed for the closing bracket.
            if (json.Length + 1 > maxRequestBytes)
            {
                json.SetLength(start);
                json.Position = start;

                if (count > 0)
                {
                    break;
                }

                // Too large for any request, so waiting would only hold up the others.
                tooLarge++;
                lastSequence = bufferedItem.Sequence;
                continue;
            }

            count++;
            truncated += result == LogEventGuardResult.Truncated ? 1 : 0;
            lastSequence = bufferedItem.Sequence;
        }

        json.WriteByte((byte)']');

        return new LogEventBatch
        {
            Body = count > 0 ? Compress(json) : [],
            Count = count,
            LastSequence = lastSequence,
            ExpiredCount = expired,
            TooLargeCount = tooLarge,
            TruncatedCount = truncated
        };
    }

    /// <summary>
    ///     Writes a string, if there is one.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="name">The name of the member.</param>
    /// <param name="value">The value.</param>
    public static void WriteOptionalString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    private static byte[] Compress(MemoryStream json)
    {
        using var compressed = new MemoryStream();

        using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(json.GetBuffer(), 0, (int)json.Length);
        }

        return compressed.ToArray();
    }
}
