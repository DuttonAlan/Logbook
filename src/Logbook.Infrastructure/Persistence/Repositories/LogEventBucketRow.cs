// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Infrastructure.Persistence.Repositories;

/// <summary>
///     A row of the query that counts the Log Events in the buckets of a chart.
/// </summary>
public sealed class LogEventBucketRow
{
    /// <summary>
    ///     Point in time (UTC) when the bucket starts.
    /// </summary>
    public DateTime Bucket { get; set; }

    /// <summary>
    ///     The number of Log Events of all levels.
    /// </summary>
    public long Total { get; set; }

    /// <summary>
    ///     The number of Log Events with the level Warning.
    /// </summary>
    public long Warnings { get; set; }

    /// <summary>
    ///     The number of Log Events with the level Error or Critical.
    /// </summary>
    public long Errors { get; set; }
}
