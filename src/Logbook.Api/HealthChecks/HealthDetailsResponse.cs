// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Logbook.Api.HealthChecks;

/// <summary>
///     Represents the detailed health report, for authorized monitoring.
/// </summary>
/// <param name="Status">Overall status: the worst status of all checks.</param>
/// <param name="Version">The version of Logbook that is running.</param>
/// <param name="Duration">How long the checks took.</param>
/// <param name="Checks">The result of each check.</param>
public sealed record HealthDetailsResponse(
    HealthStatus Status,
    string? Version,
    TimeSpan Duration,
    IReadOnlyList<HealthCheckDetails> Checks);

/// <summary>
///     Represents the result of one health check.
/// </summary>
/// <param name="Name">Name of the check.</param>
/// <param name="Status">Status of the check.</param>
/// <param name="Description">What the check found, if it says so.</param>
/// <param name="Duration">How long the check took.</param>
/// <param name="Data">Further values of the check, e.g. the pending migrations.</param>
public sealed record HealthCheckDetails(
    string Name,
    HealthStatus Status,
    string? Description,
    TimeSpan Duration,
    IReadOnlyDictionary<string, object> Data);
