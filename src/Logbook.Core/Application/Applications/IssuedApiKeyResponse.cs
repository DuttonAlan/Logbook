// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.Applications;

/// <summary>
///     Represents a newly issued API Key, including its plain-text value.
/// </summary>
/// <remarks>
///     The plain-text API Key is only returned once and cannot be retrieved later.
/// </remarks>
/// <param name="Id" example="0199d1a2-7c3e-7b4a-9f10-6c963f66afa6">Id of the API Key, e.g. to revoke it later.</param>
/// <param name="Key" example="q3Jv0Wc8uYb1nH4kT9sXe2LmPd7aRfG5zVo6iNwB0yE">The plain-text API Key. Store it securely, it is not shown again.</param>
/// <param name="CreatedAt">Point in time (UTC) when the API Key was issued.</param>
/// <param name="ExpiresAt">Point in time (UTC) when the API Key expires, or null if it does not expire.</param>
public sealed record IssuedApiKeyResponse(Guid Id, string Key, DateTime CreatedAt, DateTime? ExpiresAt);
