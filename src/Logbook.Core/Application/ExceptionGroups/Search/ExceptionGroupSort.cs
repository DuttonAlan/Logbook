// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.ExceptionGroups.Search;

/// <summary>
///     The order of groups of exceptions.
/// </summary>
public enum ExceptionGroupSort
{
    /// <summary>
    ///     The group with the latest occurrence first.
    /// </summary>
    LastOccurrence = 0,

    /// <summary>
    ///     The group with the most occurrences first.
    /// </summary>
    Count = 1
}
