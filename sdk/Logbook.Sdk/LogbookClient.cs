// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Sdk;

/// <summary>
///     Sends events to a Logbook server: they wait in a bounded in-memory buffer and are sent in batches in the background.
/// </summary>
/// <remarks>
///     The client never throws into and never blocks the host application. With options it cannot work with, e.g. without
///     an <see cref="LogbookOptions.Endpoint"/>, it stays disabled and ignores all events. Create one client and keep it
///     for the lifetime of the application.
/// </remarks>
public sealed class LogbookClient : IDisposable
{
    private readonly LogEventBuffer? _buffer;
    private readonly LogEventSender? _sender;

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
        if (options is null || !options.IsValid())
        {
            return;
        }

        try
        {
            var buffer = new LogEventBuffer(
                options.MaxQueueSize,
                options.MaxQueueBytes,
                options.BatchSize,
                options.MaxRequestBytes,
                timeProvider);

            var sender = new LogEventSender(options, buffer, handler, timeProvider);
            sender.Start();

            _buffer = buffer;
            _sender = sender;
        }
#pragma warning disable CA1031 // The client must never throw into the host application.
        catch (Exception)
#pragma warning restore CA1031
        {
            // E.g. a platform without support for the default handler. Logging is not worth failing the host for.
        }
    }

    /// <summary>
    ///     Whether the client has usable options and sends events.
    /// </summary>
    internal bool IsEnabled => _buffer is not null;

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
            _buffer?.Add(logEvent);
        }
#pragma warning disable CA1031 // The client must never throw into the host application.
        catch (Exception)
#pragma warning restore CA1031
        {
            // E.g. out of memory while the buffer grows. Losing one event is better than failing the caller.
        }
    }

    /// <summary>
    ///     Stops sending in the background. Events that were not sent yet are lost.
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
