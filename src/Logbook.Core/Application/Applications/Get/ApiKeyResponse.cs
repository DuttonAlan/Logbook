// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.Applications.Get;

/// <summary>
///     Represents the metadata of an API Key. The API Key itself is never returned.
/// </summary>
/// <param name="Id" example="0199d1a2-7c3e-7b4a-9f10-6c963f66afa6">Id of the API Key.</param>
/// <param name="CreatedAt">Point in time (UTC) when the API Key was issued.</param>
/// <param name="ExpiresAt">Point in time (UTC) when the API Key expires, or null if it does not expire.</param>
/// <param name="RevokedAt">Point in time (UTC) when the API Key was revoked, or null if it is not revoked.</param>
/// <param name="IsActive" example="true">Whether the API Key can currently be used, i.e. it is neither expired nor revoked.</param>
public sealed record ApiKeyResponse(
    Guid Id,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? RevokedAt,
    bool IsActive);
