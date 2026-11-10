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
        var (start, end) = ResolveRange(from, to, now, TimeSpan.FromDays(1));

        // Counted from the start of the first bucket, which can be earlier than the requested start.
        var width = Widths.FirstOrDefault(candidate => Math.Ceiling((end - BucketOf(start, candidate)) / candidate) <= MaxBuckets, Widths[^1]);

        return new ChartRange(BucketOf(start, width), end, width);
    }

    /// <summary>
    ///     Fills in and checks a time range.
    /// </summary>
    /// <remarks>
    ///     The range must keep a week away from the smallest and the largest date that can be represented, so that neither
    ///     the default start, nor rounding down to a bucket, nor stepping through the buckets can go past them.
    /// </remarks>
    /// <param name="from">The start of the range, or null for <paramref name="defaultLength"/> before the end.</param>
    /// <param name="to">The end of the range, or null for <paramref name="now"/>.</param>
    /// <param name="now">The current point in time (UTC).</param>
    /// <param name="defaultLength">The length of the range if it has no start.</param>
    /// <returns>The start and end (UTC).</returns>
    /// <exception cref="DomainValidationException">Thrown when the range is empty, too long, or too close to the smallest or largest date.</exception>
    public static (DateTime Start, DateTime End) ResolveRange(DateTimeOffset? from, DateTimeOffset? to, DateTime now, TimeSpan defaultLength)
    {
        var earliest = DateTime.MinValue.AddDays(7);
        var latest = DateTime.MaxValue.AddDays(-7);

        var end = to?.UtcDateTime ?? now;

        if (end > latest || (from is null && end - earliest < defaultLength) || (from is { } start && start.UtcDateTime < earliest))
        {
            throw new DomainValidationException($"The time range must be between {earliest:yyyy-MM-dd} and {latest:yyyy-MM-dd}.", nameof(from));
        }

        var begin = from?.UtcDateTime ?? end - defaultLength;

        if (begin >= end)
        {
            throw new DomainValidationException("'from' must be earlier than 'to'.", nameof(from));
        }

        if (end - begin > MaxLength)
        {
            throw new DomainValidationException($"The time range must not be longer than {MaxLength.TotalDays} days.", nameof(from));
        }

        return (begin, end);
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
