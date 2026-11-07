// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Microsoft.Extensions.Options;

namespace Logbook.Core.Application.LogEvents;

/// <summary>
///     Validates the <see cref="RetentionOptions"/> at startup.
/// </summary>
public sealed class RetentionOptionsValidator : IValidateOptions<RetentionOptions>
{
    private const int MinDays = Domain.Entities.Application.MinRetentionDays;
    private const int MaxDays = Domain.Entities.Application.MaxRetentionDays;

    // The timer of the background service cannot wait much longer than this, and would stop the server.
    private static readonly TimeSpan MaxInterval = TimeSpan.FromDays(30);

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, RetentionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var section = RetentionOptions.SectionName;
        var failures = new List<string>();

        if (options.DefaultDays is < MinDays or > MaxDays)
        {
            failures.Add($"'{section}:{nameof(options.DefaultDays)}' must be between {MinDays} and {MaxDays}.");
        }

        if (options.Interval <= TimeSpan.Zero || options.Interval > MaxInterval)
        {
            failures.Add($"'{section}:{nameof(options.Interval)}' must be greater than 0 and at most {MaxInterval.Days} days.");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
