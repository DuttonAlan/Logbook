// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Api.Authentication;

/// <summary>
///     Represents who is signed in, and how someone can sign in.
/// </summary>
/// <param name="IsSignedIn">Whether this request belongs to a session.</param>
/// <param name="UserName" example="admin">The name of the signed-in user, or null.</param>
/// <param name="CanSignInWithPassword">Whether the local administrator can sign in.</param>
/// <param name="OpenIdConnectProvider" example="Authora">The name of the OpenID Connect provider, or null if this sign-in is off.</param>
public sealed record SessionResponse(
    bool IsSignedIn,
    string? UserName,
    bool CanSignInWithPassword,
    string? OpenIdConnectProvider);
