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
    ///     Requires a signed-in user. Every admin controller requires this policy.
    /// </summary>
    public const string AdminApi = "admin-api";

    /// <summary>
    ///     Requires the API Key of an Application. Every controller that Applications send to requires this policy.
    /// </summary>
    public const string ApplicationApiKey = "application-api-key";

    /// <summary>
    ///     Requires a signed-in user or the monitoring key. Only the detailed health report requires this policy.
    /// </summary>
    public const string HealthDetails = "health-details";

    /// <summary>
    ///     Request header that every request of a session must send to change something.
    /// </summary>
    /// <remarks>
    ///     A browser sends cookies by itself, but another site can only set this header after asking the server, which does
    ///     not allow it. The cookie is <c>SameSite=Strict</c>, but that treats another subdomain of the same domain as the
    ///     same site, so this also protects against, for example, a script on such a neighbor.
    /// </remarks>
    public const string RequestedWithHeader = "X-Requested-With";

    /// <summary>
    ///     Registers the policies.
    /// </summary>
    /// <param name="options">The authorization options.</param>
    public static void AddLogbookPolicies(this AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddPolicy(AdminApi, policy => policy
            .AddAuthenticationSchemes(SessionAuthentication.SchemeName)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => context.Resource is HttpContext httpContext && IsNotCrossSite(httpContext.Request)));

        options.AddPolicy(ApplicationApiKey, policy => policy
            .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser());

        options.AddPolicy(HealthDetails, policy => policy
            .AddAuthenticationSchemes(SessionAuthentication.SchemeName, MonitoringKeyAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser());
    }

    private static bool IsNotCrossSite(HttpRequest request)
    {
        return HttpMethods.IsGet(request.Method)
            || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method)
            || request.Headers.ContainsKey(RequestedWithHeader);
    }
}
