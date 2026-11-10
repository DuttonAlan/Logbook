// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.Exceptions;

namespace Logbook.Core.Application.Charts;

/// <summary>
///     The time range of a chart, split into buckets of equal width.
/// </summary>
/// <remarks>
///     The width comes from a fixed list, so that buckets line up between requests and with the same calculation in
///     PostgreSQL (<c>date_bin</c>, with <see cref="Origin"/>). The range starts at the beginning of the bucket that contains
///     <c>from</c>, so the first bucket is complete as well.
/// </remarks>
/// <param name="From">Point in time (UTC) when the first bucket starts.</param>
/// <param name="To">Point in time (UTC) up to which data is counted, not included.</param>
/// <param name="BucketWidth">The width of one bucket.</param>
public sealed record ChartRange(DateTime From, DateTime To, TimeSpan BucketWidth)
{
    /// <summary>
    ///     The most buckets a chart is split into, unless the range is longer than the widest bucket allows.
    /// </summary>
    public const int MaxBuckets = 120;

    /// <summary>
    ///     The longest time range of a chart: the longest retention of an Application.
    /// </summary>
    public static readonly TimeSpan MaxLength = TimeSpan.FromDays(Domain.Entities.Application.MaxRetentionDays);

    /// <summary>
    ///     The point in time the buckets are aligned to: a Monday, so that a bucket of a week starts on a Monday.
    /// </summary>
    public static readonly DateTime Origin = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan[] Widths =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(3),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(12),
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(7)
    ];

    /// <summary>
    ///     The start of each bucket, oldest first.
    /// </summary>
    public IEnumerable<DateTime> Buckets
    {
        get
        {
            for (var start = From; start < To; start += BucketWidth)
            {
                yield return start;
            }
        }
    }

    /// <summary>
    ///     Chooses the buckets for a time range.
    /// </summary>
    /// <param name="from">The start of the range, or null for one day before <paramref name="to"/>.</param>
    /// <param name="to">The end of the range, or null for now.</param>
    /// <param name="now">The current point in time (UTC).</param>
    /// <returns>The range of the chart.</returns>
    /// <exception cref="DomainValidationException">Thrown when the range is empty or too long.</exception>
    public static ChartRange Create(DateTimeOffset? from, DateTimeOffset? to, DateTime now)
    {
        var end = to?.UtcDateTime ?? now;
        var start = from?.UtcDateTime ?? end.AddDays(-1);

        if (start >= end)
        {
            throw new DomainValidationException("'from' must be earlier than 'to'.", nameof(from));
        }

        if (end - start > MaxLength)
        {
            throw new DomainValidationException($"The time range must not be longer than {MaxLength.TotalDays} days.", nameof(from));
        }

        var length = end - start;
        var width = Widths.FirstOrDefault(candidate => length / candidate <= MaxBuckets, Widths[^1]);

        return new ChartRange(BucketOf(start, width), end, width);
    }

    /// <summary>
    ///     Gets the start of the bucket a point in time belongs to.
    /// </summary>
    /// <param name="timestamp">The point in time (UTC).</param>
    /// <returns>The start of its bucket.</returns>
    public DateTime BucketOf(DateTime timestamp) => BucketOf(timestamp, BucketWidth);

    private static DateTime BucketOf(DateTime timestamp, TimeSpan width)
    {
        var ticks = timestamp.Ticks - Origin.Ticks;
        var floored = ticks - (((ticks % width.Ticks) + width.Ticks) % width.Ticks);

        return new DateTime(Origin.Ticks + floored, DateTimeKind.Utc);
    }
}
