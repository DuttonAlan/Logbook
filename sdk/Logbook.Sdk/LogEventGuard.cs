// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Logbook.Sdk;

/// <summary>
///     Makes sure that an event is one the server accepts, because it rejects a whole batch for a single event it does not.
/// </summary>
/// <remarks>
///     Nearly all events pass <see cref="Check"/> after a few comparisons. Only an event that is really too large costs
///     more, which is why this runs on the sender, except for <see cref="CutLongMessages"/>. The result is not kept: an
///     event that is sent again is checked again, as its timestamp may have expired in the meantime.
/// </remarks>
internal static class LogEventGuard
{
    /// <summary>
    ///     The property that marks an event whose text was cut.
    /// </summary>
    public const string TruncatedProperty = "logbook.truncated";

    // The limits of the server for these fields are fixed, unlike those that can be set in the options.
    private const int CategoryMaxLength = 500;
    private const int EnvironmentMaxLength = 100;
    private const int TraceFieldMaxLength = 64;

    // The most bytes one UTF-16 code unit can take in UTF-8. This includes U+0000, which the server turns into U+FFFD.
    private const int MaxBytesPerCharacter = 3;

    // Below this, the text of an event is no longer of any use, so it is better to drop it and report that.
    private const int SmallestTextLimit = 64;

    // The server reads and writes at most 32 levels of JSON. Its search response puts an event three levels deep, so an
    // object with more levels could be sent, but then no page containing it could be read.
    private const int MaxObjectDepth = 29;

    /// <summary>
    ///     The options to write the JSON of events with.
    /// </summary>
    public static readonly JsonWriterOptions WriterOptions = new()
    {
        // Text is sent as UTF-8 rather than as \uXXXX, which would take up to 6 bytes for each character of the request
        // limit. The body is never embedded in HTML, which is what the stricter default encoder protects against.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // The clocks of the application and the server may differ, and the request takes time. An event close to a limit is
    // not worth risking its whole batch.
    private static readonly TimeSpan ClockMargin = TimeSpan.FromMinutes(5);

    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = MaxObjectDepth };

    /// <summary>
    ///     Cuts a message or message template over the limit. This is cheap enough for the thread of the host application.
    /// </summary>
    /// <param name="logEvent">The event.</param>
    /// <param name="limits">The limits of the server.</param>
    /// <returns>The event, or a new one if text was cut.</returns>
    public static LogbookEvent CutLongMessages(LogbookEvent logEvent, LogEventLimits limits)
    {
        if (logEvent.Message.Length <= limits.MaxMessageLength && (logEvent.MessageTemplate?.Length ?? 0) <= limits.MaxMessageLength)
        {
            return logEvent;
        }

        return WithContent(
            logEvent,
            Cut(logEvent.Message, limits.MaxMessageLength),
            CutOptional(logEvent.MessageTemplate, limits.MaxMessageLength),
            logEvent.ExceptionJson,
            logEvent.PropertiesJson,
            isTruncated: true);
    }

    /// <summary>
    ///     Checks an event against the limits of the server, and cuts its text if necessary.
    /// </summary>
    /// <param name="logEvent">The event.</param>
    /// <param name="limits">The limits of the server.</param>
    /// <param name="now">The current time.</param>
    /// <param name="guarded">The event to send, or null if it cannot be sent.</param>
    /// <returns>The decision.</returns>
    public static LogEventGuardResult Check(LogbookEvent logEvent, LogEventLimits limits, DateTimeOffset now, out LogbookEvent? guarded)
    {
        guarded = null;

        if (logEvent.Timestamp < now - limits.MaxEventAge + ClockMargin || logEvent.Timestamp > now + limits.MaxFutureOffset - ClockMargin)
        {
            return LogEventGuardResult.Expired;
        }

        // A value the server would not read is left out, as the event is still worth having without it.
        var exception = logEvent.ExceptionJson is { } exceptionJson && IsJsonObject(exceptionJson) ? exceptionJson : null;
        var properties = logEvent.PropertiesJson is { } propertiesJson && IsJsonObject(propertiesJson) ? propertiesJson : null;

        var isTruncated = logEvent.IsTruncated
            || (logEvent.Category?.Length ?? 0) > CategoryMaxLength
            || (logEvent.Environment?.Length ?? 0) > EnvironmentMaxLength
            || (logEvent.TraceId?.Length ?? 0) > TraceFieldMaxLength
            || (logEvent.SpanId?.Length ?? 0) > TraceFieldMaxLength;

        var message = logEvent.Message;
        var template = logEvent.MessageTemplate;

        if (!isTruncated && IsSurelyWithinLimits(message, template, exception, properties, limits))
        {
            guarded = ReferenceEquals(exception, logEvent.ExceptionJson) && ReferenceEquals(properties, logEvent.PropertiesJson)
                ? logEvent
                : WithContent(logEvent, message, template, exception, properties, isTruncated: false);

            return LogEventGuardResult.Accepted;
        }

        // The server counts these after replacing U+0000, which takes more bytes.
        message = message.Replace('\0', '�');
        template = template?.Replace('\0', '�');

        if (!isTruncated && !IsTooLarge(message, template, exception, properties, limits))
        {
            guarded = WithContent(logEvent, message, template, exception, properties, isTruncated: false);

            return LogEventGuardResult.Accepted;
        }

        if (!TryCut(ref message, ref template, ref exception, ref properties, limits))
        {
            return LogEventGuardResult.TooLarge;
        }

        guarded = WithContent(logEvent, message, template, exception, properties, isTruncated: true);

        return LogEventGuardResult.Truncated;
    }

