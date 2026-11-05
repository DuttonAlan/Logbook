// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Sdk;

/// <summary>
///     Severity of a <see cref="LogbookEvent"/>.
/// </summary>
public enum LogbookLevel
{
    /// <summary>
    ///     The most detailed messages, usually only enabled while looking for a problem.
    /// </summary>
    Trace,

    /// <summary>
    ///     Messages that help during development.
    /// </summary>
    Debug,

    /// <summary>
    ///     The normal flow of the application.
    /// </summary>
    Information,

    /// <summary>
    ///     Something unexpected that did not stop the application.
    /// </summary>
    Warning,

    /// <summary>
    ///     A failure of the current operation.
    /// </summary>
    Error,

    /// <summary>
    ///     A failure that needs immediate attention.
    /// </summary>
    Critical
}
