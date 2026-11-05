// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Sdk.Tests;

/// <summary>
///     Checks the waiting times after failed attempts.
/// </summary>
public sealed class RetryBackoffTests
{
    private const double Highest = 1;
    private const double Lowest = 0;

    [Fact]
    public void NextDelay_DoublesUpToTheMaximum()
    {
        var backoff = new RetryBackoff(() => Highest);

        var delays = Enumerable.Range(0, 8).Select(_ => backoff.NextDelay().TotalSeconds);

        Assert.Equal([1, 2, 4, 8, 16, 30, 30, 30], delays);
    }

    [Fact]
    public void NextDelay_ManyFailures_StaysAtTheMaximum()
    {
        var backoff = new RetryBackoff(() => Highest);

        for (var failure = 0; failure < 1000; failure++)
        {
            backoff.NextDelay();
        }

        Assert.Equal(TimeSpan.FromSeconds(30), backoff.NextDelay());
    }

    [Fact]
    public void NextDelay_IsAtLeastHalfOfTheValue_ForTheJitter()
    {
        var backoff = new RetryBackoff(() => Lowest);

        Assert.Equal(TimeSpan.FromSeconds(0.5), backoff.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(1), backoff.NextDelay());
    }

    [Fact]
    public void Reset_StartsFromTheShortestDelay()
    {
        var backoff = new RetryBackoff(() => Highest);
        backoff.NextDelay();
        backoff.NextDelay();

        backoff.Reset();

        Assert.Equal(TimeSpan.FromSeconds(1), backoff.NextDelay());
    }

    [Fact]
    public void NextDelay_WaitsForAtLeastWhatTheServerAskedFor()
    {
        var backoff = new RetryBackoff(() => Highest);

        Assert.Equal(TimeSpan.FromSeconds(20), backoff.NextDelay(TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void NextDelay_AShortRetryAfter_DoesNotShortenTheBackoff()
    {
        var backoff = new RetryBackoff(() => Highest);
        backoff.NextDelay();
        backoff.NextDelay();

        Assert.Equal(TimeSpan.FromSeconds(4), backoff.NextDelay(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void NextDelay_AnAbsurdRetryAfter_IsLimited()
    {
        var backoff = new RetryBackoff(() => Highest);

        Assert.Equal(TimeSpan.FromMinutes(5), backoff.NextDelay(TimeSpan.FromDays(365)));
    }

    [Fact]
    public void NextDelay_ARetryAfterInThePast_IsIgnored()
    {
        var backoff = new RetryBackoff(() => Highest);

        Assert.Equal(TimeSpan.FromSeconds(1), backoff.NextDelay(TimeSpan.FromSeconds(-30)));
    }

    [Theory]
    [InlineData(Lowest, 54)]
    [InlineData(Highest, 66)]
    public void NextRefusedDelay_IsAboutAMinute(double random, double expectedSeconds)
    {
        var backoff = new RetryBackoff(() => random);

        Assert.Equal(expectedSeconds, backoff.NextRefusedDelay().TotalSeconds, 6);
    }

    [Fact]
    public void NextRefusedDelay_DoesNotGrow()
    {
        var backoff = new RetryBackoff(() => Highest);
        var first = backoff.NextRefusedDelay();

        backoff.NextDelay();

        Assert.Equal(first, backoff.NextRefusedDelay());
    }

    [Fact]
    public void TheDefaultRandomNumbers_StayInTheExpectedRange()
    {
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var delay = new RetryBackoff().NextDelay();

            Assert.InRange(delay, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1));
        }
    }
}
