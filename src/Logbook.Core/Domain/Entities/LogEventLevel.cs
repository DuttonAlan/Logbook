// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Domain.Entities;

/// <summary>
///     The severity of a <see cref="LogEvent"/>, with the names of the log levels of <c>ILogger</c>.
/// </summary>
/// <remarks>
///     The numbers are stored in the database, and higher means more severe, so they must not change.
/// </remarks>
public enum LogEventLevel
{
    /// <summary>
    ///     The most detailed messages, possibly with sensitive data.
    /// </summary>
    Trace = 0,

    /// <summary>
    ///     Messages for debugging during development.
    /// </summary>
    Debug = 1,

    /// <summary>
    ///     The general flow of the application.
    /// </summary>
    Information = 2,

    /// <summary>
    ///     Something unexpected that did not stop the application.
    /// </summary>
    Warning = 3,

    /// <summary>
    ///     A failure of the current operation.
    /// </summary>
    Error = 4,

    /// <summary>
    ///     A failure that requires immediate attention.
    /// </summary>
    Critical = 5
}
