// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.Entities;

namespace Logbook.Core.Application.Spans.Search;

/// <summary>
///     Represents a stored span.
/// </summary>
/// <param name="Id" example="0199d1a2-7c3e-7b4a-9f10-7c963f66afa6">Id of the span.</param>
/// <param name="ApplicationId" example="0199d1a2-7c3e-7b4a-9f10-5c963f66afa6">Id of the Application that sent the span.</param>
/// <param name="Timestamp" example="2026-11-04T10:15:00.123Z">Point in time (UTC) when the operation started.</param>
/// <param name="Kind" example="Request">What the span measures.</param>
/// <param name="Name" example="GET /orders/{id}">The name.</param>
/// <param name="DurationMs" example="42.5">How long the operation took, in milliseconds.</param>
/// <param name="Success" example="true">Whether the operation succeeded.</param>
/// <param name="Environment" example="Production">The Environment.</param>
/// <param name="TraceId" example="4bf92f3577b34da6a3ce929d0e0e4736">The W3C Trace Id.</param>
/// <param name="SpanId" example="00f067aa0ba902b7">The W3C Span Id.</param>
/// <param name="ParentSpanId" example="53995c3f42cd8ad8">The W3C Span Id of the operation that caused this one.</param>
/// <param name="StatusCode" example="200">The status code of the operation.</param>
/// <param name="DependencyType" example="HTTP">The type of a dependency.</param>
/// <param name="Target" example="payments.example.com">What a dependency called.</param>
/// <param name="Data" example="/orders/42">The details of the operation.</param>
public sealed record SpanResponse(
    Guid Id,
    Guid ApplicationId,
    DateTime Timestamp,
    SpanKind Kind,
    string Name,
    double DurationMs,
    bool Success,
    string Environment,
    string TraceId,
    string SpanId,
    string? ParentSpanId,
    int? StatusCode,
    string? DependencyType,
    string? Target,
    string? Data)
{
    /// <summary>
    ///     Creates the response for a stored span.
    /// </summary>
    /// <param name="span">The span.</param>
    /// <returns>The response.</returns>
    public static SpanResponse From(TraceSpan span)
    {
        ArgumentNullException.ThrowIfNull(span);

        return new SpanResponse(
            span.Id,
            span.ApplicationId,
            span.Timestamp,
            span.Kind,
            span.Name,
            span.DurationMs,
            span.Success,
            span.Environment,
            span.TraceId,
            span.SpanId,
            span.ParentSpanId,
            span.StatusCode,
            span.DependencyType,
            span.Target,
            span.Data);
    }
}
