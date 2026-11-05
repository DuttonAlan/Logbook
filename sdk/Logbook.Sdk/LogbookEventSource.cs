// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Diagnostics.Tracing;

namespace Logbook.Sdk;

/// <summary>
///     Self-diagnostics of the SDK. It must not log through <c>ILogger</c>, as that would send its own logs to itself, so it
///     uses an <see cref="EventSource"/>, e.g. for <c>dotnet-trace</c> or an <see cref="EventListener"/>.
/// </summary>
/// <remarks>
///     Only numbers, reasons and names of exception types are written: never the API Key or the content of an event.
/// </remarks>
[EventSource(Name = SourceName)]
internal sealed class LogbookEventSource : EventSource
{
    /// <summary>
    ///     The name to listen to.
    /// </summary>
    public const string SourceName = "Logbook-Sdk";

    /// <summary>
    ///     Events were dropped because the buffer reached a limit.
    /// </summary>
    public const string ReasonBufferFull = "BufferFull";

    /// <summary>
    ///     Events were dropped because they are too large, even after cutting their text.
    /// </summary>
    public const string ReasonTooLarge = "TooLarge";

    /// <summary>
    ///     Events were dropped because their timestamp is outside of what the server accepts.
    /// </summary>
    public const string ReasonExpired = "Expired";

    /// <summary>
    ///     Events were dropped because they could not be sent before the shutdown ended.
    /// </summary>
    public const string ReasonShutdown = "Shutdown";

    /// <summary>
    ///     The single instance.
    /// </summary>
    public static readonly LogbookEventSource Log = new();

    private LogbookEventSource()
    {
    }

    /// <summary>
    ///     The client cannot work and ignores all events.
    /// </summary>
    /// <param name="cause">The name of the setting that cannot be used, or of the exception that occurred.</param>
    [Event(1, Level = EventLevel.Error, Message = "The Logbook client is disabled because of {0}.")]
    public void ClientDisabled(string cause) => WriteEvent(1, cause);

    /// <summary>
    ///     A batch was accepted by the server.
    /// </summary>
    /// <param name="eventCount">The number of events.</param>
    /// <param name="compressedBytes">The size of the request body.</param>
    [Event(2, Level = EventLevel.Verbose, Message = "Sent {0} events in {1} bytes.")]
    public void BatchSent(int eventCount, int compressedBytes) => WriteEvent(2, eventCount, compressedBytes);

    /// <summary>
    ///     An attempt failed for a temporary reason. The events stay in the buffer.
    /// </summary>
    /// <param name="error">The status code of the response, or the name of the exception.</param>
    /// <param name="eventCount">The number of events in the attempt.</param>
    /// <param name="retryDelayMilliseconds">The time until the next attempt.</param>
    [Event(3, Level = EventLevel.Warning, Message = "Sending {1} events failed with {0}. Trying again in {2} ms.")]
    public void RequestFailed(string error, int eventCount, int retryDelayMilliseconds)
    {
        WriteEvent(3, error, eventCount, retryDelayMilliseconds);
    }

    /// <summary>
    ///     The server refused the request, e.g. with <c>401</c> for a wrong or revoked API Key. The events stay in the buffer.
    /// </summary>
    /// <param name="statusCode">The status code of the response.</param>
    /// <param name="eventCount">The number of events in the attempt.</param>
    /// <param name="retryDelayMilliseconds">The time until the next attempt.</param>
    [Event(4, Level = EventLevel.Error, Message = "The server refused {1} events with {0}. Check the API Key and the endpoint. Trying again in {2} ms.")]
    public void RequestRefused(int statusCode, int eventCount, int retryDelayMilliseconds)
    {
        WriteEvent(4, statusCode, eventCount, retryDelayMilliseconds);
    }

    /// <summary>
    ///     The server rejected a batch, and sending it again would not help, so it was dropped.
    /// </summary>
    /// <param name="statusCode">The status code of the response.</param>
    /// <param name="eventCount">The number of events dropped.</param>
    [Event(5, Level = EventLevel.Error, Message = "The server rejected a batch with {0}, so {1} events were dropped.")]
    public void BatchRejected(int statusCode, int eventCount) => WriteEvent(5, statusCode, eventCount);

    /// <summary>
    ///     Events were dropped.
    /// </summary>
    /// <param name="reason">Why they were dropped.</param>
    /// <param name="eventCount">The number of events.</param>
    [Event(6, Level = EventLevel.Warning, Message = "{1} events were dropped: {0}.")]
    public void EventsDropped(string reason, int eventCount) => WriteEvent(6, reason, eventCount);

    /// <summary>
    ///     Events that were too large were sent with their text cut.
    /// </summary>
    /// <param name="eventCount">The number of events.</param>
    [Event(7, Level = EventLevel.Warning, Message = "{0} events were too large and were sent with their text cut.")]
    public void EventsTruncated(int eventCount) => WriteEvent(7, eventCount);
}
