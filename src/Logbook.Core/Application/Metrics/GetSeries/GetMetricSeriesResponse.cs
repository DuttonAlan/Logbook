// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.Metrics.GetSeries;

/// <summary>
///     Represents a metric over a time range, split into buckets of equal width.
/// </summary>
/// <param name="Name" example="orders.placed">The name of the metric.</param>
/// <param name="From" example="2026-11-04T00:00:00Z">Point in time (UTC) when the first bucket starts.</param>
/// <param name="To" example="2026-11-05T00:00:00Z">Point in time (UTC) up to which measurements are counted.</param>
/// <param name="BucketSeconds" example="900">The width of a bucket in seconds.</param>
/// <param name="Buckets">Every bucket of the time range, oldest first, also those without measurements.</param>
public sealed record GetMetricSeriesResponse(
    string Name,
    DateTime From,
    DateTime To,
    long BucketSeconds,
    IReadOnlyList<MetricBucketResponse> Buckets);
