// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.Applications.Get;

/// <summary>
///     Represents the response for getting an Application.
/// </summary>
/// <param name="Id" example="0199d1a2-7c3e-7b4a-9f10-5c963f66afa6">Id of the Application.</param>
/// <param name="Name" example="Shop Web App">Display Name of the Application.</param>
/// <param name="ApiKeys">API Keys of the Application, including expired and revoked ones. Only metadata, never the API Key itself.</param>
/// <param name="CreatedAt">Point in time (UTC) when the Application was created.</param>
/// <param name="ModifiedAt">Point in time (UTC) when the Application was last changed, or null if it never was.</param>
/// <param name="RetentionDays" example="14">Days to keep Log Events set for this Application, or null if it uses the default of the server.</param>
/// <param name="EffectiveRetentionDays" example="14">Days its Log Events are kept: <c>RetentionDays</c>, or else the default of the server.</param>
public sealed record GetApplicationResponse(
    Guid Id,
    string Name,
    IReadOnlyList<ApiKeyResponse> ApiKeys,
    DateTime CreatedAt,
    DateTime? ModifiedAt,
    int? RetentionDays,
    int EffectiveRetentionDays);
