// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Sdk;

/// <summary>
///     One Log Event to send to Logbook. It never changes after it is created, so it can wait in the buffer safely.
/// </summary>
public sealed class LogbookEvent
{
    // Roughly the object itself, the headers of its strings, and its place in the buffer.
    private const long MemoryOverheadBytes = 512;

    /// <summary>
    ///     Creates a Log Event.
    /// </summary>
    /// <param name="timestamp">Point in time when the Log Event occurred.</param>
    /// <param name="level">Severity of the Log Event.</param>
    /// <param name="message">The rendered message. Null is sent as an empty message.</param>
    /// <param name="messageTemplate">The message before rendering, e.g. <c>Order {OrderId} failed</c>.</param>
    /// <param name="category">The category of the logger, usually a class name.</param>
    /// <param name="environment">The Environment, e.g. <c>Production</c>.</param>
    /// <param name="eventId">The numeric Id of the event within its category.</param>
    /// <param name="exceptionJson">The exception as the text of a JSON object. If it is anything else, it is not sent.</param>
    /// <param name="traceId">The W3C Trace Id.</param>
    /// <param name="spanId">The W3C Span Id.</param>
    /// <param name="propertiesJson">The structured properties as the text of a JSON object. If it is anything else, it is not sent.</param>
    public LogbookEvent(
        DateTimeOffset timestamp,
        LogbookLevel level,
        string? message,
        string? messageTemplate = null,
        string? category = null,
        string? environment = null,
        int? eventId = null,
        string? exceptionJson = null,
        string? traceId = null,
        string? spanId = null,
        string? propertiesJson = null)
    {
        Timestamp = timestamp;
        Level = level;
        Message = message ?? string.Empty;
        MessageTemplate = messageTemplate;
        Category = category;
        Environment = environment;
        EventId = eventId;
        ExceptionJson = exceptionJson;
        TraceId = traceId;
        SpanId = spanId;
        PropertiesJson = propertiesJson;
    }

    /// <summary>
    ///     Point in time when the Log Event occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    ///     Severity of the Log Event.
    /// </summary>
    public LogbookLevel Level { get; }

    /// <summary>
    ///     The rendered message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    ///     The message before rendering.
    /// </summary>
    public string? MessageTemplate { get; }

    /// <summary>
    ///     The category of the logger.
    /// </summary>
    public string? Category { get; }

    /// <summary>
    ///     The Environment.
    /// </summary>
    public string? Environment { get; }

    /// <summary>
    ///     The numeric Id of the event within its category.
    /// </summary>
    public int? EventId { get; }

    /// <summary>
    ///     The exception as the text of a JSON object.
    /// </summary>
    public string? ExceptionJson { get; }

    /// <summary>
    ///     The W3C Trace Id.
    /// </summary>
    public string? TraceId { get; }

    /// <summary>
    ///     The W3C Span Id.
    /// </summary>
    public string? SpanId { get; }

    /// <summary>
    ///     The structured properties as the text of a JSON object.
    /// </summary>
    public string? PropertiesJson { get; }

    /// <summary>
    ///     Whether text of the Log Event was already cut because of a limit.
    /// </summary>
    internal bool IsTruncated { get; set; }

    /// <summary>
    ///     Estimates the memory the Log Event holds while it waits in the buffer. A string takes two bytes for each
    ///     character, so text mostly in ASCII takes about twice as much as it does on the wire.
    /// </summary>
    /// <returns>The estimated size in bytes.</returns>
    internal long GetMemoryBytes()
    {
        var characters = (long)Message.Length
            + (MessageTemplate?.Length ?? 0)
            + (Category?.Length ?? 0)
            + (Environment?.Length ?? 0)
            + (ExceptionJson?.Length ?? 0)
            + (TraceId?.Length ?? 0)
            + (SpanId?.Length ?? 0)
            + (PropertiesJson?.Length ?? 0);

        return (characters * sizeof(char)) + MemoryOverheadBytes;
    }
}
