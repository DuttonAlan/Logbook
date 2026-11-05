// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.LogEvents;

/// <summary>
///     The limits for received Log Events.
/// </summary>
public sealed class LogIngestionOptions
{
    /// <summary>
    ///     Name of the configuration section.
    /// </summary>
    public const string SectionName = "LogIngestion";

    /// <summary>
    ///     Maximum size of a request body in bytes. For a compressed request, it applies after decompression as well.
    /// </summary>
    public long MaxRequestBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    ///     Maximum size in bytes of the message, message template, exception and properties of one Log Event together.
    /// </summary>
    public long MaxEventBytes { get; set; } = 256 * 1024;

    /// <summary>
    ///     How far in the past the timestamp of a Log Event may be.
    /// </summary>
    public TimeSpan MaxEventAge { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    ///     How far in the future the timestamp of a Log Event may be, to tolerate wrong clocks.
    /// </summary>
    public TimeSpan MaxFutureOffset { get; set; } = TimeSpan.FromDays(1);
}
