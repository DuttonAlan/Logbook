// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Extensions.Logging;

/// <summary>
///     Collects the structured values of one event from its scopes and its state. Later values replace earlier ones of the
///     same name, so the order of precedence is: the state, the innermost scope, the outermost scope.
/// </summary>
internal sealed class EventValues
{
    /// <summary>
    ///     The property for the name of the <c>EventId</c>.
    /// </summary>
    public const string EventNameProperty = "EventName";

    /// <summary>
    ///     The property for scopes that are text instead of named values.
    /// </summary>
    public const string ScopesProperty = "Scopes";

    // The name Microsoft.Extensions.Logging gives to the message template among the values of a call.
    private const string OriginalFormat = "{OriginalFormat}";

    private List<string>? _scopeTexts;

    /// <summary>
    ///     The message before rendering, if the state has one.
    /// </summary>
    public string? MessageTemplate { get; private set; }

    /// <summary>
    ///     The values by name, in the order they were first added.
    /// </summary>
    public Dictionary<string, object?> Values { get; } = new(StringComparer.Ordinal);

    /// <summary>
    ///     Adds a scope, with its named values, or as text if it has none.
    /// </summary>
    /// <param name="scope">The object passed to <c>BeginScope</c>.</param>
    public void AddScope(object? scope)
    {
        try
        {
            if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var pair in pairs)
                {
                    if (pair.Key == OriginalFormat)
                    {
                        AddScopeText(scope.ToString());
                    }
                    else
                    {
                        Values[pair.Key] = pair.Value;
                    }
                }
            }
            else if (scope is not null)
            {
                AddScopeText(scope.ToString());
            }
        }
#pragma warning disable CA1031 // The values come from the application, and the SDK must never throw into it.
        catch (Exception)
#pragma warning restore CA1031
        {
            // The scope is simply missing from this event.
        }
    }

    /// <summary>
    ///     Adds the state of a call, with the message template and its named values.
    /// </summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="state">The state.</param>
    public void AddState<TState>(TState state)
    {
        try
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var pair in pairs)
                {
                    if (pair.Key == OriginalFormat)
                    {
                        MessageTemplate = pair.Value as string;
                    }
                    else
                    {
                        Values[pair.Key] = pair.Value;
                    }
                }
            }
        }
#pragma warning disable CA1031 // The values come from the application, and the SDK must never throw into it.
        catch (Exception)
#pragma warning restore CA1031
        {
            // What was read so far is still useful.
        }
        finally
        {
            if (_scopeTexts is not null)
            {
                AddIfMissing(ScopesProperty, _scopeTexts);
            }
        }
    }

    /// <summary>
    ///     Adds a value the SDK itself contributes, which never replaces one of the application.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="value">The value.</param>
    public void AddIfMissing(string name, object? value)
    {
        if (!Values.ContainsKey(name))
        {
            Values[name] = value;
        }
    }

    private void AddScopeText(string? text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            (_scopeTexts ??= []).Add(text!);
        }
    }
}
