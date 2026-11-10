// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Application.Charts;
using Logbook.Core.Domain.Exceptions;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks how a time range is split into the buckets of a chart. Needs no database.
/// </summary>
public sealed class ChartRangeTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 30, DateTimeKind.Utc);

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(24)]
    [InlineData(24 * 7)]
    [InlineData(24 * 120)]
    public void ARangeThatDoesNotStartOnABucket_StillHasAtMostTheMaximum(int hours)
    {
        var to = new DateTimeOffset(Now);
        var range = ChartRange.Create(to.AddHours(-hours), to, Now);

        Assert.InRange(range.Buckets.Count(), 1, ChartRange.MaxBuckets);
        Assert.True(range.From <= Now.AddHours(-hours));
    }

    [Fact]
    public void TwoHoursFromHalfAMinute_UseFiveMinuteBuckets()
    {
        var from = new DateTimeOffset(2026, 11, 4, 10, 0, 30, TimeSpan.Zero);

        var range = ChartRange.Create(from, from.AddHours(2), Now);

        Assert.Equal(TimeSpan.FromMinutes(5), range.BucketWidth);
        Assert.Equal(new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc), range.From);
    }

    [Fact]
    public void TheLongestRange_UsesWeeks()
    {
        var range = ChartRange.Create(new DateTimeOffset(Now).Add(-ChartRange.MaxLength), new DateTimeOffset(Now), Now);

        Assert.Equal(TimeSpan.FromDays(7), range.BucketWidth);
    }

    [Theory]
    [InlineData(null, "0001-01-01T00:00:00Z")]
    [InlineData(null, "0001-01-05T00:00:00Z")]
    [InlineData("0001-01-01T00:00:00Z", "0001-01-02T00:00:00Z")]
    [InlineData("9999-12-30T00:00:00Z", "9999-12-31T00:00:00Z")]
    [InlineData(null, "9999-12-31T23:59:59Z")]
    public void ARangeAtTheEdgeOfTheCalendar_IsRejected(string? from, string to)
    {
        Assert.Throws<DomainValidationException>(() => ChartRange.Create(
            from is null ? null : DateTimeOffset.Parse(from, System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(to, System.Globalization.CultureInfo.InvariantCulture),
            Now));
    }
}
