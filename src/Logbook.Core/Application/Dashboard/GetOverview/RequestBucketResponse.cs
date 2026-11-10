// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.Dashboard.GetOverview;

/// <summary>
///     Represents the number of requests in one bucket of a chart.
/// </summary>
/// <param name="Timestamp" example="2026-11-04T10:00:00Z">Point in time (UTC) when the bucket starts.</param>
/// <param name="Count" example="840">The number of requests that started in the bucket.</param>
/// <param name="Failed" example="7">The number of those that failed.</param>
public sealed record RequestBucketResponse(DateTime Timestamp, long Count, long Failed);
