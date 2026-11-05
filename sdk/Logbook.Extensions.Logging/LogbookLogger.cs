// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Diagnostics;

using Logbook.Sdk;

using Microsoft.Extensions.Logging;

namespace Logbook.Extensions.Logging;

/// <summary>
///     Turns calls to <see cref="ILogger"/> into events. It never throws, whatever the values in a call do.
/// </summary>
/// <remarks>
///     This runs on the thread of the application. Structured values are written to JSON right here, because they could
///     change or be disposed while the event waits in the buffer.
/// </remarks>
/// <param name="provider">The provider that created the logger.</param>
/// <param name="category">The category of the logger.</param>
/// <param name="isEnabled">Whether this logger sends at all.</param>
internal sealed class LogbookLogger(LogbookLoggerProvider provider, string category, bool isEnabled) : ILogger
{
    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return isEnabled ? provider.ScopeProvider.Push(state) : null;
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => isEnabled && logLevel != LogLevel.None;

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        try
        {
            provider.Client.Enqueue(CreateEvent(logLevel, eventId, state, exception, formatter));
        }
#pragma warning disable CA1031 // The SDK must never throw into the host application.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Nothing above should get here, as each part handles its own failures. One event is not worth failing for.
        }
    }

    private static string FormatMessage<TState>(TState state, Exception? exception, Func<TState, Exception?, string>? formatter)
    {
        try
        {
            return formatter is null ? state?.ToString() ?? string.Empty : formatter(state, exception);
        }
#pragma warning disable CA1031 // A value in the message may throw in its ToString. The rest of the event is still useful.
        catch (Exception)
#pragma warning restore CA1031
        {
            return string.Empty;
        }
    }

    private static LogbookLevel ToLevel(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => LogbookLevel.Trace,
            LogLevel.Debug => LogbookLevel.Debug,
            LogLevel.Warning => LogbookLevel.Warning,
            LogLevel.Error => LogbookLevel.Error,
            LogLevel.Critical => LogbookLevel.Critical,
            _ => LogbookLevel.Information
        };
    }

    private LogbookEvent CreateEvent<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var values = new EventValues();
        provider.ScopeProvider.ForEachScope(static (scope, collected) => collected.AddScope(scope), values);
        values.AddState(state);

        if (eventId.Name is not null)
        {
            values.AddIfMissing(EventValues.EventNameProperty, eventId.Name);
        }

        var json = new EventJsonWriter(provider.MaxTextLength);
        var exceptionJson = exception is null ? null : json.WriteException(exception);
        var propertiesJson = json.WriteProperties(values);

        // A W3C identifier is what the server stores. An older hierarchical Id has no such parts.
        var activity = Activity.Current is { IdFormat: ActivityIdFormat.W3C } current ? current : null;

        return new LogbookEvent(
            provider.TimeProvider.GetUtcNow(),
            ToLevel(logLevel),
            FormatMessage(state, exception, formatter),
            values.MessageTemplate,
            category,
            provider.Environment,
            eventId.Id != 0 ? eventId.Id : null,
            exceptionJson,
            activity?.TraceId.ToHexString(),
            activity?.SpanId.ToHexString(),
            propertiesJson);
    }
}
