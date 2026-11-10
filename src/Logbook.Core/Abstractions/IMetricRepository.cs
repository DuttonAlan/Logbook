// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Application.Charts;
using Logbook.Core.Application.Metrics.GetNames;
using Logbook.Core.Application.Metrics.GetSeries;
using Logbook.Core.Domain.Entities;

namespace Logbook.Core.Abstractions;

/// <summary>
///     Defines a contract for operations on the metric points.
/// </summary>
public interface IMetricRepository : ITelemetryStore<MetricPoint>
{
    /// <summary>
    ///     Gets the names of the metrics that have points in a time range, ordered by name.
    /// </summary>
    /// <param name="from">Only points at or after this point in time (UTC).</param>
    /// <param name="to">Only points before this point in time (UTC).</param>
    /// <param name="applicationId">Only points of this Application.</param>
    /// <param name="environment">Only points of this Environment, compared exactly.</param>
    /// <param name="take">The maximum number of names to return.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The names, with the latest point of each.</returns>
    public Task<IReadOnlyList<MetricNameResponse>> GetNames(
        DateTime from,
        DateTime to,
        Guid? applicationId,
        string? environment,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Combines the points of a metric into the buckets of a chart. Buckets without points are left out.
    /// </summary>
    /// <param name="range">The time range and its buckets.</param>
    /// <param name="name">The name of the metric, compared exactly.</param>
    /// <param name="applicationId">Only points of this Application.</param>
    /// <param name="environment">Only points of this Environment, compared exactly.</param>
    /// <param name="dimensionsJson">Only points with all of these dimensions, as a JSON object of strings, or null.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The buckets with points, oldest first.</returns>
    public Task<IReadOnlyList<MetricBucketResponse>> GetSeries(
        ChartRange range,
        string name,
        Guid? applicationId,
        string? environment,
        string? dimensionsJson,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes all metric points of an Application, in several steps. If it is interrupted, calling it again finishes it.
    /// </summary>
    /// <param name="applicationId">Id of the Application.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The number of deleted metric points.</returns>
    public Task<long> DeleteByApplication(Guid applicationId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes the expired metric points of an Application, in several steps. If it is interrupted, calling it again
    ///     finishes it. See <see cref="ISpanRepository.DeleteExpired"/> for how a change of the retention is handled.
    /// </summary>
    /// <param name="applicationId">Id of the Application. Nothing is deleted if it no longer exists.</param>
    /// <param name="defaultRetentionDays">The days to keep metric points, if the Application has no retention of its own.</param>
    /// <param name="now">The current point in time (UTC).</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The number of deleted metric points.</returns>
    public Task<long> DeleteExpired(Guid applicationId, int defaultRetentionDays, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Drops the partition of the oldest day that has expired for all Applications, with the metric points in it. See
    ///     <see cref="ISpanRepository.DropOldestExpiredPartition"/> for how a change of a retention is handled.
    /// </summary>
    /// <param name="defaultRetentionDays">The days to keep metric points of an Application without a retention of its own.</param>
    /// <param name="now">The current point in time (UTC).</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The day of the dropped partition, or null if no partition has expired.</returns>
    public Task<DateOnly?> DropOldestExpiredPartition(int defaultRetentionDays, DateTime now, CancellationToken cancellationToken = default);
}
