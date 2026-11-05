// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Sdk;

/// <summary>
///     Decides how long the sender waits after a failed attempt. It is only used by the loop of the sender.
/// </summary>
/// <param name="nextRandom">Returns a random number from 0 up to 1, or null for the default.</param>
internal sealed class RetryBackoff(Func<double>? nextRandom = null)
{
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RefusedDelay = TimeSpan.FromMinutes(1);

    // A header that asks for more is more likely a mistake than a plan, and the buffer only keeps events for so long.
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(5);

    private readonly Func<double> _nextRandom = nextRandom ?? new Random().NextDouble;

    private int _failures;

    /// <summary>
    ///     Starts from the shortest delay again, after the server has answered.
    /// </summary>
    public void Reset()
    {
        _failures = 0;
    }

    /// <summary>
    ///     Gets the delay after a temporary failure, which doubles with every failure up to a maximum. It is between half
    ///     and all of that value at random, so that applications that lost the server together do not return together.
    /// </summary>
    /// <param name="retryAfter">The time the server asked to wait, which is respected up to a maximum.</param>
    /// <returns>The delay.</returns>
    public TimeSpan NextDelay(TimeSpan? retryAfter = null)
    {
        // The cap on the exponent only keeps the calculation in range, as the delay reaches its maximum long before.
        var exponent = Math.Min(_failures, 16);
        var upperLimit = TimeSpan.FromTicks(Math.Min(FirstDelay.Ticks << exponent, MaxDelay.Ticks));

        _failures++;

        var delay = TimeSpan.FromTicks((long)(upperLimit.Ticks * (0.5 + (_nextRandom() / 2))));

        if (retryAfter is not { } asked)
        {
            return delay;
        }

        var respected = asked < MaxRetryAfter ? asked : MaxRetryAfter;

        return respected > delay ? respected : delay;
    }

    /// <summary>
    ///     Gets the delay after the server has refused the request, e.g. for a wrong API Key: about a minute, as this needs
    ///     someone to change something.
    /// </summary>
    /// <returns>The delay.</returns>
    public TimeSpan NextRefusedDelay()
    {
        return TimeSpan.FromTicks((long)(RefusedDelay.Ticks * (0.9 + (_nextRandom() / 5))));
    }
}
