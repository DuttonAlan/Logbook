// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

using Logbook.Core.Domain.Exceptions;

namespace Logbook.Core.Domain.Entities;

/// <summary>
///     Rewrites the JSON of a <see cref="LogEvent"/> so PostgreSQL accepts it as <c>jsonb</c>.
/// </summary>
/// <remarks>
///     <c>jsonb</c> is stricter than JSON: it rejects the character U+0000, unpaired surrogates and numbers beyond the
///     range of <c>numeric</c>. A log line can contain any of them, e.g. binary data or a value from an external system.
/// </remarks>
internal static class LogEventJson
{
    private const char ReplacementCharacter = '\uFFFD';

    // Longer numbers are not meaningful for logs, and a long fraction can exceed the range of "numeric".
    private const int MaxNumberLength = 64;

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        // Escaping is not needed for jsonb and would only make the size limit of an event harder to predict.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    ///     Replaces the character U+0000, which PostgreSQL cannot store in text, with U+FFFD.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <returns>The text without U+0000.</returns>
    public static string ReplaceNullCharacters(string value) => value.Replace('\0', ReplacementCharacter);

    /// <summary>
    ///     Validates a JSON object and rewrites it into storable JSON.
    /// </summary>
    /// <param name="element">The JSON value, which must be an object or null.</param>
    /// <param name="paramName">The name of the parameter, for the error message.</param>
    /// <returns>The storable JSON, or null if there is no value.</returns>
    /// <exception cref="DomainValidationException">Thrown when the value is not an object, or contains unpaired surrogates.</exception>
    public static string? NormalizeObject(JsonElement? element, string paramName)
    {
        if (element is not { } value || value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new DomainValidationException("The value must be a JSON object.", paramName);
        }

        var buffer = new ArrayBufferWriter<byte>();

        try
        {
            using var writer = new Utf8JsonWriter(buffer, WriterOptions);
            WriteValue(writer, value);
        }
        catch (InvalidOperationException exception)
        {
            // Thrown when a string or property name with an unpaired surrogate is read.
            throw new DomainValidationException("The value contains text that is not valid Unicode.", exception);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteValue(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();

                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(ReplaceNullCharacters(property.Name));
                    WriteValue(writer, property.Value);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();

                foreach (var item in element.EnumerateArray())
                {
                    WriteValue(writer, item);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(ReplaceNullCharacters(element.GetString()!));
                break;

            case JsonValueKind.Number:
                WriteNumber(writer, element);
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static void WriteNumber(Utf8JsonWriter writer, JsonElement number)
    {
        var text = number.GetRawText();

        if (IsStorableNumber(text, number))
        {
            number.WriteTo(writer);
        }
        else
        {
            // No data is lost: the number stays readable as text.
            writer.WriteStringValue(text);
        }
    }

    private static bool IsStorableNumber(string text, JsonElement number)
    {
        if (text.Length > MaxNumberLength || !number.TryGetDouble(out var value) || !double.IsFinite(value))
        {
            return false;
        }

        // A value that rounds to zero but has non-zero digits, e.g. 1e-20000, is out of range as well.
        return value != 0 || !text.TakeWhile(character => character is not ('e' or 'E')).Any(character => character is >= '1' and <= '9');
    }
}
