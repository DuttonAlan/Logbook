// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Logbook.Api.Authentication;

/// <summary>
///     The session after signing in: a cookie that the browser cannot read from a script and sends only to this site.
/// </summary>
internal static class SessionAuthentication
{
    /// <summary>
    ///     Name of the authentication scheme of the session.
    /// </summary>
    public const string SchemeName = "LogbookSession";

    /// <summary>
    ///     Name of the authentication scheme that signs in through an OpenID Connect provider.
    /// </summary>
    public const string OpenIdConnectSchemeName = "LogbookOpenIdConnect";

    /// <summary>
    ///     The address the provider sends the browser back to, which must be registered there.
    /// </summary>
    public const string OpenIdConnectCallbackPath = "/api/v1/auth/oidc/callback";

    /// <summary>
    ///     Name of the session cookie.
    /// </summary>
    public const string CookieName = "logbook.session";

    private const string StampClaimType = "logbook:sign_in_stamp";
    private const string OpenIdConnectStamp = "oidc";

    /// <summary>
    ///     Adds the session, and the OpenID Connect sign-in if it is configured.
    /// </summary>
    /// <param name="services">The Service Collection.</param>
    /// <param name="configuration">The configuration with the settings of the sign-in.</param>
    /// <param name="environment">The environment.</param>
    /// <returns>The builder, to add further schemes.</returns>
    public static AuthenticationBuilder AddLogbookSession(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var signIn = configuration.GetSection(SignInOptions.SectionName).Get<SignInOptions>() ?? new SignInOptions();

        var builder = services.AddAuthentication(SchemeName)
            .AddCookie(SchemeName, options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;

                // Not sent with requests from other sites. The page itself still gets it after coming back from the
                // provider, as it calls the API from this site.
                options.Cookie.SameSite = SameSiteMode.Strict;

                // Secure as soon as the request is HTTPS. Plain HTTP stays possible for http://localhost, and the
                // production settings are checked at startup.
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

                options.ExpireTimeSpan = signIn.SessionLifetime;
                options.SlidingExpiration = false;

                // This is an API, so nothing redirects to a page.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;

                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;

                    return Task.CompletedTask;
                };
                options.Events.OnValidatePrincipal = ValidateSessionAsync;
            });

        if (signIn.OpenIdConnect.IsEnabled)
        {
            builder.AddOpenIdConnect(OpenIdConnectSchemeName, options => ConfigureOpenIdConnect(options, signIn.OpenIdConnect, environment));
        }

        return builder;
    }

    /// <summary>
    ///     Creates the session of the local administrator. It ends when the name or the password change.
    /// </summary>
    /// <param name="options">The settings of the local administrator.</param>
    /// <returns>The user of the session.</returns>
    public static ClaimsPrincipal CreateAdministrator(LocalAdministratorOptions options)
    {
        return CreatePrincipal(options.UserName, CreateStamp(options));
    }

    private static ClaimsPrincipal CreatePrincipal(string userName, string stamp)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, userName), new Claim(StampClaimType, stamp)],
            SchemeName));
    }

    private static string CreateStamp(LocalAdministratorOptions options)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Password), Encoding.UTF8.GetBytes(options.UserName));

        return Convert.ToBase64String(hash);
    }

    /// <summary>
    ///     A session is only as good as what it was created with, so a changed password, or a way of signing in that was
    ///     turned off, also ends the sessions from before.
    /// </summary>
    private static Task ValidateSessionAsync(CookieValidatePrincipalContext context)
    {
        var signIn = context.HttpContext.RequestServices.GetRequiredService<IOptions<SignInOptions>>().Value;
        var stamp = context.Principal?.FindFirstValue(StampClaimType);

        var isValid = stamp == OpenIdConnectStamp
            ? signIn.OpenIdConnect.IsEnabled
            : signIn.Administrator.IsEnabled && IsSameStamp(stamp, CreateStamp(signIn.Administrator));

        if (!isValid)
        {
            context.RejectPrincipal();
        }

        return Task.CompletedTask;
    }

    private static bool IsSameStamp(string? stamp, string expected)
    {
        return stamp is not null
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(stamp), Encoding.UTF8.GetBytes(expected));
    }

    private static void ConfigureOpenIdConnect(OpenIdConnectOptions options, OpenIdConnectSignInOptions settings, IHostEnvironment environment)
    {
        options.SignInScheme = SchemeName;
        options.Authority = settings.Authority;
        options.ClientId = settings.ClientId;
        options.ClientSecret = settings.ClientSecret;
        options.RequireHttpsMetadata = !environment.IsDevelopment();
        options.CallbackPath = OpenIdConnectCallbackPath;

        options.ResponseType = OpenIdConnectResponseType.Code;
        options.ResponseMode = OpenIdConnectResponseMode.Query;
        options.UsePkce = true;

        // Lax is enough for a redirect back with the query mode, and unlike None it also works on http://localhost.
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.NonceCookie.SameSite = SameSiteMode.Lax;
        options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

        // Tokens are not needed after signing in. Claims keep the names the provider uses, which the settings refer to.
        options.SaveTokens = false;
        options.MapInboundClaims = false;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.ClaimActions.MapJsonKey(settings.RequiredClaimType, settings.RequiredClaimType);

        foreach (var scope in settings.AdditionalScopes)
        {
            options.Scope.Add(scope);
        }

        options.Events.OnTicketReceived = context =>
        {
            var logger = CreateLogger(context.HttpContext);
            var user = context.Principal!;

            if (!user.HasClaim(settings.RequiredClaimType, settings.RequiredClaimValue))
            {
                logger.OpenIdConnectSignInDenied(user.FindFirstValue("sub") ?? "unknown");
                context.Response.Redirect("/?signInError=access-denied");
                context.HandleResponse();

                return Task.CompletedTask;
            }

            var userName = user.FindFirstValue("preferred_username")
                ?? user.FindFirstValue("email")
                ?? user.FindFirstValue("name")
                ?? user.FindFirstValue("sub")
                ?? "unknown";

            // Only what Logbook needs: the cookie stays small, and holds nothing of the provider.
            context.Principal = CreatePrincipal(userName, OpenIdConnectStamp);

            logger.SignedIn(userName);

            return Task.CompletedTask;
        };

        options.Events.OnRemoteFailure = context =>
        {
            // Only the type: the message can repeat what the provider or the address contain.
            CreateLogger(context.HttpContext).OpenIdConnectSignInFailed(context.Failure?.GetType().Name ?? "unknown");
            context.Response.Redirect("/?signInError=failed");
            context.HandleResponse();

            return Task.CompletedTask;
        };
    }

    private static ILogger CreateLogger(HttpContext httpContext)
    {
        return httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SessionAuthentication).FullName!);
    }
}
