// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.Metrics.GetSeries;

/// <summary>
///     Represents the measurements of a metric in one bucket of a chart.
/// </summary>
/// <param name="Timestamp" example="2026-11-04T10:00:00Z">Point in time (UTC) when the bucket starts.</param>
/// <param name="Count" example="120">The number of measurements, 0 if there were none.</param>
/// <param name="Sum" example="15400.5">The sum of the measurements.</param>
/// <param name="Min" example="12.5">The smallest measurement, or null if there were none.</param>
/// <param name="Max" example="320">The largest measurement, or null if there were none.</param>
public sealed record MetricBucketResponse(DateTime Timestamp, long Count, double Sum, double? Min, double? Max);
