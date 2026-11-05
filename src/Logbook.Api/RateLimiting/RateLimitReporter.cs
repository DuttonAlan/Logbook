// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Collections.Concurrent;

namespace Logbook.Api.RateLimiting;

/// <summary>
///     Makes rejections by a rate limit visible to operators, at most once per minute for each API Key or IP address.
/// </summary>
/// <remarks>
///     Logging every rejection would write the most exactly when a sender is already too busy. A warning per minute says
///     what an operator needs to know, without a metrics system.
/// </remarks>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The Logger for the warnings.</param>
public sealed class RateLimitReporter(TimeProvider timeProvider, ILogger<RateLimitReporter> logger)
{
    private const int MaxTracked = 10_000;

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastReported = new();

    /// <summary>
    ///     Reports a request of an API Key that was rejected.
    /// </summary>
    /// <param name="apiKeyId">Id of the API Key, never the API Key itself.</param>
    public void ApiKeyOverLimit(string apiKeyId)
    {
        if (IsDue($"key:{apiKeyId}"))
        {
            logger.ApiKeyOverLimit(apiKeyId);
        }
    }

    /// <summary>
    ///     Reports a request that was rejected, because its IP address tried too many API Keys that are not valid.
    /// </summary>
    /// <param name="ipAddress">The IP address of the client.</param>
    public void TooManyInvalidApiKeys(string ipAddress)
    {
        if (IsDue($"ip:{ipAddress}"))
        {
            logger.TooManyInvalidApiKeys(ipAddress);
        }
    }

    private bool IsDue(string key)
    {
        var now = timeProvider.GetUtcNow();

        // Bounded, because IP addresses are chosen by the client. Forgetting them only means reporting one again early.
        if (_lastReported.Count >= MaxTracked)
        {
            _lastReported.Clear();
        }

        var isDue = true;

        _lastReported.AddOrUpdate(
            key,
            now,
            (_, last) =>
            {
                isDue = now - last >= Interval;

                return isDue ? now : last;
            });

        return isDue;
    }
}
