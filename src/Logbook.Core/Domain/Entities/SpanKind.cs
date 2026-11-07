// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Domain.Entities;

/// <summary>
///     What a <see cref="TraceSpan"/> measures. Stored as a number, so the values must not change.
/// </summary>
public enum SpanKind
{
    /// <summary>
    ///     A request the Application received.
    /// </summary>
    Request = 0,

    /// <summary>
    ///     A call the Application made to something else, e.g. over HTTP or to a database.
    /// </summary>
    Dependency = 1
}
