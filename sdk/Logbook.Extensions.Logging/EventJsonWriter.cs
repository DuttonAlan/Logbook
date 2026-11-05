// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Logbook.Extensions.Logging;

/// <summary>
///     Writes the structured values and the exception of one event as JSON.
/// </summary>
/// <remarks>
///     Other objects are written with their <c>ToString</c> and never through reflection: that would run any getter of the
///     application, could follow cycles, and could send secrets nobody meant to log. All limits keep one call from costing
///     the application much, and the event from becoming too large. If one is reached, the event is marked as truncated.
/// </remarks>
/// <param name="maxTextLength">Maximum length of text in a value.</param>
internal sealed class EventJsonWriter(int maxTextLength)
{
    // The mark the SDK sets on an event whose text was cut. It is read by nobody but people.
    private const string TruncatedProperty = "logbook.truncated";

    private const int MaxValueDepth = 5;
    private const int MaxCollectionItems = 100;
    private const int MaxExceptionDepth = 8;
    private const int MaxExceptions = 32;

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        // Text is sent as UTF-8 rather than as \uXXXX, as the limits of the server count bytes. It is never embedded in HTML,
        // which is what the stricter default encoder protects against.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private bool _isTruncated;

    /// <summary>
    ///     Writes an exception with its inner exceptions.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns>The JSON object, with <c>type</c>, <c>message</c>, <c>stackTrace</c>, and <c>inner</c> or, for an <see cref="AggregateException"/>, <c>innerExceptions</c>.</returns>
    public string? WriteException(Exception exception)
    {
        var remaining = MaxExceptions;

        return WriteJson(writer => WriteException(writer, exception, 1, ref remaining));
    }

    /// <summary>
    ///     Writes the structured values. Call it last, so that it contains the mark for everything cut so far.
    /// </summary>
    /// <param name="values">The values.</param>
    /// <returns>The JSON object, or null if there is nothing to write.</returns>
    public string? WriteProperties(EventValues values)
    {
        return values.Values.Count == 0 && !_isTruncated
            ? null
            : WriteJson(writer =>
            {
                writer.WriteStartObject();

                foreach (var pair in values.Values)
                {
                    writer.WritePropertyName(pair.Key);
                    WriteValue(writer, pair.Value, 1);
                }

                if (_isTruncated)
                {
                    writer.WriteBoolean(TruncatedProperty, true);
                }

                writer.WriteEndObject();
            });
    }

    private static string? WriteJson(WriteDelegate write)
    {
        using var output = new MemoryStream();

        try
        {
            using (var writer = new Utf8JsonWriter(output, WriterOptions))
            {
                write(writer);
            }
        }
#pragma warning disable CA1031 // The values come from the application, and the SDK must never throw into it.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Each value handles its own failures, so this should not happen. The event is still useful without this part.
            return null;
        }

