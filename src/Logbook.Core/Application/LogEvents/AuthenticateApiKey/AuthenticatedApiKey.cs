// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.LogEvents.AuthenticateApiKey;

/// <summary>
///     Represents an active API Key that a request was authenticated with.
/// </summary>
/// <param name="ApiKeyId">Id of the API Key.</param>
/// <param name="ApplicationId">Id of the Application the API Key belongs to.</param>
public sealed record AuthenticatedApiKey(Guid ApiKeyId, Guid ApplicationId);
