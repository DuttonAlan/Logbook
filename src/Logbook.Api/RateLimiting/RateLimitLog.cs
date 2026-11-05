// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Api.RateLimiting;

/// <summary>
///     Log messages of the rate limits. API Keys are never logged, only their Ids.
/// </summary>
internal static partial class RateLimitLog
{
    [LoggerMessage(EventId = 2201, Level = LogLevel.Warning, Message = "API Key {ApiKeyId} is over its rate limit, so its requests are rejected. Reported at most once per minute.")]
    public static partial void ApiKeyOverLimit(this ILogger logger, string apiKeyId);

    [LoggerMessage(EventId = 2202, Level = LogLevel.Warning, Message = "IP address {IpAddress} tried too many API Keys that are not valid, so its requests with an API Key are rejected. Reported at most once per minute.")]
    public static partial void TooManyInvalidApiKeys(this ILogger logger, string ipAddress);
}
