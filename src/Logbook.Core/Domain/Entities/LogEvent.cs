// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Text;
using System.Text.Json;

using Logbook.Core.Domain.Exceptions;

namespace Logbook.Core.Domain.Entities;

/// <summary>
///     Represents a Log Event: one entry an <see cref="Application"/> sends to Logbook. A Log Event never changes.
/// </summary>
/// <remarks>
///     Text and JSON are made storable here, because PostgreSQL rejects some values that are valid in JSON, and one
///     such value would otherwise fail all Log Events written together with it.
/// </remarks>
public sealed class LogEvent
{
    /// <summary>
    ///     The Environment of a Log Event that was sent without one.
    /// </summary>
    public const string DefaultEnvironment = "Unknown";

    /// <summary>
    ///     Maximum length of the Environment.
    /// </summary>
    public const int EnvironmentMaxLength = 100;

    /// <summary>
    ///     Maximum length of the Category.
    /// </summary>
    public const int CategoryMaxLength = 500;

    /// <summary>
    ///     Maximum length of the Trace Id and the Span Id.
    /// </summary>
    public const int TraceFieldMaxLength = 64;

    /// <summary>
    ///     Maximum length of the Message. PostgreSQL cannot index the words of much longer texts for the full-text search,
    ///     so this applies regardless of the configured maximum size of a Log Event.
    /// </summary>
    public const int MessageMaxLength = 64 * 1024;

    /// <summary>
    ///     Maximum number of levels in the JSON of the Exception and the Properties, counting the object itself. The API
    ///     reads and writes JSON up to 32 levels, and a Log Event in a search response starts at level 4, so a deeper value
    ///     could be received but never returned.
    /// </summary>
    public const int MaxJsonDepth = 29;

    // Roughly the object itself, the headers of its strings, and its place in the queue.
    private const long MemoryOverheadBytes = 512;

    /// <summary>
    ///     Used by EF Core to materialize a Log Event from the database.
    /// </summary>
    private LogEvent()
    {
    }

    /// <summary>
    ///     Id of the Log Event.
    /// </summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>
    ///     Id of the <see cref="Application"/> that sent the Log Event, taken from its API Key.
    /// </summary>
    public Guid ApplicationId { get; private set; }

    /// <summary>
    ///     Point in time (UTC) when the Log Event occurred, set by the sender.
    /// </summary>
    public DateTime Timestamp { get; private set; }

    /// <summary>
    ///     Severity of the Log Event.
    /// </summary>
    public LogEventLevel Level { get; private set; }

    /// <summary>
    ///     The rendered message. Can be empty, e.g. when only an exception is logged.
    /// </summary>
    public string Message { get; private set; } = null!;

    /// <summary>
    ///     The message before rendering, e.g. <c>Order {OrderId} failed</c>.
    /// </summary>
    public string? MessageTemplate { get; private set; }

    /// <summary>
    ///     The category of the logger, usually a class name.
    /// </summary>
    public string? Category { get; private set; }

    /// <summary>
    ///     The Environment, e.g. <c>Production</c>. A filter label, not a security boundary.
    /// </summary>
    public string Environment { get; private set; } = null!;

    /// <summary>
    ///     The numeric Id of the event within its category.
    /// </summary>
    public int? EventId { get; private set; }

    /// <summary>
    ///     The exception as a JSON object.
    /// </summary>
    public string? Exception { get; private set; }

    /// <summary>
    ///     The W3C Trace Id.
    /// </summary>
    public string? TraceId { get; private set; }

    /// <summary>
    ///     The W3C Span Id.
    /// </summary>
    public string? SpanId { get; private set; }

    /// <summary>
    ///     The structured properties as a JSON object.
    /// </summary>
    public string? Properties { get; private set; }

