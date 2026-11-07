// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.ExceptionGroups.Search;

/// <summary>
///     Represents the groups of exceptions that were found.
/// </summary>
/// <param name="Items">The groups, in the requested order.</param>
public sealed record SearchExceptionGroupsResponse(IReadOnlyList<ExceptionGroupResponse> Items);
