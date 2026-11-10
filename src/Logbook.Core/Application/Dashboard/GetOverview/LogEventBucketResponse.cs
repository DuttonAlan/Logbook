// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.Dashboard.GetOverview;

/// <summary>
///     Represents the number of Log Events in one bucket of a chart.
/// </summary>
/// <param name="Timestamp" example="2026-11-04T10:00:00Z">Point in time (UTC) when the bucket starts.</param>
/// <param name="Total" example="1520">The number of Log Events of all levels.</param>
/// <param name="Warnings" example="12">The number of Log Events with the level Warning.</param>
/// <param name="Errors" example="3">The number of Log Events with the level Error or Critical.</param>
public sealed record LogEventBucketResponse(DateTime Timestamp, long Total, long Warnings, long Errors);
