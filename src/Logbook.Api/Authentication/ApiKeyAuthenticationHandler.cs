// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;

using Logbook.Core.Application.LogEvents.AuthenticateApiKey;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Logbook.Api.Authentication;

/// <summary>
///     Authenticates requests of Applications by their API Key, sent in the header <c>X-Logbook-Key</c>.
/// </summary>
/// <remarks>
///     The API Key is checked in the database on every request, so a revoked API Key is rejected right away. The admin
///     key is not accepted. API Keys are never logged, only their Ids.
/// </remarks>
/// <param name="options">The options of the scheme.</param>
/// <param name="logger">The LoggerFactory of the handler.</param>
/// <param name="encoder">The UrlEncoder of the handler.</param>
/// <param name="authenticateApiKeyService">The Service to check the API Key.</param>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AuthenticateApiKeyService authenticateApiKeyService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>
    ///     Name of the authentication scheme.
    /// </summary>
    public const string SchemeName = "LogbookApiKey";

    /// <summary>
    ///     Name of the request header with the API Key.
    /// </summary>
    public const string HeaderName = "X-Logbook-Key";

    /// <inheritdoc/>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? key = Request.Headers[HeaderName];

        if (string.IsNullOrEmpty(key))
        {
            return AuthenticateResult.NoResult();
        }

        var apiKey = await authenticateApiKeyService.ExecuteAsync(key, Context.RequestAborted);

        if (apiKey is null)
        {
            return AuthenticateResult.Fail("The API Key is not active.");
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ApiKeyClaimTypes.ApiKeyId, apiKey.ApiKeyId.ToString("D", CultureInfo.InvariantCulture)),
                new Claim(ApiKeyClaimTypes.ApplicationId, apiKey.ApplicationId.ToString("D", CultureInfo.InvariantCulture))
            ],
            SchemeName);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    /// <inheritdoc/>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.Append(HeaderNames.WWWAuthenticate, HeaderName);

        return Task.CompletedTask;
    }
}
