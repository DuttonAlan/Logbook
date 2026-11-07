// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Logbook.Core.Domain.ValueObjects;

/// <summary>
///     Computes what makes exceptions the same failure: their type and the code they came through.
/// </summary>
/// <remarks>
///     <para>
///         The message is not part of it, as it often contains values such as an Id, which would make every occurrence its
///         own group. From the stack trace, only the methods count: file names and line numbers change with a new build or
///         another build machine, and so do the numbers the compiler adds to the names of lambdas and async methods. The
///         words of a stack trace depend on the language of the sending machine, so frames are recognized by their structure.
///     </para>
///     <para>
///         Of the inner exceptions, only the types count: the same failure can be reached through different paths of a
///         library, and which of several tasks failed first is a matter of chance.
///     </para>
/// </remarks>
public static partial class ExceptionFingerprint
{
    /// <summary>
    ///     The length of a fingerprint in characters.
    /// </summary>
    public const int Length = 32;

    // Recursion would otherwise give each depth its own fingerprint. The top of the stack tells failures apart.
    private const int MaxFrames = 64;

    private const int MaxInnerExceptions = 64;

    /// <summary>
    ///     Computes the fingerprint of an exception as the SDK sends it, with <c>type</c>, <c>stackTrace</c>, and <c>inner</c>
    ///     or <c>innerExceptions</c>.
    /// </summary>
    /// <param name="exception">The exception, a JSON object or null.</param>
    /// <returns>The fingerprint in lowercase hexadecimal, or null if the exception has neither a type nor a stack trace.</returns>
    public static string? Compute(JsonElement? exception)
    {
        if (exception is not { ValueKind: JsonValueKind.Object } root)
        {
            return null;
        }

        var type = ReadText(root, "type");
        var stackTrace = ReadText(root, "stackTrace");

        if (type is null && stackTrace is null)
        {
            return null;
        }

        var text = new StringBuilder();

        text.Append(type).Append('\n');

        if (stackTrace is not null)
        {
            AppendFrames(text, stackTrace);
        }

        var innerTypes = new List<string>();
        CollectInnerTypes(root, innerTypes);
        innerTypes.Sort(StringComparer.Ordinal);

        foreach (var innerType in innerTypes.Distinct(StringComparer.Ordinal))
        {
            text.Append("inner ").Append(innerType).Append('\n');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));

#pragma warning disable CA1308 // Only hexadecimal digits, and lowercase is how trace identifiers are written as well.
        return Convert.ToHexString(hash, 0, Length / 2).ToLowerInvariant();
#pragma warning restore CA1308
    }

    /// <summary>
    ///     Takes the methods from a stack trace, one per line, e.g. <c>   at Shop.Orders.Place(Int32 id) in /src/Orders.cs:line 42</c>.
    /// </summary>
    private static void AppendFrames(StringBuilder text, string stackTrace)
    {
        var frames = 0;

        foreach (var line in stackTrace.AsSpan().EnumerateLines())
        {
            var trimmed = line.Trim();
            var open = trimmed.IndexOf('(');
            var space = trimmed.IndexOf(' ');

            // Without a word in front and parentheses, this is not a frame, e.g. the line where an async method continues.
            if (open < 0 || space < 0 || space > open)
            {
                continue;
            }

            var close = trimmed[open..].IndexOf(')');

            if (close < 0)
            {
                continue;
            }

            var frame = trimmed[(space + 1)..(open + close + 1)].ToString();

            text.Append(CompilerNumbers().Replace(frame, "$1")).Append('\n');

            if (++frames == MaxFrames)
            {
                return;
            }
        }
    }

    private static void CollectInnerTypes(JsonElement exception, List<string> types)
    {
        if (exception.TryGetProperty("inner", out var inner))
        {
            CollectInnerType(inner, types);
        }

        if (exception.TryGetProperty("innerExceptions", out var innerExceptions) && innerExceptions.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in innerExceptions.EnumerateArray())
            {
                CollectInnerType(item, types);
            }
        }
    }

    private static void CollectInnerType(JsonElement inner, List<string> types)
    {
        if (inner.ValueKind != JsonValueKind.Object || types.Count >= MaxInnerExceptions)
        {
            return;
        }

        if (ReadText(inner, "type") is { } type)
        {
            types.Add(type);
        }

        CollectInnerTypes(inner, types);
    }

    private static string? ReadText(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
    }

    // E.g. <>c__DisplayClass5_0, <Place>d__3 and <Place>g__Local|3_0. Linear, as it has no nested repetition, and the input
    // comes from senders.
    [GeneratedRegex(@"(__|DisplayClass|\|)\d+(?:_\d+)*", RegexOptions.CultureInvariant)]
    private static partial Regex CompilerNumbers();
}