        return Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
    }

    private static string? Read(Func<string?> read)
    {
        try
        {
            return read();
        }
#pragma warning disable CA1031 // Any member of an exception can be overridden, and can throw.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    private static List<Exception> ReadInnerExceptions(AggregateException aggregate)
    {
        try
        {
            return [.. aggregate.InnerExceptions];
        }
#pragma warning disable CA1031 // Any member of an exception can be overridden, and can throw.
        catch (Exception)
#pragma warning restore CA1031
        {
            return [];
        }
    }

    private static Exception? ReadInnerException(Exception exception)
    {
        try
        {
            return exception.InnerException;
        }
#pragma warning disable CA1031 // Any member of an exception can be overridden, and can throw.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    private static string? ToText(object value)
    {
        return Read(() => value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString());
    }

    private void WriteException(Utf8JsonWriter writer, Exception exception, int depth, ref int remaining)
    {
        remaining--;

        writer.WriteStartObject();
        writer.WriteString("type", exception.GetType().FullName ?? exception.GetType().Name);
        WriteOptionalText(writer, "message", Read(() => exception.Message));
        WriteOptionalText(writer, "stackTrace", Read(() => exception.StackTrace));

        if (exception is AggregateException aggregate)
        {
            writer.WriteStartArray("innerExceptions");

            foreach (var inner in ReadInnerExceptions(aggregate))
            {
                if (depth >= MaxExceptionDepth || remaining <= 0)
                {
                    _isTruncated = true;
                    break;
                }

                WriteException(writer, inner, depth + 1, ref remaining);
            }

            writer.WriteEndArray();
        }
        else
        {
            writer.WritePropertyName("inner");

            var inner = ReadInnerException(exception);

            if (inner is null)
            {
                writer.WriteNullValue();
            }
            else if (depth >= MaxExceptionDepth || remaining <= 0)
            {
                _isTruncated = true;
                writer.WriteNullValue();
            }
            else
            {
                WriteException(writer, inner, depth + 1, ref remaining);
            }
        }

        writer.WriteEndObject();
    }

    private void WriteOptionalText(Utf8JsonWriter writer, string name, string? text)
    {
        writer.WritePropertyName(name);
        WriteText(writer, text);
    }

    private void WriteText(Utf8JsonWriter writer, string? text)
    {
        if (text is null)
        {
            writer.WriteNullValue();
        }
        else if (text.Length <= maxTextLength)
        {
            writer.WriteStringValue(text);
        }
        else
        {
            _isTruncated = true;

            // Half of a surrogate pair is not valid text.
            var length = char.IsHighSurrogate(text[maxTextLength - 1]) ? maxTextLength - 1 : maxTextLength;
            writer.WriteStringValue(text.Substring(0, length));
        }
    }

    private void WriteValue(Utf8JsonWriter writer, object? value, int depth)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                WriteText(writer, text);
                break;
            case bool boolean:
                writer.WriteBooleanValue(boolean);
                break;
            case byte or sbyte or short or ushort or int:
                writer.WriteNumberValue(Convert.ToInt32(value, CultureInfo.InvariantCulture));
                break;
            case uint number:
                writer.WriteNumberValue(number);
                break;
            case long number:
                writer.WriteNumberValue(number);
                break;
            case ulong number:
                writer.WriteNumberValue(number);
                break;
            case decimal number:
                writer.WriteNumberValue(number);
                break;
            case float number when !float.IsNaN(number) && !float.IsInfinity(number):
                writer.WriteNumberValue(number);
                break;
            case double number when !double.IsNaN(number) && !double.IsInfinity(number):
                writer.WriteNumberValue(number);
                break;
            case DateTime dateTime:
                writer.WriteStringValue(dateTime);
                break;
            case DateTimeOffset dateTimeOffset:
                writer.WriteStringValue(dateTimeOffset);
                break;
            case Guid guid:
                writer.WriteStringValue(guid);
                break;
            case Enum or char or Uri or TimeSpan or float or double:
                WriteText(writer, ToText(value));
                break;
            case IDictionary dictionary when depth < MaxValueDepth:
                WriteDictionary(writer, dictionary, depth);
                break;
            case IEnumerable<KeyValuePair<string, object?>> pairs when depth < MaxValueDepth:
                WritePairs(writer, pairs, depth);
                break;
            case IEnumerable items when depth < MaxValueDepth:
                WriteItems(writer, items, depth);
                break;
            default:
                WriteText(writer, ToText(value));
                break;
        }
    }

    // Reads everything first: an enumerator of the application can throw, which must not leave the JSON half written.
    private void WriteDictionary(Utf8JsonWriter writer, IDictionary dictionary, int depth)
    {
        var pairs = new List<KeyValuePair<string, object?>>();

        try
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (pairs.Count == MaxCollectionItems)
                {
                    _isTruncated = true;
                    break;
                }

                pairs.Add(new KeyValuePair<string, object?>(ToText(entry.Key) ?? string.Empty, entry.Value));
            }
        }
#pragma warning disable CA1031 // The values come from the application, and the SDK must never throw into it.
        catch (Exception)
#pragma warning restore CA1031
        {
            _isTruncated = true;
        }

        WritePairs(writer, pairs, depth);
    }

    private void WritePairs(Utf8JsonWriter writer, IEnumerable<KeyValuePair<string, object?>> pairs, int depth)
    {
        var read = new Dictionary<string, object?>(StringComparer.Ordinal);

        try
        {
            foreach (var pair in pairs)
            {
                if (read.Count == MaxCollectionItems)
                {
                    _isTruncated = true;
                    break;
                }

                read[pair.Key] = pair.Value;
            }
        }
#pragma warning disable CA1031 // The values come from the application, and the SDK must never throw into it.
        catch (Exception)
#pragma warning restore CA1031
        {
            _isTruncated = true;
        }

        writer.WriteStartObject();

        foreach (var pair in read)
        {
            writer.WritePropertyName(pair.Key);
            WriteValue(writer, pair.Value, depth + 1);
        }

        writer.WriteEndObject();
    }

    private void WriteItems(Utf8JsonWriter writer, IEnumerable items, int depth)
    {
        var read = new List<object?>();

        try
        {
            foreach (var item in items)
            {
                if (read.Count == MaxCollectionItems)
                {
                    _isTruncated = true;
                    break;
                }

                read.Add(item);
            }
        }
#pragma warning disable CA1031 // The values come from the application, and the SDK must never throw into it.
        catch (Exception)
#pragma warning restore CA1031
        {
            _isTruncated = true;
        }

        writer.WriteStartArray();

        foreach (var item in read)
        {
            WriteValue(writer, item, depth + 1);
        }

        writer.WriteEndArray();
    }

    private delegate void WriteDelegate(Utf8JsonWriter writer);
}
