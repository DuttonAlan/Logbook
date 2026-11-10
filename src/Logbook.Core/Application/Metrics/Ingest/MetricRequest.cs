// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.ComponentModel.DataAnnotations;

namespace Logbook.Core.Application.Metrics.Ingest;

/// <summary>
///     Represents one metric point in a request to receive metrics: the measurements of one metric over a short interval,
///     aggregated by the sender.
/// </summary>
/// <param name="Timestamp" example="2026-11-04T10:15:00Z">Point in time when the interval of the measurements started, with an offset or Z.</param>
/// <param name="Name" example="orders.placed">The name of the metric.</param>
/// <param name="Count" example="12">The number of measurements, at least 1.</param>
/// <param name="Sum" example="1250.5">The sum of the measurements.</param>
/// <param name="Min" example="12.5">The smallest measurement.</param>
/// <param name="Max" example="320">The largest measurement.</param>
/// <param name="Environment" example="Production">The Environment. A metric point without one is stored with the Environment <c>Unknown</c>.</param>
/// <param name="Dimensions">The dimensions, at most 10, e.g. <c>{"Region": "EU"}</c>. A dimension without a value is left out.</param>
public sealed record MetricRequest(
    [Required] DateTimeOffset? Timestamp,
    [Required] string Name,
    [Required] long? Count,
    [Required] double? Sum,
    [Required] double? Min,
    [Required] double? Max,
    string? Environment = null,
    IReadOnlyDictionary<string, string?>? Dimensions = null);
