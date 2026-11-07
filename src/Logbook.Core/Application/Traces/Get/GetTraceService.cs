// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Application.Exceptions;
using Logbook.Core.Application.LogEvents.Search;
using Logbook.Core.Application.Spans.Search;

namespace Logbook.Core.Application.Traces.Get;

/// <summary>
///     The Service to get everything stored for one trace.
/// </summary>
/// <param name="spanRepository">An Instance of a ISpanRepository to read the spans.</param>
/// <param name="logEventRepository">An Instance of a ILogEventRepository to read the Log Events.</param>
public sealed class GetTraceService(ISpanRepository spanRepository, ILogEventRepository logEventRepository)
{
    /// <summary>
    ///     The maximum number of spans, and of Log Events, in the response.
    /// </summary>
    public const int MaxItems = 1000;

    /// <summary>
    ///     Gets the requests, dependencies and Log Events of a trace, across all Applications and Environments.
    /// </summary>
    /// <param name="traceId">The W3C Trace Id, compared exactly.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The trace, oldest first.</returns>
    /// <exception cref="NotFoundException">Thrown when nothing is stored for the trace.</exception>
    public async Task<GetTraceResponse> ExecuteAsync(string traceId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(traceId);

        // One more than the limit tells whether there are more.
        var spans = await spanRepository.GetByTrace(traceId, MaxItems + 1, cancellationToken);
        var logEvents = await logEventRepository.GetByTrace(traceId, MaxItems + 1, cancellationToken);

        if (spans.Count == 0 && logEvents.Count == 0)
        {
            throw new NotFoundException("Nothing is stored for this trace.");
        }

        return new GetTraceResponse(
            traceId,
            [.. spans.Take(MaxItems).Select(SpanResponse.From)],
            [.. logEvents.Take(MaxItems).Select(LogEventResponse.From)],
            spans.Count > MaxItems || logEvents.Count > MaxItems);
    }
}
