// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Microsoft.Extensions.Options;

namespace Logbook.Api.Authentication;

/// <summary>
///     Validates the <see cref="AdminApiOptions"/> at startup. The messages never contain the key.
/// </summary>
public sealed class AdminApiOptionsValidator : IValidateOptions<AdminApiOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, AdminApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var setting = $"{AdminApiOptions.SectionName}:{nameof(AdminApiOptions.Key)}";

        if (string.IsNullOrEmpty(options.Key))
        {
            return ValidateOptionsResult.Fail(
                $"'{setting}' is not configured. Set it, e.g. via the environment variable 'AdminApi__Key'.");
        }

        if (options.Key.Length < AdminApiOptions.KeyMinLength)
        {
            return ValidateOptionsResult.Fail($"'{setting}' must be at least {AdminApiOptions.KeyMinLength} characters long.");
        }

        // A stray line break from a file or a shell would otherwise end in a key that never matches.
        if (options.Key.Any(char.IsWhiteSpace))
        {
            return ValidateOptionsResult.Fail($"'{setting}' must not contain whitespace.");
        }

        return ValidateOptionsResult.Success;
    }
}
