// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.ComponentModel.DataAnnotations;

using Logbook.Core.Domain.Entities;

namespace Logbook.Core.Application.SavedSearches;

/// <summary>
///     Represents the request to create or change a saved search.
/// </summary>
/// <param name="Name" example="Errors in Production">Display Name, unique within Logbook.</param>
/// <param name="Query" example="level=Error&amp;env=Production&amp;range=24h">The query string of the log explorer. A leading <c>?</c> is removed.</param>
public sealed record SaveSearchRequest(
    [Required, StringLength(SavedSearch.NameMaxLength, MinimumLength = 1)] string Name,
    [StringLength(SavedSearch.QueryMaxLength + 1)] string? Query);
