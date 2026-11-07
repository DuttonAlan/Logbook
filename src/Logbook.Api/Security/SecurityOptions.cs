// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Api.Security;

/// <summary>
///     The settings for the checks of the production settings, bound from the section <see cref="SectionName"/>.
/// </summary>
public sealed class SecurityOptions
{
    /// <summary>
    ///     Name of the configuration section.
    /// </summary>
    public const string SectionName = "Security";

    /// <summary>
    ///     Allows serving a host other than this computer over plain HTTP, which sends passwords and logs in the clear. Only
    ///     for a network where that is acceptable.
    /// </summary>
    public bool AllowInsecureHttp { get; set; }
}
