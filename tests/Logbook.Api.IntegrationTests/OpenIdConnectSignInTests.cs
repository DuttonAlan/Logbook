// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

using Logbook.Api.Authentication;

using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Runs the whole sign-in through OpenID Connect against a provider that answers like a real one.
/// </summary>
/// <param name="factory">The Api under test.</param>
public sealed class OpenIdConnectSignInTests(LogbookApiFactory factory) : IClassFixture<LogbookApiFactory>
{
    private const string Authority = "https://provider.test";
    private const string RequiredGroup = "logbook-admins";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SignIn_RedirectsToTheProvider_WithTheCodeFlowAndPkce()
    {
        using var provider = new FakeProvider();
        using var host = CreateHost(provider);
        using var client = CreateBrowser(host);

        using var response = await client.GetAsync("/api/v1/auth/oidc/sign-in", Token);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith($"{Authority}/authorize", response.Headers.Location!.AbsoluteUri, StringComparison.Ordinal);

        var query = HttpUtility.ParseQueryString(response.Headers.Location.Query);

        Assert.Equal("code", query["response_type"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("logbook", query["client_id"]);
        Assert.Equal("http://localhost/api/v1/auth/oidc/callback", query["redirect_uri"]);
        Assert.Contains("extra", query["scope"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task UserWithTheRequiredClaim_IsSignedIn_AndContinuesWhereTheyWere()
    {
        using var provider = new FakeProvider { Groups = ["others", RequiredGroup] };
        using var host = CreateHost(provider);
        using var client = CreateBrowser(host);

        using var callback = await SignInAtProviderAsync(client, provider, "/applications?x=1");

        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        Assert.Equal("/applications?x=1", callback.Headers.Location!.OriginalString);

        var session = await client.GetFromJsonAsync<SessionResponse>("/api/v1/auth/session", Token);
        Assert.True(session!.IsSignedIn);
        Assert.Equal("ada", session.UserName);

        using var applications = await client.GetAsync("/api/v1/applications", Token);
        Assert.Equal(HttpStatusCode.OK, applications.StatusCode);

        Assert.True(provider.ReceivedCodeVerifier);
        Assert.True(provider.ReceivedClientSecret);
    }

    [Fact]
    public async Task ClaimFromTheUserInfoEndpoint_Counts()
    {
        using var provider = new FakeProvider { Groups = [], UserInfoGroups = [RequiredGroup] };
        using var host = CreateHost(provider);
        using var client = CreateBrowser(host);

        using var callback = await SignInAtProviderAsync(client, provider, "/");

        Assert.Equal("/", callback.Headers.Location!.OriginalString);

        var session = await client.GetFromJsonAsync<SessionResponse>("/api/v1/auth/session", Token);
        Assert.True(session!.IsSignedIn);
    }

    [Fact]
    public async Task UserWithoutTheRequiredClaim_IsDenied_AndGetsNoSession()
    {
        using var provider = new FakeProvider { Groups = ["others"] };
        using var host = CreateHost(provider);
        using var client = CreateBrowser(host);

        using var callback = await SignInAtProviderAsync(client, provider, "/");

        Assert.Equal("/?signInError=access-denied", callback.Headers.Location!.OriginalString);

        using var applications = await client.GetAsync("/api/v1/applications", Token);
        Assert.Equal(HttpStatusCode.Unauthorized, applications.StatusCode);
    }

    [Theory]
    [InlineData("https://evil.test/")]
    [InlineData("//evil.test/")]
    [InlineData("/\\evil.test/")]
    public async Task ReturnAddressOnAnotherSite_IsIgnored(string returnUrl)
    {
        using var provider = new FakeProvider { Groups = [RequiredGroup] };
        using var host = CreateHost(provider);
        using var client = CreateBrowser(host);

        using var callback = await SignInAtProviderAsync(client, provider, returnUrl);

        Assert.Equal("/", callback.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task CallbackThatWasNotStartedHere_FailsWithoutASession()
    {
        using var provider = new FakeProvider { Groups = [RequiredGroup] };
        using var host = CreateHost(provider);
        using var client = CreateBrowser(host);

        using var callback = await client.GetAsync("/api/v1/auth/oidc/callback?code=forged&state=forged", Token);

        Assert.Equal("/?signInError=failed", callback.Headers.Location!.OriginalString);

        using var applications = await client.GetAsync("/api/v1/applications", Token);
        Assert.Equal(HttpStatusCode.Unauthorized, applications.StatusCode);
    }

    [Fact]
    public async Task TokenSignedByAnotherKey_FailsWithoutASession()
    {
        using var provider = new FakeProvider { Groups = [RequiredGroup], SignsWithWrongKey = true };
        using var host = CreateHost(provider);
        using var client = CreateBrowser(host);

        using var callback = await SignInAtProviderAsync(client, provider, "/");

        Assert.Equal("/?signInError=failed", callback.Headers.Location!.OriginalString);

        using var applications = await client.GetAsync("/api/v1/applications", Token);
        Assert.Equal(HttpStatusCode.Unauthorized, applications.StatusCode);
    }

    private static HttpClient CreateBrowser(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        client.DefaultRequestHeaders.Add("X-Requested-With", "tests");

        return client;
    }

    /// <summary>
    ///     Does what the browser and the provider do between leaving Logbook and coming back.
    /// </summary>
    private static async Task<HttpResponseMessage> SignInAtProviderAsync(HttpClient client, FakeProvider provider, string returnUrl)
    {
        using var start = await client.GetAsync($"/api/v1/auth/oidc/sign-in?returnUrl={Uri.EscapeDataString(returnUrl)}", Token);

        var query = HttpUtility.ParseQueryString(start.Headers.Location!.Query);

        provider.Nonce = query["nonce"];

        return await client.GetAsync(
            $"/api/v1/auth/oidc/callback?code=the-code&state={Uri.EscapeDataString(query["state"]!)}",
            Token);
    }

    private WebApplicationFactory<Program> CreateHost(FakeProvider provider)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("SignIn:OpenIdConnect:Authority", Authority);
            builder.UseSetting("SignIn:OpenIdConnect:ClientId", "logbook");
            builder.UseSetting("SignIn:OpenIdConnect:ClientSecret", "the-secret");
            builder.UseSetting("SignIn:OpenIdConnect:AdditionalScopes:0", "extra");
            builder.UseSetting("SignIn:OpenIdConnect:RequiredClaimType", "groups");
            builder.UseSetting("SignIn:OpenIdConnect:RequiredClaimValue", RequiredGroup);

            builder.ConfigureTestServices(services => services.Configure<OpenIdConnectOptions>(
                SessionAuthentication.OpenIdConnectSchemeName,
                options => options.BackchannelHttpHandler = provider));
        });
    }

    private sealed class FakeProvider : HttpMessageHandler
    {
        private readonly RSA _key = RSA.Create(2048);

        public string[] Groups { get; init; } = [];

        public string[] UserInfoGroups { get; init; } = [];

        public bool SignsWithWrongKey { get; init; }

        public string? Nonce { get; set; }

        public bool ReceivedCodeVerifier { get; private set; }

        public bool ReceivedClientSecret { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return request.RequestUri!.AbsolutePath switch
            {
                "/.well-known/openid-configuration" => Json(new Dictionary<string, object>
                {
                    ["issuer"] = Authority,
                    ["authorization_endpoint"] = $"{Authority}/authorize",
                    ["token_endpoint"] = $"{Authority}/token",
                    ["userinfo_endpoint"] = $"{Authority}/userinfo",
                    ["jwks_uri"] = $"{Authority}/jwks"
                }),
                "/jwks" => Json(new { keys = new[] { PublicKey() } }),
                "/token" => await TokenAsync(request, cancellationToken),
                "/userinfo" => Json(new Dictionary<string, object> { ["sub"] = "user-1", ["groups"] = UserInfoGroups }),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }

        protected override void Dispose(bool disposing)
        {
            _key.Dispose();

            base.Dispose(disposing);
        }

        private static HttpResponseMessage Json(object value)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
            };
        }

        private async Task<HttpResponseMessage> TokenAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(cancellationToken));

            ReceivedCodeVerifier = !string.IsNullOrEmpty(form["code_verifier"]);
            ReceivedClientSecret = form["client_secret"] == "the-secret";

            return Json(new Dictionary<string, object>
            {
                ["access_token"] = "the-access-token",
                ["token_type"] = "Bearer",
                ["expires_in"] = 300,
                ["id_token"] = CreateIdToken()
            });
        }

        private string CreateIdToken()
        {
            using var otherKey = RSA.Create(2048);

            var credentials = new SigningCredentials(
                new RsaSecurityKey(SignsWithWrongKey ? otherKey : _key) { KeyId = "key-1" },
                SecurityAlgorithms.RsaSha256);

            return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = Authority,
                Audience = "logbook",
                Expires = DateTime.UtcNow.AddMinutes(5),
                SigningCredentials = credentials,
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = "user-1",
                    ["nonce"] = Nonce!,
                    ["preferred_username"] = "ada",
                    ["groups"] = Groups
                }
            });
        }

        private Dictionary<string, string> PublicKey()
        {
            var parameters = _key.ExportParameters(includePrivateParameters: false);

            return new Dictionary<string, string>
            {
                ["kty"] = "RSA",
                ["use"] = "sig",
                ["alg"] = "RS256",
                ["kid"] = "key-1",
                ["n"] = Base64UrlEncoder.Encode(parameters.Modulus),
                ["e"] = Base64UrlEncoder.Encode(parameters.Exponent)
            };
        }
    }
}
