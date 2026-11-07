// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Api.RateLimiting;

/// <summary>
///     Settings of the rate limits, bound from the section <see cref="SectionName"/>.
/// </summary>
public sealed class RateLimitOptions
{
    /// <summary>
    ///     Name of the configuration section.
    /// </summary>
    public const string SectionName = "RateLimiting";

    /// <summary>
    ///     Largest allowed value of each limit.
    /// </summary>
    public const int MaxPermitsPerMinute = 1_000_000;

    /// <summary>
    ///     Requests per minute to send Log Events, per API Key and server. A burst of up to this number is allowed if the
    ///     API Key was not used for a while. Defaults to 6000, about 10,000 Log Events per second with batches of 100.
    /// </summary>
    public int ApiKeyPermitsPerMinute { get; set; } = 6000;

    /// <summary>
    ///     Requests per minute with an API Key that is not valid, per IP address and server. Each one costs a lookup in the
    ///     database. Above this, all requests with an API Key from that IP address are rejected. Defaults to 600, far above
    ///     what a misconfigured application sends.
    /// </summary>
    public int InvalidApiKeyPermitsPerMinute { get; set; } = 600;

    /// <summary>
    ///     Failed sign-ins per minute, per IP address and server. Above this, all sign-ins from that IP address are rejected.
    ///     Defaults to 10, enough for someone who mistypes, and too few to guess a password.
    /// </summary>
    public int InvalidSignInPermitsPerMinute { get; set; } = 10;
}
