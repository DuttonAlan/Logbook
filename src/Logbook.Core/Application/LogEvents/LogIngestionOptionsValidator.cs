// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Microsoft.Extensions.Options;

namespace Logbook.Core.Application.LogEvents;

/// <summary>
///     Validates the <see cref="LogIngestionOptions"/> at startup.
/// </summary>
public sealed class LogIngestionOptionsValidator : IValidateOptions<LogIngestionOptions>
{
    // Each day in the accepted time window can become a partition, i.e. a table with its indexes.
    private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(3650);

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, LogIngestionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var section = LogIngestionOptions.SectionName;
        var failures = new List<string>();

        if (options.MaxRequestBytes <= 0)
        {
            failures.Add($"'{section}:{nameof(options.MaxRequestBytes)}' must be greater than 0.");
        }

        if (options.MaxEventBytes <= 0 || options.MaxEventBytes > options.MaxRequestBytes)
        {
            failures.Add($"'{section}:{nameof(options.MaxEventBytes)}' must be greater than 0 and not greater than '{nameof(options.MaxRequestBytes)}'.");
        }

        if (options.MaxEventAge <= TimeSpan.Zero || options.MaxEventAge > MaxWindow)
        {
            failures.Add($"'{section}:{nameof(options.MaxEventAge)}' must be greater than 0 and at most {MaxWindow.Days} days.");
        }

        if (options.MaxFutureOffset < TimeSpan.Zero || options.MaxFutureOffset > MaxWindow)
        {
            failures.Add($"'{section}:{nameof(options.MaxFutureOffset)}' must not be negative and at most {MaxWindow.Days} days.");
        }

        if (options.MaxQueuedBytes <= 0)
        {
            failures.Add($"'{section}:{nameof(options.MaxQueuedBytes)}' must be greater than 0.");
        }

        if (options.WriteBatchSize <= 0)
        {
            failures.Add($"'{section}:{nameof(options.WriteBatchSize)}' must be greater than 0.");
        }

        if (options.WriteInterval <= TimeSpan.Zero)
        {
            failures.Add($"'{section}:{nameof(options.WriteInterval)}' must be greater than 0.");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
