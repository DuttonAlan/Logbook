// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Microsoft.Extensions.Options;

namespace Logbook.Api.RateLimiting;

/// <summary>
///     Limits how many passwords one IP address can try.
/// </summary>
/// <remarks>
///     Behind a reverse proxy, this only tells clients apart if forwarded headers are turned on (setting
///     <c>ReverseProxy:Enabled</c>). Otherwise all of them share the IP address of the proxy.
/// </remarks>
/// <param name="options">The limit.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class InvalidSignInLimiter(IOptions<RateLimitOptions> options, TimeProvider timeProvider) : FailedAttemptLimiter(timeProvider)
{
    /// <inheritdoc/>
    protected override int PermitsPerMinute => options.Value.InvalidSignInPermitsPerMinute;
}
