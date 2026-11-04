// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Api.Authentication;

/// <summary>
///     The settings of the admin API.
/// </summary>
public sealed class AdminApiOptions
{
    /// <summary>
    ///     Name of the configuration section.
    /// </summary>
    public const string SectionName = "AdminApi";

    /// <summary>
    ///     Minimum length of <see cref="Key"/>.
    /// </summary>
    public const int KeyMinLength = 32;

    /// <summary>
    ///     The key that grants full access to the admin API. Required: Logbook does not start without it.
    /// </summary>
    public string Key { get; set; } = string.Empty;
}
