// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.Metrics.GetNames;

/// <summary>
///     Represents a metric that has points in the requested time range.
/// </summary>
/// <param name="Name" example="orders.placed">The name of the metric.</param>
/// <param name="LastSeen" example="2026-11-04T10:15:00Z">Point in time (UTC) when the interval of its latest point started.</param>
public sealed record MetricNameResponse(string Name, DateTime LastSeen);
