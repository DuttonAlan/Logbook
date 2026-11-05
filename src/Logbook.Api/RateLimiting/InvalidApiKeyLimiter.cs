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
///     A token is reserved before the lookup, as parallel requests would otherwise all pass while the bucket still has a
///     token, and is given back unless the API Key turns out not to be valid. So only failures use up the limit in the
///     end, and lookups in progress count against it. An API Key cannot be checked without the lookup, so while an IP
///     address is over the limit, all its requests with an API Key are rejected. Behind a reverse proxy all clients share
///     the IP address of the proxy until forwarded headers exist (milestone M4), which the generous default allows for.
///     <para>
///         This is not built on <c>System.Threading.RateLimiting</c>, because a token must be given back, which a token
///         bucket lease does not do.
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
    ///     Reserves a token for a lookup. Keep it if the API Key is not valid, otherwise call <see cref="Release"/>.
    /// </summary>
    /// <param name="ipAddress">The IP address of the client.</param>
    /// <param name="retryAfter">How long the client should wait, if it is over the limit.</param>
    /// <returns><see langword="true"/> if the lookup may start, or <see langword="false"/> if the IP address is over the limit.</returns>
    public bool TryReserve(string ipAddress, out TimeSpan retryAfter)
    {
        if (_buckets.Count >= MaxTracked)
        {
            _buckets.Clear();
        }

        var permitsPerMinute = options.Value.InvalidApiKeyPermitsPerMinute;
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
    ///     Gives a reserved token back, because the API Key is valid or could not be checked.
    /// </summary>
    /// <param name="ipAddress">The IP address of the client.</param>
    public void Release(string ipAddress)
    {
        if (!_buckets.TryGetValue(ipAddress, out var bucket))
        {
            return;
        }

        var permitsPerMinute = options.Value.InvalidApiKeyPermitsPerMinute;

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
