// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.LogEvents;

/// <summary>
///     The settings of the retention of Log Events.
/// </summary>
public sealed class RetentionOptions
{
    /// <summary>
    ///     Name of the configuration section.
    /// </summary>
    public const string SectionName = "Retention";

    /// <summary>
    ///     Days to keep the Log Events of an Application that has no retention of its own.
    /// </summary>
    public int DefaultDays { get; set; } = 30;

    /// <summary>
    ///     Time between two retention runs. The first run starts with the server.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
}