    private static bool IsSurelyWithinLimits(string message, string? template, string? exception, string? properties, LogEventLimits limits)
    {
        var templateLength = template?.Length ?? 0;
        var characters = (long)message.Length + templateLength + (exception?.Length ?? 0) + (properties?.Length ?? 0);

        return message.Length <= limits.MaxMessageLength
            && templateLength <= limits.MaxMessageLength
            && characters * MaxBytesPerCharacter <= limits.MaxEventBytes;
    }

    private static bool IsTooLarge(string message, string? template, string? exception, string? properties, LogEventLimits limits)
    {
        var bytes = (long)Encoding.UTF8.GetByteCount(message)
            + Encoding.UTF8.GetByteCount(template ?? string.Empty)
            + Encoding.UTF8.GetByteCount(exception ?? string.Empty)
            + Encoding.UTF8.GetByteCount(properties ?? string.Empty);

        return bytes > limits.MaxEventBytes
            || message.Length > limits.MaxMessageLength
            || (template?.Length ?? 0) > limits.MaxMessageLength;
    }

    // Applies one limit to all long text of the event, lowered until the event fits. So a single long text is cut only as
    // far as needed, while the short texts next to it stay whole.
    private static bool TryCut(ref string message, ref string? template, ref string? exception, ref string? properties, LogEventLimits limits)
    {
        for (var textLimit = limits.MaxMessageLength; ; textLimit /= 2)
        {
            message = Cut(message, textLimit);
            template = CutOptional(template, textLimit);
            exception = exception is null ? null : RewriteObject(exception, textLimit, addMark: false);
            var marked = properties is null ? MarkOnlyObject() : RewriteObject(properties, textLimit, addMark: true);

            if (!IsTooLarge(message, template, exception, marked, limits))
            {
                properties = marked;
                return true;
            }

            if (textLimit / 2 < SmallestTextLimit)
            {
                return false;
            }
        }
    }

    private static string? CutOptional(string? text, int maxLength) => text is null ? null : Cut(text, maxLength);

    private static string Cut(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        // Half of a surrogate pair is not valid text, and the server would reject it.
        var length = char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength;

        return text.Substring(0, length);
    }

    private static string MarkOnlyObject() => $"{{\"{TruncatedProperty}\":true}}";

    private static string RewriteObject(string json, int textLimit, bool addMark)
    {
        using var document = JsonDocument.Parse(json, DocumentOptions);
        using var output = new MemoryStream();

        using (var writer = new Utf8JsonWriter(output, WriterOptions))
        {
            writer.WriteStartObject();

            if (addMark)
            {
                writer.WriteBoolean(TruncatedProperty, true);
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                // Only present if it was marked before, as this object is rewritten as often as the limit is lowered.
                if (property.Name != TruncatedProperty)
                {
                    writer.WritePropertyName(property.Name);
                    WriteValue(writer, property.Value, textLimit);
                }
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
    }

    private static void WriteValue(Utf8JsonWriter writer, JsonElement element, int textLimit)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();

                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteValue(writer, property.Value, textLimit);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();

                foreach (var item in element.EnumerateArray())
                {
                    WriteValue(writer, item, textLimit);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(Cut(element.GetString()!, textLimit));
                break;

            default:
                element.WriteTo(writer);
                break;
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

    private static LogbookEvent WithContent(
        LogbookEvent logEvent,
        string message,
        string? template,
        string? exception,
        string? properties,
        bool isTruncated)
    {
        return new LogbookEvent(
            logEvent.Timestamp,
            logEvent.Level,
            message,
            template,
            CutOptional(logEvent.Category, CategoryMaxLength),
            CutOptional(logEvent.Environment, EnvironmentMaxLength),
            logEvent.EventId,
            exception,
            CutOptional(logEvent.TraceId, TraceFieldMaxLength),
            CutOptional(logEvent.SpanId, TraceFieldMaxLength),
            properties)
        {
            IsTruncated = isTruncated
        };
    }
}
