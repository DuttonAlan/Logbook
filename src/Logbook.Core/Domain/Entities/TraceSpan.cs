// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.Exceptions;

namespace Logbook.Core.Domain.Entities;

/// <summary>
///     Represents one measured operation of an <see cref="Application"/> within a trace: a request it received, or a call it
///     made to a dependency. A span never changes.
/// </summary>
public sealed class TraceSpan : ITelemetryRecord
{
    /// <summary>
    ///     Maximum length of the Name.
    /// </summary>
    public const int NameMaxLength = 500;

    /// <summary>
    ///     Maximum length of the Dependency Type.
    /// </summary>
    public const int DependencyTypeMaxLength = 100;

    /// <summary>
    ///     Maximum length of the Target.
    /// </summary>
    public const int TargetMaxLength = 500;

    /// <summary>
    ///     Maximum length of the Data.
    /// </summary>
    public const int DataMaxLength = 8 * 1024;

    // Roughly the object itself, the headers of its strings, and its place in the queue.
    private const long MemoryOverheadBytes = 512;

    /// <summary>
    ///     Used by EF Core to materialize a span from the database.
    /// </summary>
    private TraceSpan()
    {
    }

    /// <summary>
    ///     Id of the span.
    /// </summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>
    ///     Id of the <see cref="Application"/> that sent the span, taken from its API Key.
    /// </summary>
    public Guid ApplicationId { get; private set; }

    /// <summary>
    ///     Point in time (UTC) when the operation started, set by the sender.
    /// </summary>
    public DateTime Timestamp { get; private set; }

    /// <summary>
    ///     What the span measures.
    /// </summary>
    public SpanKind Kind { get; private set; }

    /// <summary>
    ///     The name, with few distinct values, e.g. <c>GET /orders/{id}</c>.
    /// </summary>
    public string Name { get; private set; } = null!;

    /// <summary>
    ///     How long the operation took, in milliseconds.
    /// </summary>
    public double DurationMs { get; private set; }

    /// <summary>
    ///     Whether the operation succeeded.
    /// </summary>
    public bool Success { get; private set; }

    /// <summary>
    ///     The Environment, e.g. <c>Production</c>. A filter label, not a security boundary.
    /// </summary>
    public string Environment { get; private set; } = null!;

    /// <summary>
    ///     The W3C Trace Id.
    /// </summary>
    public string TraceId { get; private set; } = null!;

    /// <summary>
    ///     The W3C Span Id.
    /// </summary>
    public string SpanId { get; private set; } = null!;

    /// <summary>
    ///     The W3C Span Id of the operation that caused this one.
    /// </summary>
    public string? ParentSpanId { get; private set; }

    /// <summary>
    ///     The status code of the operation, e.g. the HTTP status.
    /// </summary>
    public int? StatusCode { get; private set; }

    /// <summary>
    ///     The type of a dependency, e.g. <c>HTTP</c> or <c>SQL</c>.
    /// </summary>
    public string? DependencyType { get; private set; }

    /// <summary>
    ///     What a dependency called, e.g. a host or a database.
    /// </summary>
    public string? Target { get; private set; }

    /// <summary>
    ///     The details of the operation, e.g. a path or an SQL statement.
    /// </summary>
    public string? Data { get; private set; }

    /// <summary>
    ///     Creates a new span.
    /// </summary>
    /// <param name="applicationId">Id of the Application that sent the span.</param>
    /// <param name="timestamp">Point in time (UTC) when the operation started.</param>
    /// <param name="kind">What the span measures.</param>
    /// <param name="name">The name.</param>
    /// <param name="durationMs">How long the operation took, in milliseconds.</param>
    /// <param name="success">Whether the operation succeeded.</param>
    /// <param name="traceId">The W3C Trace Id.</param>
    /// <param name="spanId">The W3C Span Id.</param>
    /// <param name="parentSpanId">The W3C Span Id of the operation that caused this one.</param>
    /// <param name="environment">The Environment. If it is empty, <see cref="LogEvent.DefaultEnvironment"/> is stored.</param>
    /// <param name="statusCode">The status code of the operation.</param>
    /// <param name="dependencyType">The type of a dependency.</param>
    /// <param name="target">What a dependency called.</param>
    /// <param name="data">The details of the operation.</param>
    /// <returns>The created span.</returns>
    /// <exception cref="DomainValidationException">Thrown when a value is not valid. The message never contains received values.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="timestamp"/> is not in UTC.</exception>
    public static TraceSpan Create(
        Guid applicationId,
        DateTime timestamp,
        SpanKind kind,
        string name,
        double durationMs,
        bool success,
        string traceId,
        string spanId,
        string? parentSpanId = null,
        string? environment = null,
        int? statusCode = null,
        string? dependencyType = null,
        string? target = null,
        string? data = null)
    {
        UtcGuard.EnsureUtc(timestamp);

        if (!Enum.IsDefined(kind))
        {
            throw new DomainValidationException("The kind is not valid.", nameof(kind));
        }

        if (!double.IsFinite(durationMs) || durationMs < 0)
        {
            throw new DomainValidationException("The duration must be a number that is not negative.", nameof(durationMs));
        }

        return new TraceSpan
        {
            ApplicationId = applicationId,
            Timestamp = timestamp,
            Kind = kind,
            Name = NormalizeRequired(name, NameMaxLength, nameof(name)),
            DurationMs = durationMs,
            Success = success,
            Environment = NormalizeOptional(environment, LogEvent.EnvironmentMaxLength, nameof(environment)) ?? LogEvent.DefaultEnvironment,
            TraceId = NormalizeRequired(traceId, LogEvent.TraceFieldMaxLength, nameof(traceId)),
            SpanId = NormalizeRequired(spanId, LogEvent.TraceFieldMaxLength, nameof(spanId)),
            ParentSpanId = NormalizeOptional(parentSpanId, LogEvent.TraceFieldMaxLength, nameof(parentSpanId)),
            StatusCode = statusCode,
            DependencyType = NormalizeOptional(dependencyType, DependencyTypeMaxLength, nameof(dependencyType)),
            Target = NormalizeOptional(target, TargetMaxLength, nameof(target)),
            Data = NormalizeOptional(data, DataMaxLength, nameof(data))
        };
    }

    /// <inheritdoc/>
    public long GetMemoryBytes()
    {
        var characters = (long)Name.Length
            + Environment.Length
            + TraceId.Length
            + SpanId.Length
            + (ParentSpanId?.Length ?? 0)
            + (DependencyType?.Length ?? 0)
            + (Target?.Length ?? 0)
            + (Data?.Length ?? 0);

        return (characters * sizeof(char)) + MemoryOverheadBytes;
    }

    private static string NormalizeRequired(string? value, int maxLength, string paramName)
    {
        return NormalizeOptional(value, maxLength, paramName)
            ?? throw new DomainValidationException("The value must not be empty.", paramName);
    }

    private static string? NormalizeOptional(string? value, int maxLength, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        if (trimmed.Length > maxLength)
        {
            throw new DomainValidationException($"The value must not be longer than {maxLength} characters.", paramName);
        }

        return LogEventJson.ReplaceNullCharacters(trimmed);
    }
}
