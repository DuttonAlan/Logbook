// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Infrastructure.Persistence.Repositories;

/// <summary>
///     A row of the query for the slowest requests: the requests of one Application with one name.
/// </summary>
public sealed class SlowRequestRow
{
    /// <summary>
    ///     Id of the Application.
    /// </summary>
    public Guid ApplicationId { get; set; }

    /// <summary>
    ///     The name of the requests.
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    ///     The number of requests.
    /// </summary>
    public long Count { get; set; }

    /// <summary>
    ///     The number of requests that failed.
    /// </summary>
    public long Failed { get; set; }

    /// <summary>
    ///     The average duration in milliseconds.
    /// </summary>
    public double AverageDurationMs { get; set; }

    /// <summary>
    ///     The 95th percentile of the duration in milliseconds.
    /// </summary>
    public double P95DurationMs { get; set; }

    /// <summary>
    ///     The longest duration in milliseconds.
    /// </summary>
    public double MaxDurationMs { get; set; }
}
