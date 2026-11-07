// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Api.Authentication;

/// <summary>
///     The settings of the sign-in to the web interface and the admin API, bound from the section <see cref="SectionName"/>.
/// </summary>
public sealed class SignInOptions
{
    /// <summary>
    ///     Name of the configuration section.
    /// </summary>
    public const string SectionName = "SignIn";

    /// <summary>
    ///     How long a session lasts from the moment of signing in. Activity does not extend it: the web interface checks
    ///     the health of the server all the time, so an open tab would otherwise never be signed out.
    /// </summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    ///     The local administrator, who signs in with the configured name and password.
    /// </summary>
    public LocalAdministratorOptions Administrator { get; set; } = new();

    /// <summary>
    ///     The sign-in through an OpenID Connect provider.
    /// </summary>
    public OpenIdConnectSignInOptions OpenIdConnect { get; set; } = new();
}

/// <summary>
///     The settings of the local administrator.
/// </summary>
public sealed class LocalAdministratorOptions
{
    /// <summary>
    ///     Minimum length of <see cref="Password"/>.
    /// </summary>
    public const int PasswordMinLength = 12;

    /// <summary>
    ///     The name to sign in with.
    /// </summary>
    public string UserName { get; set; } = "admin";

    /// <summary>
    ///     The password to sign in with. Without a password, the local administrator cannot sign in.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    ///     Whether the local administrator can sign in.
    /// </summary>
    public bool IsEnabled => !string.IsNullOrEmpty(Password);
}

/// <summary>
///     The settings of the sign-in through an OpenID Connect provider.
/// </summary>
public sealed class OpenIdConnectSignInOptions
{
    /// <summary>
    ///     Address of the provider, which also serves its discovery document. Without it, this sign-in is off.
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    ///     The Id of Logbook at the provider.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    ///     The secret of Logbook at the provider, unless it is registered as a public client.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    ///     The name of the provider on the button of the sign-in page.
    /// </summary>
    public string DisplayName { get; set; } = "OpenID Connect";

    /// <summary>
    ///     Scopes to request in addition to <c>openid</c> and <c>profile</c>, e.g. one that adds the required claim.
    /// </summary>
    public IReadOnlyList<string> AdditionalScopes { get; set; } = [];

    /// <summary>
    ///     Type of the claim that a user needs to get access, e.g. <c>groups</c>. Required, because signing in gives full
    ///     access, and many providers let anyone create an account.
    /// </summary>
    public string RequiredClaimType { get; set; } = string.Empty;

    /// <summary>
    ///     Value of the claim that a user needs to get access, e.g. <c>logbook-admins</c>.
    /// </summary>
    public string RequiredClaimValue { get; set; } = string.Empty;

    /// <summary>
    ///     Whether this sign-in is on.
    /// </summary>
    public bool IsEnabled => !string.IsNullOrEmpty(Authority);
}
