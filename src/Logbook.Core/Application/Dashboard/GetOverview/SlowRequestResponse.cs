// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.Dashboard.GetOverview;

/// <summary>
///     Represents the requests of one Application with the same name, e.g. <c>GET /orders/{id}</c>, and how long they took.
/// </summary>
/// <param name="ApplicationId" example="0199d1a2-7c3e-7b4a-9f10-5c963f66afa6">Id of the Application.</param>
/// <param name="Name" example="GET /orders/{id}">The name of the requests.</param>
/// <param name="Count" example="840">The number of requests in the time range.</param>
/// <param name="Failed" example="7">The number of those that failed.</param>
/// <param name="AverageDurationMs" example="42.5">The average duration in milliseconds.</param>
/// <param name="P95DurationMs" example="180">The duration in milliseconds that 95 percent of the requests did not exceed.</param>
/// <param name="MaxDurationMs" example="1250">The longest duration in milliseconds.</param>
public sealed record SlowRequestResponse(
    Guid ApplicationId,
    string Name,
    long Count,
    long Failed,
    double AverageDurationMs,
    double P95DurationMs,
    double MaxDurationMs);
