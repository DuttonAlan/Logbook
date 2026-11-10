// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.Metrics.GetNames;

/// <summary>
///     Represents the metrics that have points in the requested time range.
/// </summary>
/// <param name="Items">The metrics, ordered by name.</param>
public sealed record GetMetricNamesResponse(IReadOnlyList<MetricNameResponse> Items);
