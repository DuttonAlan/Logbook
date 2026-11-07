// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.ComponentModel.DataAnnotations;

using Logbook.Core.Domain.Entities;

namespace Logbook.Core.Application.Spans.Ingest;

/// <summary>
///     Represents one span in a request to receive spans.
/// </summary>
/// <param name="Timestamp" example="2026-11-04T10:15:00.123Z">Point in time when the operation started, with an offset or Z.</param>
/// <param name="Kind" example="Request">What the span measures: a Request the Application received, or a Dependency it called.</param>
/// <param name="Name" example="GET /orders/{id}">The name, with few distinct values.</param>
/// <param name="DurationMs" example="42.5">How long the operation took, in milliseconds.</param>
/// <param name="Success" example="true">Whether the operation succeeded.</param>
/// <param name="TraceId" example="4bf92f3577b34da6a3ce929d0e0e4736">The W3C Trace Id.</param>
/// <param name="SpanId" example="00f067aa0ba902b7">The W3C Span Id.</param>
/// <param name="ParentSpanId" example="53995c3f42cd8ad8">The W3C Span Id of the operation that caused this one.</param>
/// <param name="Environment" example="Production">The Environment. A span without one is stored with the Environment <c>Unknown</c>.</param>
/// <param name="StatusCode" example="200">The status code of the operation, e.g. the HTTP status.</param>
/// <param name="DependencyType" example="HTTP">The type of a dependency.</param>
/// <param name="Target" example="payments.example.com">What a dependency called, e.g. a host or a database.</param>
/// <param name="Data" example="/orders/42">The details of the operation, e.g. a path or an SQL statement.</param>
public sealed record SpanRequest(
    [Required] DateTimeOffset? Timestamp,
    [Required] SpanKind? Kind,
    [Required] string Name,
    [Required] double? DurationMs,
    [Required] bool? Success,
    [Required] string TraceId,
    [Required] string SpanId,
    string? ParentSpanId = null,
    string? Environment = null,
    int? StatusCode = null,
    string? DependencyType = null,
    string? Target = null,
    string? Data = null);
