// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Application.LogEvents.Search;
using Logbook.Core.Application.Spans.Search;

namespace Logbook.Core.Application.Traces.Get;

/// <summary>
///     Represents everything stored for one trace: its transaction.
/// </summary>
/// <param name="TraceId" example="4bf92f3577b34da6a3ce929d0e0e4736">The W3C Trace Id.</param>
/// <param name="Spans">The requests and dependencies of the trace, oldest first.</param>
/// <param name="LogEvents">The Log Events of the trace, oldest first.</param>
/// <param name="IsTruncated" example="false">Whether the trace has more spans or Log Events than the response contains. The latest are left out.</param>
public sealed record GetTraceResponse(
    string TraceId,
    IReadOnlyList<SpanResponse> Spans,
    IReadOnlyList<LogEventResponse> LogEvents,
    bool IsTruncated);
