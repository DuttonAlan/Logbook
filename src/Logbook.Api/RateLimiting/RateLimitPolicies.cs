// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;

using Logbook.Api.Authentication;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Logbook.Api.RateLimiting;

/// <summary>
///     Rate limiting policies of Logbook, with the limits of <see cref="RateLimitOptions"/>.
/// </summary>
internal static class RateLimitPolicies
{
    /// <summary>
    ///     Limits the requests of each API Key, so one Application cannot take the capacity of the others.
    /// </summary>
    /// <remarks>
    ///     The middleware must run after authorization: the API Key is authenticated by the authorization policy, so
    ///     the claim does not exist earlier. A request without the claim fails instead of going unlimited.
    /// </remarks>
    public const string ApiKey = "api-key";

    /// <summary>
    ///     Registers the policies.
    /// </summary>
    /// <param name="options">The rate limiter options.</param>
    public static void AddLogbookPolicies(this RateLimiterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = WriteRejectionAsync;

        options.AddPolicy(ApiKey, context =>
        {
            var permitsPerMinute = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value.ApiKeyPermitsPerMinute;

            return RateLimitPartition.GetTokenBucketLimiter(
                context.User.FindFirstValue(ApiKeyClaimTypes.ApiKeyId)!,
                _ => CreateBucket(permitsPerMinute));
        });
    }

    /// <summary>
    ///     A bucket refills continuously, unlike a fixed window. After a burst, an Application can go on sending at the
    ///     allowed rate, instead of losing its logs until the next window starts.
    /// </summary>
    private static TokenBucketRateLimiterOptions CreateBucket(int permitsPerMinute)
    {
        // Refills at least every second. A limit below 60 per minute cannot refill a whole token that often.
        var period = TimeSpan.FromSeconds(Math.Max(1, 60.0 / permitsPerMinute));

        return new TokenBucketRateLimiterOptions
        {
            TokenLimit = permitsPerMinute,
            ReplenishmentPeriod = period,
            TokensPerPeriod = (int)Math.Ceiling(permitsPerMinute * period.TotalMinutes),
            QueueLimit = 0
        };
    }

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;

        httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(RateLimitPolicies).FullName!)
            .RequestRejected(httpContext.User.FindFirstValue(ApiKeyClaimTypes.ApiKeyId));

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            httpContext.Response.Headers[HeaderNames.RetryAfter] =
                Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        await httpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Detail = "This API Key sends too many requests. Send them again later."
            }
        });
    }
}
