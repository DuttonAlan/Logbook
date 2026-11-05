// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Sdk;

/// <summary>
///     What <see cref="LogEventGuard"/> decided about an event.
/// </summary>
internal enum LogEventGuardResult
{
    /// <summary>
    ///     The event can be sent.
    /// </summary>
    Accepted,

    /// <summary>
    ///     The event can be sent after cutting some of its text, and is marked as truncated.
    /// </summary>
    Truncated,

    /// <summary>
    ///     The timestamp is outside of what the server accepts.
    /// </summary>
    Expired,

    /// <summary>
    ///     The event is too large, even after cutting its text.
    /// </summary>
    TooLarge
}
