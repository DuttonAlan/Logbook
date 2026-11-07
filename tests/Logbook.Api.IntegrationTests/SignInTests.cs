// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net;
using System.Net.Http.Json;

using Logbook.Api.Authentication;
using Logbook.Core.Application.Applications.Create;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks at request level who can sign in, what a session allows, and when it ends.
/// </summary>
/// <param name="factory">The Api under test.</param>
public sealed class SignInTests(LogbookApiFactory factory) : IClassFixture<LogbookApiFactory>
{
    private const string Applications = "/api/v1/applications";
    private const string Session = "/api/v1/auth/session";
    private const string SignInEndpoint = "/api/v1/auth/sign-in";
    private const string SignOutEndpoint = "/api/v1/auth/sign-out";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AdminApi_WithoutSession_IsUnauthorized()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Applications, Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(LogbookApiFactory.AdminUserName, "wrong-password-0123456789")]
    [InlineData(LogbookApiFactory.AdminUserName, LogbookApiFactory.AdminPassword + "x")]
    [InlineData("someone-else", LogbookApiFactory.AdminPassword)]
    public async Task SignIn_WithWrongCredentials_IsUnauthorized_AndStartsNoSession(string userName, string password)
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(SignInEndpoint, new SignInRequest(userName, password), Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task SignIn_GivesAccessToTheAdminApi()
    {
        using var client = factory.CreateAdminClient();

        using var response = await client.GetAsync(Applications, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SessionCookie_CannotBeReadByScripts_AndIsNotSentByOtherSites()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            SignInEndpoint,
            new SignInRequest(LogbookApiFactory.AdminUserName, LogbookApiFactory.AdminPassword),
            Token);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));

        Assert.StartsWith("logbook.session=", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Session_TellsWhoIsSignedIn_AndHowToSignIn()
    {
        using var anonymous = factory.CreateClient();
        using var admin = factory.CreateAdminClient();

        var before = await anonymous.GetFromJsonAsync<SessionResponse>(Session, Token);
        var after = await admin.GetFromJsonAsync<SessionResponse>(Session, Token);

        Assert.Equal(new SessionResponse(false, null, true, null), before);
        Assert.Equal(new SessionResponse(true, LogbookApiFactory.AdminUserName, true, null), after);
    }

    [Fact]
    public async Task ChangingSomething_WithoutTheRequestedWithHeader_IsForbidden_EvenWithASession()
    {
        using var client = factory.CreateAdminClient();
        client.DefaultRequestHeaders.Remove("X-Requested-With");

        using var response = await client.PostAsJsonAsync(Applications, new CreateApplicationRequest($"Forged {Guid.NewGuid():N}"), Token);
        using var signOut = await client.PostAsync(SignOutEndpoint, content: null, Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, signOut.StatusCode);

        // Reading stays possible, and the session was not ended.
        using var read = await client.GetAsync(Applications, Token);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task SignIn_WithAFormInsteadOfJson_IsRejected_SoAnotherSiteCannotSubmitIt()
    {
        using var client = factory.CreateClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["userName"] = LogbookApiFactory.AdminUserName,
            ["password"] = LogbookApiFactory.AdminPassword
        });

        using var response = await client.PostAsync(SignInEndpoint, form, Token);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task SignOut_EndsTheSession()
    {
        using var client = factory.CreateAdminClient();

        using var signOut = await client.PostAsync(SignOutEndpoint, content: null, Token);
        using var afterwards = await client.GetAsync(Applications, Token);

        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);
    }

    [Fact]
    public async Task Session_ContinuesOnAnotherServerAndAfterARestart()
    {
        using var client = factory.CreateClient();
        using var signIn = await client.PostAsJsonAsync(
            SignInEndpoint,
            new SignInRequest(LogbookApiFactory.AdminUserName, LogbookApiFactory.AdminPassword),
            Token);
        var cookie = Assert.Single(signIn.Headers.GetValues("Set-Cookie")).Split(';')[0];

        using var other = factory.WithWebHostBuilder(builder => builder.UseSetting("SomethingElse", "another host"));
        using var otherClient = other.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Applications);
        request.Headers.Add("Cookie", cookie);

        using var response = await otherClient.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Session_Ends_WhenThePasswordChanges()
    {
        using var client = factory.CreateClient();
        using var signIn = await client.PostAsJsonAsync(
            SignInEndpoint,
            new SignInRequest(LogbookApiFactory.AdminUserName, LogbookApiFactory.AdminPassword),
            Token);
        var cookie = Assert.Single(signIn.Headers.GetValues("Set-Cookie")).Split(';')[0];

        using var changed = factory.WithWebHostBuilder(builder => builder.UseSetting("SignIn:Administrator:Password", "another-password-0123456789"));
        using var changedClient = changed.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Applications);
        request.Headers.Add("Cookie", cookie);

        using var response = await changedClient.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Session_Expires_AfterItsLifetime_EvenWhenInUse()
    {
        using var shortLived = factory.WithWebHostBuilder(builder => builder.UseSetting("SignIn:SessionLifetime", "00:00:01"));
        using var client = shortLived.CreateAdminClient();

        using var right = await client.GetAsync(Applications, Token);
        await Task.Delay(TimeSpan.FromSeconds(1.5), Token);
        using var later = await client.GetAsync(Applications, Token);

        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, later.StatusCode);
    }

    [Fact]
    public async Task TooManyFailedSignIns_AreRejected_WithRetryAfter_EvenWithTheRightPassword()
    {
        using var limited = WithSignInLimit(2);
        using var client = limited.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(client, "wrong-password-1"));
        Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(client, "wrong-password-2"));

        using var rejected = await client.PostAsJsonAsync(
            SignInEndpoint,
            new SignInRequest(LogbookApiFactory.AdminUserName, LogbookApiFactory.AdminPassword),
            Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter?.Delta);
        Assert.False(rejected.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task SuccessfulSignIns_DoNotUseUpTheLimit()
    {
        using var limited = WithSignInLimit(1);
        using var client = limited.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.NoContent, await SignInAsync(client, LogbookApiFactory.AdminPassword));
        }
    }

    [Fact]
    public async Task WithoutAdministratorPassword_ThePasswordSignInIsOff()
    {
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("SignIn:Administrator:Password", string.Empty);
            builder.UseSetting("SignIn:OpenIdConnect:Authority", "https://provider.test");
            builder.UseSetting("SignIn:OpenIdConnect:ClientId", "logbook");
            builder.UseSetting("SignIn:OpenIdConnect:RequiredClaimType", "groups");
            builder.UseSetting("SignIn:OpenIdConnect:RequiredClaimValue", "logbook-admins");
        });
        using var client = host.CreateClient();

        Assert.Equal(HttpStatusCode.BadRequest, await SignInAsync(client, string.Empty));
        Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(client, LogbookApiFactory.AdminPassword));

        var session = await client.GetFromJsonAsync<SessionResponse>(Session, Token);
        Assert.False(session!.CanSignInWithPassword);
        Assert.Equal("OpenID Connect", session.OpenIdConnectProvider);
    }

    [Fact]
    public async Task OpenIdConnectSignIn_IsNotFound_WhenItIsOff()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/auth/oidc/sign-in", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("SignIn:Administrator:Password", "")]
    [InlineData("SignIn:Administrator:Password", "too-short")]
    [InlineData("SignIn:Administrator:Password", "long-enough-but-with-a-line-break\n")]
    [InlineData("SignIn:Administrator:UserName", " ")]
    [InlineData("SignIn:SessionLifetime", "00:00:00")]
    [InlineData("SignIn:SessionLifetime", "31.00:00:00")]
    [InlineData("SignIn:OpenIdConnect:ClientId", "without-the-other-settings")]
    public void Startup_WithUnusableSettings_Fails(string setting, string value)
    {
        using var misconfigured = factory.WithWebHostBuilder(builder => builder.UseSetting(setting, value));

        var exception = Assert.Throws<OptionsValidationException>(() => misconfigured.CreateClient());

        Assert.DoesNotContain(LogbookApiFactory.AdminPassword, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://provider.test")]
    [InlineData("not an address")]
    public void Startup_WithOpenIdConnectProviderWithoutHttps_Fails(string authority)
    {
        using var misconfigured = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("SignIn:OpenIdConnect:Authority", authority);
            builder.UseSetting("SignIn:OpenIdConnect:ClientId", "logbook");
            builder.UseSetting("SignIn:OpenIdConnect:RequiredClaimType", "groups");
            builder.UseSetting("SignIn:OpenIdConnect:RequiredClaimValue", "logbook-admins");
        });

        Assert.Throws<OptionsValidationException>(() => misconfigured.CreateClient());
    }

    private static async Task<HttpStatusCode> SignInAsync(HttpClient client, string password)
    {
        using var response = await client.PostAsJsonAsync(SignInEndpoint, new SignInRequest(LogbookApiFactory.AdminUserName, password), Token);

        return response.StatusCode;
    }

    private WebApplicationFactory<Program> WithSignInLimit(int permitsPerMinute)
    {
        return factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:InvalidSignInPermitsPerMinute", permitsPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }
}
