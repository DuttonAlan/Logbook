// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Infrastructure.Persistence.Repositories;

/// <summary>
///     A row of the query that counts the requests in the buckets of a chart.
/// </summary>
public sealed class RequestBucketRow
{
    /// <summary>
    ///     Point in time (UTC) when the bucket starts.
    /// </summary>
    public DateTime Bucket { get; set; }

    /// <summary>
    ///     The number of requests.
    /// </summary>
    public long Count { get; set; }

    /// <summary>
    ///     The number of requests that failed.
    /// </summary>
    public long Failed { get; set; }
}