    /// <summary>
    ///     Creates a new Log Event.
    /// </summary>
    /// <param name="applicationId">Id of the Application that sent the Log Event.</param>
    /// <param name="timestamp">Point in time (UTC) when the Log Event occurred.</param>
    /// <param name="level">Severity of the Log Event.</param>
    /// <param name="message">The rendered message.</param>
    /// <param name="messageTemplate">The message before rendering.</param>
    /// <param name="category">The category of the logger.</param>
    /// <param name="environment">The Environment. If it is empty, <see cref="DefaultEnvironment"/> is stored.</param>
    /// <param name="eventId">The numeric Id of the event within its category.</param>
    /// <param name="exception">The exception, a JSON object or null.</param>
    /// <param name="traceId">The W3C Trace Id.</param>
    /// <param name="spanId">The W3C Span Id.</param>
    /// <param name="properties">The structured properties, a JSON object or null.</param>
    /// <returns>The created Log Event.</returns>
    /// <exception cref="DomainValidationException">Thrown when a value is not valid. The message never contains received values.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="timestamp"/> is not in UTC.</exception>
    public static LogEvent Create(
        Guid applicationId,
        DateTime timestamp,
        LogEventLevel level,
        string message,
        string? messageTemplate = null,
        string? category = null,
        string? environment = null,
        int? eventId = null,
        JsonElement? exception = null,
        string? traceId = null,
        string? spanId = null,
        JsonElement? properties = null)
    {
        UtcGuard.EnsureUtc(timestamp);

        if (!Enum.IsDefined(level))
        {
            throw new DomainValidationException("The level is not valid.", nameof(level));
        }

        if (message is null)
        {
            throw new DomainValidationException("The message must not be null.", nameof(message));
        }

        return new LogEvent
        {
            ApplicationId = applicationId,
            Timestamp = timestamp,
            Level = level,
            Message = NormalizeText(message, MessageMaxLength, nameof(message)),
            MessageTemplate = NormalizeOptionalText(messageTemplate, MessageMaxLength, nameof(messageTemplate)),
            Category = NormalizeOptionalText(category, CategoryMaxLength, nameof(category)),
            Environment = NormalizeOptionalText(environment, EnvironmentMaxLength, nameof(environment)) ?? DefaultEnvironment,
            EventId = eventId,
            Exception = LogEventJson.NormalizeObject(exception, nameof(exception)),
            TraceId = NormalizeOptionalText(traceId, TraceFieldMaxLength, nameof(traceId)),
            SpanId = NormalizeOptionalText(spanId, TraceFieldMaxLength, nameof(spanId)),
            Properties = LogEventJson.NormalizeObject(properties, nameof(properties))
        };
    }

    /// <summary>
    ///     Gets the size of the content of the Log Event, which is limited by the setting <c>LogIngestion:MaxEventBytes</c>.
    /// </summary>
    /// <returns>The size of the free-form fields in UTF-8 bytes.</returns>
    public long GetContentBytes()
    {
        return Encoding.UTF8.GetByteCount(Message)
            + (long)Encoding.UTF8.GetByteCount(MessageTemplate ?? string.Empty)
            + Encoding.UTF8.GetByteCount(Exception ?? string.Empty)
            + Encoding.UTF8.GetByteCount(Properties ?? string.Empty);
    }

    /// <summary>
    ///     Estimates the memory the Log Event holds while it waits in the queue. Unlike the size on the wire, a string
    ///     takes two bytes for each character, so text mostly in ASCII takes about twice as much.
    /// </summary>
    /// <returns>The estimated size in bytes.</returns>
    public long GetMemoryBytes()
    {
        var characters = (long)Message.Length
            + (MessageTemplate?.Length ?? 0)
            + (Category?.Length ?? 0)
            + Environment.Length
            + (Exception?.Length ?? 0)
            + (TraceId?.Length ?? 0)
            + (SpanId?.Length ?? 0)
            + (Properties?.Length ?? 0);

        return (characters * sizeof(char)) + MemoryOverheadBytes;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength, string paramName)
    {
        return string.IsNullOrWhiteSpace(value) ? null : NormalizeText(value.Trim(), maxLength, paramName);
    }

    private static string NormalizeText(string value, int maxLength, string paramName)
    {
        if (value.Length > maxLength)
        {
            throw new DomainValidationException($"The value must not be longer than {maxLength} characters.", paramName);
        }

        return LogEventJson.ReplaceNullCharacters(value);
    }
}
