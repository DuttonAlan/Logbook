// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Api.Authentication;

using Microsoft.AspNetCore.Authorization;

namespace Logbook.Api.Authorization;

/// <summary>
///     Authorization policies of the Api.
/// </summary>
internal static class AuthorizationPolicies
{
    /// <summary>
    ///     Requires the admin key. Every admin controller requires this policy.
    /// </summary>
    public const string AdminApi = "admin-api";

    /// <summary>
    ///     Requires the API Key of an Application. Every controller that Applications send to requires this policy.
    /// </summary>
    public const string ApplicationApiKey = "application-api-key";

    /// <summary>
    ///     Registers the policies.
    /// </summary>
    /// <param name="options">The authorization options.</param>
    public static void AddLogbookPolicies(this AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddPolicy(AdminApi, policy => policy
            .AddAuthenticationSchemes(AdminKeyAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser());

        options.AddPolicy(ApplicationApiKey, policy => policy
            .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser());
    }
}
