// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Api.Security;

/// <summary>
///     The settings for running behind a reverse proxy, bound from the section <see cref="SectionName"/>.
/// </summary>
public sealed class ReverseProxyOptions
{
    /// <summary>
    ///     Name of the configuration section.
    /// </summary>
    public const string SectionName = "ReverseProxy";

    /// <summary>
    ///     Whether to read the client address and the protocol from <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c>.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    ///     IP addresses of the proxies to trust. If this and <see cref="KnownNetworks"/> are empty, only a proxy on the same
    ///     host is trusted.
    /// </summary>
    public IReadOnlyList<string> KnownProxies { get; set; } = [];

    /// <summary>
    ///     Networks of the proxies to trust, in CIDR notation, e.g. <c>172.18.0.0/16</c>.
    /// </summary>
    public IReadOnlyList<string> KnownNetworks { get; set; } = [];

    /// <summary>
    ///     Number of proxies in a row, each of which adds to the headers.
    /// </summary>
    public int ForwardLimit { get; set; } = 1;
}
