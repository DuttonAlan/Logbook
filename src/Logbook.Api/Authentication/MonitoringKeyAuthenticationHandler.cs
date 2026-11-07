// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Logbook.Api.Authentication;

/// <summary>
///     Authenticates monitoring tools by the configured key, sent as <c>Authorization: Bearer</c> (RFC 6750).
/// </summary>
/// <remarks>
///     Monitoring tools cannot sign in, and need no more than the health report, which is the only endpoint that accepts
///     this scheme. API Keys of Applications are not accepted.
/// </remarks>
/// <param name="options">The options of the scheme.</param>
/// <param name="logger">The LoggerFactory of the handler.</param>
/// <param name="encoder">The UrlEncoder of the handler.</param>
/// <param name="monitoringKeyOptions">The settings with the key.</param>
public sealed class MonitoringKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<MonitoringKeyOptions> monitoringKeyOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>
    ///     Name of the authentication scheme.
    /// </summary>
    public const string SchemeName = "LogbookMonitoringKey";

    private const string BearerPrefix = "Bearer ";

    /// <inheritdoc/>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? authorization = Request.Headers.Authorization;

        if (authorization is null || !authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!IsMonitoringKey(authorization[BearerPrefix.Length..].Trim()))
        {
            return Task.FromResult(AuthenticateResult.Fail("The monitoring key is not valid."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "monitoring")], SchemeName);

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    /// <inheritdoc/>
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var result = await HandleAuthenticateOnceSafeAsync();

        Response.StatusCode = StatusCodes.Status401Unauthorized;

        // RFC 6750, Section 3: no error code if no token was sent, "invalid_token" if it was rejected.
        Response.Headers.Append(
            HeaderNames.WWWAuthenticate,
            result.Failure is null ? "Bearer" : "Bearer error=\"invalid_token\"");
    }

    private bool IsMonitoringKey(string candidate)
    {
        var key = monitoringKeyOptions.Value.MonitoringKey;

        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        // Comparing hashes in constant time reveals neither the content nor the length of the key.
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(candidate)),
            SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }
}
