// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Collections.Concurrent;

using Microsoft.Extensions.Options;

namespace Logbook.Api.RateLimiting;

/// <summary>
///     Limits how many API Keys that are not valid one IP address can try, as each one costs a lookup in the database.
/// </summary>
/// <remarks>
///     Only failures count, so valid API Keys use up nothing. An API Key cannot be checked without the lookup, so while
///     an IP address is over the limit, all its requests with an API Key are rejected. Behind a reverse proxy all clients
///     share the IP address of the proxy until forwarded headers exist (milestone M4), which the generous default allows for.
///     <para>
///         This is not built on <c>System.Threading.RateLimiting</c>, because checking must not use up anything. The only
///         way to look without taking is to acquire 0 permits, which succeeds as soon as a bucket holds any fraction of
///         a token, so right after it refills by a tiny amount, and the limit would hardly ever apply.
///     </para>
/// </remarks>
/// <param name="options">The limit.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class InvalidApiKeyLimiter(IOptions<RateLimitOptions> options, TimeProvider timeProvider)
{
    // Bounded, because the addresses come from outside. Forgetting them only means that they start with a full bucket.
    private const int MaxTracked = 100_000;

    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();

    /// <summary>
    ///     Checks whether an IP address is over the limit, without counting this check.
    /// </summary>
    /// <param name="ipAddress">The IP address of the client.</param>
    /// <param name="retryAfter">How long the client should wait, if it is over the limit.</param>
    /// <returns><see langword="true"/> if requests from the IP address must be rejected.</returns>
    public bool IsOverLimit(string ipAddress, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;

        if (!_buckets.TryGetValue(ipAddress, out var bucket))
        {
            return false;
        }

        var permitsPerMinute = options.Value.InvalidApiKeyPermitsPerMinute;

        lock (bucket)
        {
            Refill(bucket, permitsPerMinute);

            if (bucket.Tokens >= 1)
            {
                return false;
            }

            retryAfter = TimeSpan.FromMinutes((1 - bucket.Tokens) / permitsPerMinute);

            return true;
        }
    }

    /// <summary>
    ///     Counts a request with an API Key that is not valid.
    /// </summary>
    /// <param name="ipAddress">The IP address of the client.</param>
    public void RecordFailure(string ipAddress)
    {
        if (_buckets.Count >= MaxTracked)
        {
            _buckets.Clear();
        }

        var permitsPerMinute = options.Value.InvalidApiKeyPermitsPerMinute;
        var bucket = _buckets.GetOrAdd(ipAddress, _ => new Bucket { Tokens = permitsPerMinute, UpdatedAt = timeProvider.GetUtcNow() });

        lock (bucket)
        {
            Refill(bucket, permitsPerMinute);

            bucket.Tokens = Math.Max(0, bucket.Tokens - 1);
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
