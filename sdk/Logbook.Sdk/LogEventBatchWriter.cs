// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.IO.Compression;
using System.Text.Json;

namespace Logbook.Sdk;

/// <summary>
///     Writes events as the body of a request to <c>POST /api/v1/logs</c>.
/// </summary>
/// <remarks>
///     The format is written by hand, because the SDK cannot share types with the server. The end-to-end test in
///     <c>Logbook.Api.IntegrationTests</c> guards it against drifting apart.
/// </remarks>
internal static class LogEventBatchWriter
{
    /// <summary>
    ///     Writes as many of the events as fit into one request, in order, and compresses them. Events the server would
    ///     reject are left out, because one of them would cost the whole batch.
    /// </summary>
    /// <param name="events">The events, oldest first. Must not be empty.</param>
    /// <param name="limits">The limits of the server.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The batch. It has no events if none of them can be sent.</returns>
    public static LogEventBatch Write(IReadOnlyList<BufferedEvent> events, LogEventLimits limits, DateTimeOffset now)
    {
        using var json = new MemoryStream();
        using var writer = new Utf8JsonWriter(json, LogEventGuard.WriterOptions);

        json.WriteByte((byte)'[');

        var count = 0;
        var expired = 0;
        var tooLarge = 0;
        var truncated = 0;
        var lastSequence = 0L;

        foreach (var bufferedEvent in events)
        {
            var result = LogEventGuard.Check(bufferedEvent.Event, limits, now, out var guarded);

            if (result is LogEventGuardResult.Expired or LogEventGuardResult.TooLarge)
            {
                expired += result == LogEventGuardResult.Expired ? 1 : 0;
                tooLarge += result == LogEventGuardResult.TooLarge ? 1 : 0;
                lastSequence = bufferedEvent.Sequence;
                continue;
            }

            var start = json.Length;

            if (count > 0)
            {
                json.WriteByte((byte)',');
            }

            WriteEvent(writer, guarded!);
            writer.Flush();
            writer.Reset();

            // One byte is still needed for the closing bracket.
            if (json.Length + 1 > limits.MaxRequestBytes)
            {
                json.SetLength(start);
                json.Position = start;

                if (count > 0)
                {
                    break;
                }

                // Too large for any request, so waiting would only hold up the others.
                tooLarge++;
                lastSequence = bufferedEvent.Sequence;
                continue;
            }

            count++;
            truncated += result == LogEventGuardResult.Truncated ? 1 : 0;
            lastSequence = bufferedEvent.Sequence;
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

    private static void WriteEvent(Utf8JsonWriter writer, LogbookEvent logEvent)
    {
        writer.WriteStartObject();
        writer.WriteString("timestamp", logEvent.Timestamp);
        writer.WriteString("level", GetLevelName(logEvent.Level));
        writer.WriteString("message", logEvent.Message);
        WriteOptionalString(writer, "messageTemplate", logEvent.MessageTemplate);
        WriteOptionalString(writer, "category", logEvent.Category);
        WriteOptionalString(writer, "environment", logEvent.Environment);

        if (logEvent.EventId is { } eventId)
        {
            writer.WriteNumber("eventId", eventId);
        }

        WriteOptionalObject(writer, "exception", logEvent.ExceptionJson);
        WriteOptionalString(writer, "traceId", logEvent.TraceId);
        WriteOptionalString(writer, "spanId", logEvent.SpanId);
        WriteOptionalObject(writer, "properties", logEvent.PropertiesJson);
        writer.WriteEndObject();
    }

    private static void WriteOptionalString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteOptionalObject(Utf8JsonWriter writer, string name, string? json)
    {
        // The guard has checked it.
        if (json is not null)
        {
            writer.WritePropertyName(name);
            writer.WriteRawValue(json, skipInputValidation: true);
        }
    }

    private static string GetLevelName(LogbookLevel level)
    {
        return level switch
        {
            LogbookLevel.Trace => "Trace",
            LogbookLevel.Debug => "Debug",
            LogbookLevel.Warning => "Warning",
            LogbookLevel.Error => "Error",
            LogbookLevel.Critical => "Critical",

            // An undefined value would reject the whole batch.
            _ => "Information"
        };
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
