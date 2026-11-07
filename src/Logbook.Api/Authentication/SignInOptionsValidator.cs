// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Microsoft.Extensions.Options;

namespace Logbook.Api.Authentication;

/// <summary>
///     Validates the <see cref="SignInOptions"/> at startup. The messages never contain the password or the secret.
/// </summary>
/// <param name="environment">The environment, as only <c>Development</c> may use a provider without HTTPS.</param>
public sealed class SignInOptionsValidator(IHostEnvironment environment) : IValidateOptions<SignInOptions>
{
    private static readonly TimeSpan MaxSessionLifetime = TimeSpan.FromDays(30);

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, SignInOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.SessionLifetime <= TimeSpan.Zero || options.SessionLifetime > MaxSessionLifetime)
        {
            failures.Add($"'{SignInOptions.SectionName}:{nameof(options.SessionLifetime)}' must be greater than 0 and at most {MaxSessionLifetime.Days} days.");
        }

        ValidateAdministrator(options.Administrator, failures);
        ValidateOpenIdConnect(options.OpenIdConnect, failures);

        if (!options.Administrator.IsEnabled && !options.OpenIdConnect.IsEnabled && failures.Count == 0)
        {
            failures.Add(
                $"Nobody could sign in. Set '{SignInOptions.SectionName}:Administrator:Password', e.g. via the environment variable "
                + $"'{SignInOptions.SectionName}__Administrator__Password', or configure '{SignInOptions.SectionName}:OpenIdConnect'.");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }

    private static void ValidateAdministrator(LocalAdministratorOptions options, List<string> failures)
    {
        var section = $"{SignInOptions.SectionName}:Administrator";

        if (string.IsNullOrWhiteSpace(options.UserName))
        {
            failures.Add($"'{section}:{nameof(options.UserName)}' must not be empty.");
        }

        if (!options.IsEnabled)
        {
            return;
        }

        if (options.Password.Length < LocalAdministratorOptions.PasswordMinLength)
        {
            failures.Add($"'{section}:{nameof(options.Password)}' must be at least {LocalAdministratorOptions.PasswordMinLength} characters long.");
        }

        // A stray line break from a file or a shell would otherwise end in a password that never matches.
        if (options.Password != options.Password.Trim())
        {
            failures.Add($"'{section}:{nameof(options.Password)}' must not start or end with whitespace.");
        }
    }

    private void ValidateOpenIdConnect(OpenIdConnectSignInOptions options, List<string> failures)
    {
        var section = $"{SignInOptions.SectionName}:OpenIdConnect";

        var isConfigured = options.IsEnabled
            || !string.IsNullOrEmpty(options.ClientId)
            || !string.IsNullOrEmpty(options.ClientSecret)
            || !string.IsNullOrEmpty(options.RequiredClaimType)
            || !string.IsNullOrEmpty(options.RequiredClaimValue);

        if (!isConfigured)
        {
            return;
        }

        var allowsHttp = environment.IsDevelopment();

        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out var authority)
            || !(authority.Scheme == Uri.UriSchemeHttps || (allowsHttp && authority.Scheme == Uri.UriSchemeHttp)))
        {
            failures.Add($"'{section}:{nameof(options.Authority)}' must be an absolute HTTPS address.");
        }

        foreach (var (setting, value) in new[]
        {
            (nameof(options.ClientId), options.ClientId),
            (nameof(options.RequiredClaimType), options.RequiredClaimType),
            (nameof(options.RequiredClaimValue), options.RequiredClaimValue),
            (nameof(options.DisplayName), options.DisplayName)
        })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                failures.Add($"'{section}:{setting}' must be set, if '{section}' is used.");
            }
        }
    }
}
