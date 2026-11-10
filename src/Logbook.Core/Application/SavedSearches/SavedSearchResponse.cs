// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.Entities;

namespace Logbook.Core.Application.SavedSearches;

/// <summary>
///     Represents a saved search of the log explorer.
/// </summary>
/// <param name="Id" example="0199d1a2-7c3e-7b4a-9f10-5c963f66afa6">Id of the saved search.</param>
/// <param name="Name" example="Errors in Production">Display Name, unique within Logbook.</param>
/// <param name="Query" example="level=Error&amp;env=Production&amp;range=24h">The query string of the log explorer, without the leading <c>?</c>.</param>
/// <param name="CreatedAt" example="2026-11-04T10:15:00Z">Point in time (UTC) when the saved search was created.</param>
/// <param name="ModifiedAt" example="2026-11-05T08:00:00Z">Point in time (UTC) when it was last changed, or null if it never was.</param>
public sealed record SavedSearchResponse(Guid Id, string Name, string Query, DateTime CreatedAt, DateTime? ModifiedAt)
{
    /// <summary>
    ///     Creates the response for a saved search.
    /// </summary>
    /// <param name="savedSearch">The saved search.</param>
    /// <returns>The response.</returns>
    public static SavedSearchResponse From(SavedSearch savedSearch)
    {
        ArgumentNullException.ThrowIfNull(savedSearch);

        return new SavedSearchResponse(savedSearch.Id, savedSearch.Name, savedSearch.Query, savedSearch.CreatedAt, savedSearch.ModifiedAt);
    }
}
