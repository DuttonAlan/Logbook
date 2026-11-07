// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Collections.Concurrent;

namespace Logbook.Api.RateLimiting;

/// <summary>
///     Limits how many failed attempts one IP address can make per minute.
/// </summary>
/// <remarks>
///     A token is reserved before an attempt, as parallel requests would otherwise all pass while the bucket still has a
///     token, and is given back unless the attempt turns out to have failed. So only failures use up the limit in the end,
///     and attempts in progress count against it. Whether an attempt fails is only known after trying it, so while an IP
///     address is over the limit, all its attempts are rejected.
///     <para>
///         This is not built on <c>System.Threading.RateLimiting</c>, because a token must be given back, which a token
///         bucket lease does not do.
///     </para>
/// </remarks>
/// <param name="timeProvider">The clock.</param>
public abstract class FailedAttemptLimiter(TimeProvider timeProvider)
{
    // Bounded, because the addresses come from outside. Forgetting them only means that they start with a full bucket.
    private const int MaxTracked = 100_000;

    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();

    /// <summary>
    ///     The failed attempts allowed per minute.
    /// </summary>
    protected abstract int PermitsPerMinute { get; }

    /// <summary>
    ///     Reserves a token for an attempt. Keep it if the attempt failed, otherwise call <see cref="Release"/>.
    /// </summary>
    /// <param name="ipAddress">The IP address of the client.</param>
    /// <param name="retryAfter">How long the client should wait, if it is over the limit.</param>
    /// <returns><see langword="true"/> if the attempt may start, or <see langword="false"/> if the IP address is over the limit.</returns>
    public bool TryReserve(string ipAddress, out TimeSpan retryAfter)
    {
        if (_buckets.Count >= MaxTracked)
        {
            _buckets.Clear();
        }

        var permitsPerMinute = PermitsPerMinute;
        var bucket = _buckets.GetOrAdd(ipAddress, _ => new Bucket { Tokens = permitsPerMinute, UpdatedAt = timeProvider.GetUtcNow() });

        retryAfter = TimeSpan.Zero;

        lock (bucket)
        {
            Refill(bucket, permitsPerMinute);

            if (bucket.Tokens < 1)
            {
                retryAfter = TimeSpan.FromMinutes((1 - bucket.Tokens) / permitsPerMinute);

                return false;
            }

            bucket.Tokens--;

            return true;
        }
    }

    /// <summary>
    ///     Gives a reserved token back, because the attempt succeeded or could not be finished.
    /// </summary>
    /// <param name="ipAddress">The IP address of the client.</param>
    public void Release(string ipAddress)
    {
        if (!_buckets.TryGetValue(ipAddress, out var bucket))
        {
            return;
        }

        var permitsPerMinute = PermitsPerMinute;

        lock (bucket)
        {
            bucket.Tokens = Math.Min(permitsPerMinute, bucket.Tokens + 1);
        }
    }

    private void Refill(Bucket bucket, int permitsPerMinute)
    {
        var now = timeProvider.GetUtcNow();

        bucket.Tokens = Math.Min(permitsPerMinute, bucket.Tokens + ((now - bucket.UpdatedAt).TotalMinutes * permitsPerMinute));
        bucket.UpdatedAt = now;
    }

    private sealed class Bucket
    {
        public double Tokens { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }
}
