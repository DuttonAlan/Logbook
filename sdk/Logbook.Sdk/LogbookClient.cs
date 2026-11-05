// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Sdk;

/// <summary>
///     Sends events to a Logbook server: they wait in a bounded in-memory buffer and are sent in batches in the background.
/// </summary>
/// <remarks>
///     The client never throws into the host application, and only makes it wait when it is disposed, for at most
///     <see cref="LogbookOptions.ShutdownTimeout"/>. With options it cannot work with, e.g. without an
///     <see cref="LogbookOptions.Endpoint"/>, it stays disabled and ignores all events. Create one client and keep it for
///     the lifetime of the application. What it does can be followed through the <c>EventSource</c> named <c>Logbook-Sdk</c>.
/// </remarks>
public sealed class LogbookClient : IDisposable
{
    private readonly LogEventBuffer? _buffer;
    private readonly LogEventSender? _sender;
    private readonly LogEventLimits? _limits;

    private int _disposed;

    /// <summary>
    ///     Creates the client and starts sending in the background.
    /// </summary>
    /// <param name="options">The options. They are read once, so later changes have no effect.</param>
    public LogbookClient(LogbookOptions options)
        : this(options, handler: null, TimeProvider.System)
    {
    }

    /// <summary>
    ///     Creates the client with the parts tests replace.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="handler">The handler for the requests, or null for the default.</param>
    /// <param name="timeProvider">The clock for the waiting times.</param>
    internal LogbookClient(LogbookOptions options, HttpMessageHandler? handler, TimeProvider timeProvider)
    {
        var invalidSetting = options is null ? nameof(options) : options.FindInvalidSetting();

        if (invalidSetting is not null)
        {
            LogbookEventSource.Log.ClientDisabled(invalidSetting);
            return;
        }

        try
        {
            var limits = new LogEventLimits(options!);

            var buffer = new LogEventBuffer(
                options!.MaxQueueSize,
                options.MaxQueueBytes,
                options.BatchSize,
                options.MaxRequestBytes,
                timeProvider);

            var sender = new LogEventSender(options, limits, buffer, handler, timeProvider);
            sender.Start();

            _limits = limits;
            _buffer = buffer;
            _sender = sender;
        }
#pragma warning disable CA1031 // The client must never throw into the host application.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // E.g. a platform without support for the default handler. Logging is not worth failing the host for.
            LogbookEventSource.Log.ClientDisabled(exception.GetType().Name);
        }
    }

    /// <summary>
    ///     Whether the client has usable options and sends events. If not, the <c>EventSource</c> has reported why.
    /// </summary>
    public bool IsEnabled => _buffer is not null;

    /// <summary>
    ///     The buffer of the client, or null if it is disabled.
    /// </summary>
    internal LogEventBuffer? Buffer => _buffer;

    /// <summary>
    ///     Adds an event to the buffer and returns immediately, dropping the oldest events if the buffer is full.
    /// </summary>
    /// <param name="logEvent">The event. Null is ignored.</param>
    public void Enqueue(LogbookEvent logEvent)
    {
        if (logEvent is null || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            if (_buffer is not null)
            {
                // Only the cheap part of the guard runs here. It also keeps a huge message from pushing out other events.
                _buffer.Add(LogEventGuard.CutLongMessages(logEvent, _limits!));
            }
        }
#pragma warning disable CA1031 // The client must never throw into the host application.
        catch (Exception)
#pragma warning restore CA1031
        {
            // E.g. out of memory while the buffer grows. Losing one event is better than failing the caller.
        }
    }

    /// <summary>
    ///     Sends the remaining events and stops sending in the background. This waits for at most
    ///     <see cref="LogbookOptions.ShutdownTimeout"/>. Events that could not be sent by then are lost.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _sender?.Dispose();
        }
#pragma warning disable CA1031 // The client must never throw into the host application.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Disposing must not fail the shutdown of the host application.
        }
    }
}
