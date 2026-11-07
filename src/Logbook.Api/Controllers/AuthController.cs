// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Security.Cryptography;
using System.Text;

using Logbook.Api.Authentication;
using Logbook.Api.Authorization;
using Logbook.Api.RateLimiting;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Logbook.Api.Controllers;

/// <summary>
///     Provides the Endpoints to sign in to the web interface and the admin API, and to sign out.
/// </summary>
/// <param name="signInOptions">The settings of the sign-in.</param>
/// <param name="invalidSignInLimiter">The limit for failed sign-ins.</param>
/// <param name="rateLimitReporter">Reports when that limit is reached.</param>
/// <param name="logger">The Logger to record sign-ins.</param>
[ApiController]
[Route("api/v1/auth")]
[Tags("Sign-in")]
public sealed class AuthController(
    IOptions<SignInOptions> signInOptions,
    InvalidSignInLimiter invalidSignInLimiter,
    RateLimitReporter rateLimitReporter,
    ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>
    ///     Gets who is signed in, and how someone can sign in.
    /// </summary>
    /// <remarks>
    ///     This needs no session, so a web interface can find out whether it has one without causing an error.
    /// </remarks>
    /// <returns>200 OK with the session.</returns>
    /// <response code="200">Responds with the state of the session.</response>
    [HttpGet("session")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK, "application/json")]
    public IActionResult GetSession()
    {
        var options = signInOptions.Value;
        var isSignedIn = User.Identity?.IsAuthenticated == true;

        return Ok(new SessionResponse(
            isSignedIn,
            isSignedIn ? User.Identity!.Name : null,
            options.Administrator.IsEnabled,
            options.OpenIdConnect.IsEnabled ? options.OpenIdConnect.DisplayName : null));
    }

    /// <summary>
    ///     Signs in the local administrator.
    /// </summary>
    /// <remarks>
    ///     Starts a session, in the cookie of the response. Failed attempts are limited per IP address by the setting
    ///     <c>RateLimiting:InvalidSignInPermitsPerMinute</c>.
    /// </remarks>
    /// <param name="request">The name and the password.</param>
    /// <returns>204 No Content with the session cookie.</returns>
    /// <response code="204">Responds with the session cookie.</response>
    /// <response code="400">Responds with validation problem details if the request body is invalid.</response>
    /// <response code="401">Responds with problem details if the name or the password is wrong, or the local administrator is turned off.</response>
    /// <response code="415">Responds with problem details if the request body is not JSON.</response>
    /// <response code="429">Responds with problem details and a Retry-After header after too many failed attempts from this IP address.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred.</response>
    /// <response code="503">Responds with problem details and a Retry-After header if the database with the keys of the sessions cannot be reached.</response>
    [HttpPost("sign-in")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> SignIn(SignInRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (!invalidSignInLimiter.TryReserve(ipAddress, out var retryAfter))
        {
            rateLimitReporter.TooManyInvalidSignIns(ipAddress);

            await RateLimitPolicies.WriteTooManyRequestsAsync(HttpContext, retryAfter, "Too many failed sign-ins. Try again later.");

            return new EmptyResult();
        }

        var administrator = signInOptions.Value.Administrator;

        if (!IsAdministrator(administrator, request))
        {
            logger.SignInFailed(ipAddress);

            return Problem(statusCode: StatusCodes.Status401Unauthorized, detail: "The name or the password is wrong.");
        }

        invalidSignInLimiter.Release(ipAddress);

        await HttpContext.SignInAsync(SessionAuthentication.SchemeName, SessionAuthentication.CreateAdministrator(administrator));

        logger.SignedIn(administrator.UserName);

        return NoContent();
    }

    /// <summary>
    ///     Signs out, by removing the session cookie.
    /// </summary>
    /// <remarks>
    ///     Requires the header <c>X-Requested-With</c>, like every request of a session that changes something.
    /// </remarks>
    /// <returns>204 No Content.</returns>
    /// <response code="204">Responds with no content once the session cookie is removed.</response>
    /// <response code="403">Responds with no content if the header <c>X-Requested-With</c> is missing.</response>
    [HttpPost("sign-out")]
    [Authorize(AuthorizationPolicies.AdminApi)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public new async Task<IActionResult> SignOut()
    {
        await HttpContext.SignOutAsync(SessionAuthentication.SchemeName);

        logger.SignedOut(User.Identity?.Name);

        return NoContent();
    }

    /// <summary>
    ///     Starts the sign-in through the OpenID Connect provider.
    /// </summary>
    /// <remarks>
    ///     Open this in the browser, not with a script: it redirects to the provider, which later returns to the web interface.
    /// </remarks>
    /// <param name="returnUrl" example="/applications">Where the web interface continues afterwards, a path on this server.</param>
    /// <returns>302 Found to the provider.</returns>
    /// <response code="302">Redirects to the provider.</response>
    /// <response code="404">Responds with problem details if this sign-in is turned off.</response>
    /// <response code="500">Responds with problem details if an unexpected error occurred, e.g. the provider cannot be reached.</response>
    /// <response code="503">Responds with problem details and a Retry-After header if the database with the keys of the sessions cannot be reached.</response>
    [HttpGet("oidc/sign-in")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public IActionResult SignInWithOpenIdConnect([FromQuery] string? returnUrl = null)
    {
        if (!signInOptions.Value.OpenIdConnect.IsEnabled)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, detail: "The sign-in through OpenID Connect is turned off.");
        }

        // Only a path on this server: anything else would let a link send people from the sign-in to another site.
        var redirectUri = Url.IsLocalUrl(returnUrl) ? returnUrl : "/";

        return Challenge(new AuthenticationProperties { RedirectUri = redirectUri }, SessionAuthentication.OpenIdConnectSchemeName);
    }

    private static bool IsAdministrator(LocalAdministratorOptions administrator, SignInRequest request)
    {
        // Both are always compared, in constant time, so the time taken does not reveal which one is wrong.
        var isUserName = IsSame(request.UserName, administrator.UserName);
        var isPassword = IsSame(request.Password, administrator.Password);

        return administrator.IsEnabled && isUserName && isPassword;
    }

    private static bool IsSame(string candidate, string expected)
    {
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(candidate)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
    }
}
