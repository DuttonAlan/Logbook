// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Diagnostics;

namespace Logbook.Sdk;

/// <summary>
///     One measured operation to send to Logbook: a request the application received, or a call it made. It never changes
///     after it is created, so it can wait in the buffer safely.
/// </summary>
/// <remarks>
///     The server rejects a whole batch for a single value it does not accept, so values are made acceptable here: text
///     over a limit of the server is cut, a negative or undefined duration becomes 0, an empty name becomes
///     <see cref="UnnamedName"/>, and a missing Trace Id or Span Id gets a new random value.
/// </remarks>
public sealed class LogbookSpan : ITelemetryItem
{
    /// <summary>
    ///     The name of a span that was created without one.
    /// </summary>
    public const string UnnamedName = "(unnamed)";

    // The limits of the server, which are fixed.
    private const int NameMaxLength = 500;
    private const int EnvironmentMaxLength = 100;
    private const int TraceFieldMaxLength = 64;
    private const int DependencyTypeMaxLength = 100;
    private const int TargetMaxLength = 500;
    private const int DataMaxLength = 8 * 1024;

    // Roughly the object itself, the headers of its strings, and its place in the buffer.
    private const long MemoryOverheadBytes = 512;

    /// <summary>
    ///     Creates a span.
    /// </summary>
    /// <param name="timestamp">Point in time when the operation started.</param>
    /// <param name="kind">What the span measures.</param>
    /// <param name="name">The name, with few distinct values, e.g. <c>GET /orders/{id}</c>.</param>
    /// <param name="durationMilliseconds">How long the operation took, in milliseconds.</param>
    /// <param name="success">Whether the operation succeeded.</param>
    /// <param name="traceId">The W3C Trace Id.</param>
    /// <param name="spanId">The W3C Span Id.</param>
    /// <param name="parentSpanId">The W3C Span Id of the operation that caused this one.</param>
    /// <param name="environment">The Environment, e.g. <c>Production</c>.</param>
    /// <param name="statusCode">The status code of the operation, e.g. the HTTP status.</param>
    /// <param name="dependencyType">The type of a dependency, e.g. <c>HTTP</c> or <c>SQL</c>.</param>
    /// <param name="target">What a dependency called, e.g. a host or a database.</param>
    /// <param name="data">The details of the operation, e.g. the path of the URL or an SQL statement.</param>
    public LogbookSpan(
        DateTimeOffset timestamp,
        LogbookSpanKind kind,
        string? name,
        double durationMilliseconds,
        bool success,
        string? traceId = null,
        string? spanId = null,
        string? parentSpanId = null,
        string? environment = null,
        int? statusCode = null,
        string? dependencyType = null,
        string? target = null,
        string? data = null)
    {
        Timestamp = timestamp;
        Kind = kind;
        Name = string.IsNullOrWhiteSpace(name) ? UnnamedName : LogEventGuard.Cut(name!, NameMaxLength);
        DurationMilliseconds = double.IsFinite(durationMilliseconds) && durationMilliseconds > 0 ? durationMilliseconds : 0;
        Success = success;
        TraceId = string.IsNullOrWhiteSpace(traceId) ? ActivityTraceId.CreateRandom().ToHexString() : LogEventGuard.Cut(traceId!, TraceFieldMaxLength);
        SpanId = string.IsNullOrWhiteSpace(spanId) ? ActivitySpanId.CreateRandom().ToHexString() : LogEventGuard.Cut(spanId!, TraceFieldMaxLength);
        ParentSpanId = CutOptional(parentSpanId, TraceFieldMaxLength);
        Environment = CutOptional(environment, EnvironmentMaxLength);
        StatusCode = statusCode;
        DependencyType = CutOptional(dependencyType, DependencyTypeMaxLength);
        Target = CutOptional(target, TargetMaxLength);
        Data = CutOptional(data, DataMaxLength);
    }

    /// <summary>
    ///     Point in time when the operation started.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    ///     What the span measures.
    /// </summary>
    public LogbookSpanKind Kind { get; }

    /// <summary>
    ///     The name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     How long the operation took, in milliseconds.
    /// </summary>
    public double DurationMilliseconds { get; }

    /// <summary>
    ///     Whether the operation succeeded.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    ///     The W3C Trace Id.
    /// </summary>
    public string TraceId { get; }

    /// <summary>
    ///     The W3C Span Id.
    /// </summary>
    public string SpanId { get; }

    /// <summary>
    ///     The W3C Span Id of the operation that caused this one.
    /// </summary>
    public string? ParentSpanId { get; }

    /// <summary>
    ///     The Environment.
    /// </summary>
    public string? Environment { get; }

    /// <summary>
    ///     The status code of the operation.
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    ///     The type of a dependency.
    /// </summary>
    public string? DependencyType { get; }

    /// <summary>
    ///     What a dependency called.
    /// </summary>
    public string? Target { get; }

    /// <summary>
    ///     The details of the operation.
    /// </summary>
    public string? Data { get; }

    /// <inheritdoc/>
    long ITelemetryItem.GetMemoryBytes()
    {
        var characters = (long)Name.Length
            + TraceId.Length
            + SpanId.Length
            + (ParentSpanId?.Length ?? 0)
            + (Environment?.Length ?? 0)
            + (DependencyType?.Length ?? 0)
            + (Target?.Length ?? 0)
            + (Data?.Length ?? 0);

        return (characters * sizeof(char)) + MemoryOverheadBytes;
    }

    private static string? CutOptional(string? value, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value) ? null : LogEventGuard.Cut(value!, maxLength);
    }
}
