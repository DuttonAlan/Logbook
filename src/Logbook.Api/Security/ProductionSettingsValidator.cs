// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net;

using Logbook.Api.Authentication;

using Microsoft.Extensions.Options;

using Npgsql;

namespace Logbook.Api.Security;

/// <summary>
///     Refuses to start in the environment <c>Production</c> with settings that are insecure. The messages never contain
///     a secret.
/// </summary>
/// <remarks>
///     Other environments are not checked: the settings for development would fail, on purpose.
/// </remarks>
/// <param name="environment">The environment.</param>
/// <param name="configuration">The settings to check, which come from several sections.</param>
public sealed class ProductionSettingsValidator(IHostEnvironment environment, IConfiguration configuration) : IValidateOptions<SecurityOptions>
{
    /// <summary>
    ///     The password of the local administrator in <c>appsettings.Development.json</c>.
    /// </summary>
    public const string DevelopmentPassword = "development-only-password-do-not-deploy";

    private static readonly string[] DefaultDatabasePasswords = ["12345", "password", "postgres"];

    private static readonly string[] LoopbackHosts = ["localhost", "127.0.0.1", "[::1]"];

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, SecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!environment.IsProduction())
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        CheckHosts(options, failures);
        CheckSecrets(failures);
        CheckProxies(failures);

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }

    private static bool IsOpenToEveryone(string network)
    {
        return IPNetwork.TryParse(network, out var parsed) && parsed.PrefixLength == 0;
    }

    private void CheckHosts(SecurityOptions options, List<string> failures)
    {
        var hosts = (configuration["AllowedHosts"] ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (hosts.Length == 0 || hosts.Contains("*"))
        {
            failures.Add("'AllowedHosts' must name the host names of this server instead of allowing all of them, e.g. 'logbook.example.com'.");

            return;
        }

        var isServedToOthers = hosts.Any(host => !LoopbackHosts.Contains(host, StringComparer.OrdinalIgnoreCase));

        if (isServedToOthers && !options.AllowInsecureHttp && !configuration.GetValue<bool>($"{ReverseProxyOptions.SectionName}:Enabled") && !HasHttpsEndpoint())
        {
            failures.Add(
                "Logbook would send passwords and logs in the clear, as it serves other hosts than this computer over plain HTTP. "
                + $"Put it behind a reverse proxy with HTTPS and set '{ReverseProxyOptions.SectionName}:Enabled', "
                + "or configure an HTTPS endpoint. Where plain HTTP is acceptable, set "
                + $"'{SecurityOptions.SectionName}:{nameof(SecurityOptions.AllowInsecureHttp)}'.");
        }
    }

    private bool HasHttpsEndpoint()
    {
        var addresses = new[] { configuration["urls"], configuration["https_ports"] is { Length: > 0 } ? "https://" : null }
            .Concat(configuration.GetSection("Kestrel:Endpoints").GetChildren().Select(endpoint => endpoint["Url"]));

        return addresses.Any(address => address?.Contains("https://", StringComparison.OrdinalIgnoreCase) == true);
    }

    private void CheckSecrets(List<string> failures)
    {
        if (configuration[$"{SignInOptions.SectionName}:Administrator:Password"] == DevelopmentPassword)
        {
            failures.Add($"'{SignInOptions.SectionName}:Administrator:Password' still has the value from the settings for development.");
        }

        var connectionString = configuration.GetConnectionString("Database");

        if (string.IsNullOrEmpty(connectionString))
        {
            return;
        }

        var databasePassword = new NpgsqlConnectionStringBuilder(connectionString).Password;

        if (DefaultDatabasePasswords.Contains(databasePassword, StringComparer.OrdinalIgnoreCase))
        {
            failures.Add("The password in 'ConnectionStrings:Database' is a well-known default. Choose another one.");
        }
    }

    private void CheckProxies(List<string> failures)
    {
        var networks = configuration.GetSection($"{ReverseProxyOptions.SectionName}:KnownNetworks").Get<string[]>() ?? [];

        if (networks.Any(IsOpenToEveryone))
        {
            failures.Add(
                $"'{ReverseProxyOptions.SectionName}:KnownNetworks' trusts every address, so that any client could claim to be "
                + "any other, and to use HTTPS. Name the network of the proxy instead.");
        }
    }
}
