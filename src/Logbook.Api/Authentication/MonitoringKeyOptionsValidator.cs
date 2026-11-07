// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Microsoft.Extensions.Options;

namespace Logbook.Api.Authentication;

/// <summary>
///     Validates the <see cref="MonitoringKeyOptions"/> at startup. The messages never contain the key.
/// </summary>
public sealed class MonitoringKeyOptionsValidator : IValidateOptions<MonitoringKeyOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, MonitoringKeyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var setting = $"{MonitoringKeyOptions.SectionName}:{nameof(MonitoringKeyOptions.MonitoringKey)}";

        if (string.IsNullOrEmpty(options.MonitoringKey))
        {
            return ValidateOptionsResult.Success;
        }

        if (options.MonitoringKey.Length < MonitoringKeyOptions.KeyMinLength)
        {
            return ValidateOptionsResult.Fail($"'{setting}' must be at least {MonitoringKeyOptions.KeyMinLength} characters long.");
        }

        // A stray line break from a file or a shell would otherwise end in a key that never matches.
        if (options.MonitoringKey.Any(char.IsWhiteSpace))
        {
            return ValidateOptionsResult.Fail($"'{setting}' must not contain whitespace.");
        }

        return ValidateOptionsResult.Success;
    }
}
