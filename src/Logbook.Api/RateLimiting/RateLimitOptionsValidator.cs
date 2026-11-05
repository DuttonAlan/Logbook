// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Microsoft.Extensions.Options;

namespace Logbook.Api.RateLimiting;

/// <summary>
///     Validates the <see cref="RateLimitOptions"/> at startup.
/// </summary>
public sealed class RateLimitOptionsValidator : IValidateOptions<RateLimitOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, RateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.ApiKeyPermitsPerMinute is < 1 or > RateLimitOptions.MaxPermitsPerMinute)
        {
            failures.Add($"'{RateLimitOptions.SectionName}:{nameof(RateLimitOptions.ApiKeyPermitsPerMinute)}' must be between 1 and {RateLimitOptions.MaxPermitsPerMinute}.");
        }

        if (options.InvalidApiKeyPermitsPerMinute is < 1 or > RateLimitOptions.MaxPermitsPerMinute)
        {
            failures.Add($"'{RateLimitOptions.SectionName}:{nameof(RateLimitOptions.InvalidApiKeyPermitsPerMinute)}' must be between 1 and {RateLimitOptions.MaxPermitsPerMinute}.");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
