// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Sdk;

/// <summary>
///     What a <see cref="LogbookSpan"/> measures.
/// </summary>
public enum LogbookSpanKind
{
    /// <summary>
    ///     A request the application received.
    /// </summary>
    Request = 0,

    /// <summary>
    ///     A call the application made to something else, e.g. over HTTP or to a database.
    /// </summary>
    Dependency = 1
}
