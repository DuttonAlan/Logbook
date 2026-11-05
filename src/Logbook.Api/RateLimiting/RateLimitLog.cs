// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Api.RateLimiting;

/// <summary>
///     Log messages of the rate limits. API Keys are never logged, only their Ids.
/// </summary>
internal static partial class RateLimitLog
{
    // Debug, because it happens on every rejected request, exactly when a sender is already too busy.
    [LoggerMessage(EventId = 2201, Level = LogLevel.Debug, Message = "A request of API Key {ApiKeyId} was rejected by the rate limit.")]
    public static partial void RequestRejected(this ILogger logger, string? apiKeyId);
}
