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

    private static bool IsHttps(string address)
    {
        return address.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoopback(string address)
    {
        // The host of an address such as http://+:8080 is no valid URI host, so it is read by hand.
        var authority = address[(address.IndexOf("://", StringComparison.Ordinal) + "://".Length)..].Split('/')[0];
        var host = authority.StartsWith('[') ? authority[..(authority.IndexOf(']', StringComparison.Ordinal) + 1)] : authority.Split(':')[0];

        return LoopbackHosts.Contains(host, StringComparer.OrdinalIgnoreCase);
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

        if (isServedToOthers && !options.AllowInsecureHttp && !configuration.GetValue<bool>($"{ReverseProxyOptions.SectionName}:Enabled") && !ServesOnlyHttps())
        {
            failures.Add(
                "Logbook would send passwords and logs in the clear, as it serves other hosts than this computer over plain HTTP. "
                + $"Put it behind a reverse proxy with HTTPS and set '{ReverseProxyOptions.SectionName}:Enabled', "
                + "or listen only on HTTPS. The container image also listens on HTTP through ASPNETCORE_HTTP_PORTS, unless "
                + "ASPNETCORE_URLS replaces it or it is set to empty. Where plain HTTP is acceptable, set "
                + $"'{SecurityOptions.SectionName}:{nameof(SecurityOptions.AllowInsecureHttp)}'.");
        }
    }

    /// <summary>
    ///     Whether there is an HTTPS listener, and every other one can only be reached from this computer. One listener on
    ///     plain HTTP is enough to send passwords and logs in the clear.
    /// </summary>
    private bool ServesOnlyHttps()
    {
        var addresses = GetListenAddresses();

        return addresses.Any(IsHttps) && addresses.All(address => IsHttps(address) || IsLoopback(address));
    }

    /// <summary>
    ///     The addresses Kestrel listens on. Each kind of setting replaces the next ones, rather than adding to them.
    /// </summary>
    private List<string> GetListenAddresses()
    {
        var endpoints = configuration.GetSection("Kestrel:Endpoints").GetChildren()
            .Select(endpoint => endpoint["Url"])
            .OfType<string>()
            .ToList();

        if (endpoints.Count > 0)
        {
            return endpoints;
        }

        var urls = configuration["urls"];

        if (!string.IsNullOrWhiteSpace(urls))
        {
            return [.. urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        }

        return
        [
            .. Ports("http_ports").Select(port => $"http://*:{port}"),
            .. Ports("https_ports").Select(port => $"https://*:{port}")
        ];
    }

    private string[] Ports(string setting)
    {
        return (configuration[setting] ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
