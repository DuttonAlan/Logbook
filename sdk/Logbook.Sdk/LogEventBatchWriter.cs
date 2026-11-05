// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.IO.Compression;
using System.Text.Encodings.Web;
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
    // The server reads and writes at most 32 levels of JSON. Its search response puts an event three levels deep, so an
    // object with more levels could be sent, but then no page containing it could be read.
    private const int MaxObjectDepth = 29;

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        // Text is sent as UTF-8 rather than as \uXXXX, which would take up to 6 bytes for each character of the request
        // limit. The body is never embedded in HTML, which is what the stricter default encoder protects against.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = MaxObjectDepth };

    /// <summary>
    ///     Writes as many of the events as fit into one request, in order, and compresses them.
    /// </summary>
    /// <param name="events">The events, oldest first. Must not be empty.</param>
    /// <param name="maxBytes">Maximum size of the JSON before compression.</param>
    /// <returns>The batch. It has no events if the first event alone is too large for a request.</returns>
    public static LogEventBatch Write(IReadOnlyList<BufferedEvent> events, int maxBytes)
    {
        using var json = new MemoryStream();
        using var writer = new Utf8JsonWriter(json, WriterOptions);

        json.WriteByte((byte)'[');

        var count = 0;

        foreach (var bufferedEvent in events)
        {
            var start = json.Length;

            if (count > 0)
            {
                json.WriteByte((byte)',');
            }

            WriteEvent(writer, bufferedEvent.Event);
            writer.Flush();
            writer.Reset();

            // One byte is still needed for the closing bracket.
            if (json.Length + 1 > maxBytes)
            {
                if (count == 0)
                {
                    return new LogEventBatch([], 0, bufferedEvent.Sequence);
                }

                json.SetLength(start);
                json.Position = start;
                break;
            }

            count++;
        }

        json.WriteByte((byte)']');

        return new LogEventBatch(Compress(json), count, events[count - 1].Sequence);
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
        if (json is not null && IsJsonObject(json))
        {
            writer.WritePropertyName(name);
            writer.WriteRawValue(json, skipInputValidation: true);
        }
    }

    // The server rejects a whole batch for one value it cannot read, so such a value must not leave the SDK.
    private static bool IsJsonObject(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, DocumentOptions);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            // An unpaired surrogate can only be in the text at this point as an escape sequence, which parsing accepts.
            if (json.IndexOf("\\u", StringComparison.Ordinal) >= 0)
            {
                ReadAllText(document.RootElement);
            }

            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    // Reading text with an unpaired surrogate throws an InvalidOperationException.
    private static void ReadAllText(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    _ = property.Name;
                    ReadAllText(property.Value);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ReadAllText(item);
                }

                break;

            case JsonValueKind.String:
                _ = element.GetString();
                break;

            default:
                break;
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
