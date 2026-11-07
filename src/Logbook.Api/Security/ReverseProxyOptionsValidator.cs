// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net;

using Microsoft.Extensions.Options;

namespace Logbook.Api.Security;

/// <summary>
///     Validates the <see cref="ReverseProxyOptions"/> at startup.
/// </summary>
public sealed class ReverseProxyOptionsValidator : IValidateOptions<ReverseProxyOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, ReverseProxyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var section = ReverseProxyOptions.SectionName;
        var failures = new List<string>();

        failures.AddRange(options.KnownProxies
            .Where(proxy => !IPAddress.TryParse(proxy, out _))
            .Select(proxy => $"'{section}:{nameof(options.KnownProxies)}' contains '{proxy}', which is not an IP address."));

        failures.AddRange(options.KnownNetworks
            .Where(network => !IPNetwork.TryParse(network, out _))
            .Select(network => $"'{section}:{nameof(options.KnownNetworks)}' contains '{network}', which is not a network in CIDR notation."));

        if (options.ForwardLimit < 1)
        {
            failures.Add($"'{section}:{nameof(options.ForwardLimit)}' must be at least 1.");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
