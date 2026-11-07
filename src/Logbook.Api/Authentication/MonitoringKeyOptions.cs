// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Api.Authentication;

/// <summary>
///     The settings of the access of monitoring tools to the detailed health report.
/// </summary>
public sealed class MonitoringKeyOptions
{
    /// <summary>
    ///     Name of the configuration section.
    /// </summary>
    public const string SectionName = "HealthDetails";

    /// <summary>
    ///     Minimum length of <see cref="MonitoringKey"/>.
    /// </summary>
    public const int KeyMinLength = 32;

    /// <summary>
    ///     The key that gives access to <c>/health/details</c> and nothing else. Without it, only a signed-in user has access.
    /// </summary>
    public string MonitoringKey { get; set; } = string.Empty;
}
