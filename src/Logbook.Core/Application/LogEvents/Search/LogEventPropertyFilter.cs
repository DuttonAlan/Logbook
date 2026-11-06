// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Globalization;
using System.Text.Json;

using Logbook.Core.Domain.Entities;
using Logbook.Core.Domain.Exceptions;

namespace Logbook.Core.Application.LogEvents.Search;

/// <summary>
///     A filter on one top-level property of a Log Event.
/// </summary>
/// <remarks>
///     A URL carries the value as text, but the stored property can be a string or a number or boolean, so
///     <c>OrderId=42</c> has to find both <c>"OrderId": 42</c> and <c>"OrderId": "42"</c>. Each form is a JSON object for
///     the containment operator of PostgreSQL, which the GIN index on the properties serves.
/// </remarks>
/// <param name="TextJson">The JSON object with the value as a string.</param>
/// <param name="LiteralJson">The JSON object with the value as a number or boolean, or null if the value is neither.</param>
public sealed record LogEventPropertyFilter(string TextJson, string? LiteralJson)
{
    private const char Separator = '=';

    /// <summary>
    ///     Reads a filter in the form <c>Name=Value</c>. The name ends at the first equals sign.
    /// </summary>
    /// <param name="value">The filter, which can be null when it comes from a URL.</param>
    /// <returns>The parsed filter.</returns>
    /// <exception cref="DomainValidationException">Thrown when the filter or its name is missing.</exception>
    public static LogEventPropertyFilter Parse(string? value)
    {
        // Null is what the model binding makes of an empty parameter.
        var separator = value?.IndexOf(Separator, StringComparison.Ordinal) ?? -1;

        if (value is null || separator <= 0)
        {
            throw new DomainValidationException("A property filter must have the form 'Name=Value'.", "property");
        }

        var name = LogEventJson.ReplaceNullCharacters(value[..separator]);
        var text = LogEventJson.ReplaceNullCharacters(value[(separator + 1)..]);

        return new LogEventPropertyFilter(
            ToJson(name, text),
            ReadLiteral(text) is { } literal ? ToJson(name, literal) : null);
    }

    private static string ToJson(string name, object value) => JsonSerializer.Serialize(new Dictionary<string, object> { [name] = value });

    private static object? ReadLiteral(string text)
    {
        if (text is "true" or "false")
        {
            return text == "true";
        }

        // As a decimal, which keeps all digits of a 64-bit Id and cannot exceed the range of "numeric" like an exponent can.
        const NumberStyles Styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

        return decimal.TryParse(text, Styles, CultureInfo.InvariantCulture, out var number) ? number : null;
    }
}
