// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Infrastructure.Persistence.Repositories;

/// <summary>
///     A row of the query that combines the points of a metric into the buckets of a chart.
/// </summary>
public sealed class MetricBucketRow
{
    /// <summary>
    ///     Point in time (UTC) when the bucket starts.
    /// </summary>
    public DateTime Bucket { get; set; }

    /// <summary>
    ///     The number of measurements.
    /// </summary>
    public long Count { get; set; }

    /// <summary>
    ///     The sum of the measurements.
    /// </summary>
    public double Sum { get; set; }

    /// <summary>
    ///     The smallest measurement.
    /// </summary>
    public double Min { get; set; }

    /// <summary>
    ///     The largest measurement.
    /// </summary>
    public double Max { get; set; }
}
