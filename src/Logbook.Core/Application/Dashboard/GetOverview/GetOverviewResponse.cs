// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.Dashboard.GetOverview;

/// <summary>
///     Represents the overview of a time range: how many Log Events and requests there were, how many failed, and which
///     requests were the slowest.
/// </summary>
/// <param name="From" example="2026-11-04T00:00:00Z">Point in time (UTC) when the first bucket starts.</param>
/// <param name="To" example="2026-11-05T00:00:00Z">Point in time (UTC) up to which data is counted.</param>
/// <param name="BucketSeconds" example="900">The width of a bucket in seconds.</param>
/// <param name="LogEvents">The number of Log Events in every bucket of the time range, oldest first.</param>
/// <param name="Requests">The number of requests in every bucket of the time range, oldest first.</param>
/// <param name="RequestCount" example="20160">The number of requests in the time range.</param>
/// <param name="FailedRequestCount" example="42">The number of those that failed. Divided by the number of requests, it is the error rate.</param>
/// <param name="SlowestRequests">The requests with the longest 95th percentile of their duration, slowest first.</param>
public sealed record GetOverviewResponse(
    DateTime From,
    DateTime To,
    long BucketSeconds,
    IReadOnlyList<LogEventBucketResponse> LogEvents,
    IReadOnlyList<RequestBucketResponse> Requests,
    long RequestCount,
    long FailedRequestCount,
    IReadOnlyList<SlowRequestResponse> SlowestRequests);
