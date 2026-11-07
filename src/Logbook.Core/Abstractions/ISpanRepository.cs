// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Application.LogEvents.Search;
using Logbook.Core.Application.Spans.Search;
using Logbook.Core.Domain.Entities;

namespace Logbook.Core.Abstractions;

/// <summary>
///     Defines a contract for span operations.
/// </summary>
public interface ISpanRepository : ITelemetryStore<TraceSpan>
{
    /// <summary>
    ///     Searches spans, newest first.
    /// </summary>
    /// <param name="filter">The filters to apply.</param>
    /// <param name="after">The position to continue after, or null to start with the newest span.</param>
    /// <param name="take">The maximum number of spans to return.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The untracked spans.</returns>
    public Task<IReadOnlyList<TraceSpan>> Search(
        SpanFilter filter,
        LogEventCursor? after,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the spans of a trace, oldest first, as a trace reads from its start.
    /// </summary>
    /// <param name="traceId">The W3C Trace Id, compared exactly.</param>
    /// <param name="take">The maximum number of spans to return.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The untracked spans.</returns>
    public Task<IReadOnlyList<TraceSpan>> GetByTrace(string traceId, int take, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes all spans of an Application, in several steps. If it is interrupted, calling it again finishes it.
    /// </summary>
    /// <param name="applicationId">Id of the Application.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The number of deleted spans.</returns>
    public Task<long> DeleteByApplication(Guid applicationId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes the expired spans of an Application, in several steps. If it is interrupted, calling it again finishes it.
    /// </summary>
    /// <remarks>
    ///     Each step reads the retention of the Application and holds it until its spans are deleted, so that a retention
    ///     raised in the meantime applies to everything that has not been deleted yet.
    /// </remarks>
    /// <param name="applicationId">Id of the Application. Nothing is deleted if it no longer exists.</param>
    /// <param name="defaultRetentionDays">The days to keep spans, if the Application has no retention of its own.</param>
    /// <param name="now">The current point in time (UTC).</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The number of deleted spans.</returns>
    public Task<long> DeleteExpired(Guid applicationId, int defaultRetentionDays, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Drops the partition of the oldest day that has expired for all Applications, with the spans in it. One at a time,
    ///     so that the lock on the table of all spans is released in between.
    /// </summary>
    /// <remarks>
    ///     This reads the retention of all Applications and holds it until the partition is dropped, so that a retention
    ///     raised in the meantime applies to everything that has not been dropped yet.
    /// </remarks>
    /// <param name="defaultRetentionDays">The days to keep spans of an Application without a retention of its own.</param>
    /// <param name="now">The current point in time (UTC).</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The day of the dropped partition, or null if no partition has expired.</returns>
    public Task<DateOnly?> DropOldestExpiredPartition(int defaultRetentionDays, DateTime now, CancellationToken cancellationToken = default);
}
