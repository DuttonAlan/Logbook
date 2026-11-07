// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Infrastructure.Persistence.Repositories;

/// <summary>
///     A row of the query for what is shown about a group of exceptions, besides its aggregates.
/// </summary>
public sealed class ExceptionGroupDetails
{
    /// <summary>
    ///     Id of the Application.
    /// </summary>
    public Guid ApplicationId { get; set; }

    /// <summary>
    ///     What identifies the group.
    /// </summary>
    public string Fingerprint { get; set; } = null!;

    /// <summary>
    ///     Point in time (UTC) of the earliest occurrence that is still stored.
    /// </summary>
    public DateTime FirstOccurrence { get; set; }

    /// <summary>
    ///     The exception of the latest occurrence, as the text of a JSON object.
    /// </summary>
    public string Exception { get; set; } = null!;
}
