// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Sdk;

/// <summary>
///     The limits an event must keep to be accepted by the server, read once from the options.
/// </summary>
/// <param name="options">The options, which must be valid.</param>
internal sealed class LogEventLimits(LogbookOptions options)
{
    /// <summary>
    ///     Maximum size of the JSON of one request before compression.
    /// </summary>
    public int MaxRequestBytes { get; } = options.MaxRequestBytes;

    /// <summary>
    ///     Maximum size of the content of one event.
    /// </summary>
    public int MaxEventBytes { get; } = options.MaxEventBytes;

    /// <summary>
    ///     Maximum number of characters of the message and of the message template.
    /// </summary>
    public int MaxMessageLength { get; } = options.MaxMessageLength;

    /// <summary>
    ///     Maximum age of an event.
    /// </summary>
    public TimeSpan MaxEventAge { get; } = options.MaxEventAge;

    /// <summary>
    ///     How far in the future the timestamp of an event may be.
    /// </summary>
    public TimeSpan MaxFutureOffset { get; } = options.MaxFutureOffset;
}
