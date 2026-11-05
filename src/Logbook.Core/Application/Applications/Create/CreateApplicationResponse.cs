// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.Applications.Create;

/// <summary>
///     Represents the response for creating an Application.
/// </summary>
/// <param name="Id" example="0199d1a2-7c3e-7b4a-9f10-5c963f66afa6">Id of the created Application.</param>
/// <param name="ApiKey">The first API Key of the Application, shown only once.</param>
public sealed record CreateApplicationResponse(Guid Id, IssuedApiKeyResponse ApiKey);
