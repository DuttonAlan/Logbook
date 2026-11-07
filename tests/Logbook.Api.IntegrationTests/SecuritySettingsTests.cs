// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net;
using System.Net.Http.Json;

using Logbook.Api.Authentication;
using Logbook.Api.Security;
using Logbook.Core.Abstractions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks the security headers, the handling of a reverse proxy, and the refusal to start with insecure settings.
/// </summary>
/// <param name="factory">The Api under test.</param>
public sealed class SecuritySettingsTests(LogbookApiFactory factory) : IClassFixture<LogbookApiFactory>
{
    private const string SignInEndpoint = "/api/v1/auth/sign-in";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/health")]
    [InlineData("/api/does-not-exist")]
    [InlineData("/api/v1/applications")]
    public async Task EveryResponse_HasTheSecurityHeaders(string endpoint)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(endpoint, Token);

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));

        var policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));

        Assert.Contains("default-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe", policy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ErrorResponses_KeepTheSecurityHeaders()
    {
        // A bug in an endpoint: the exception handler removes the headers that were set so far.
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(
            services => services.AddTransient<IApplicationRepository>(_ => throw new InvalidOperationException("A bug."))));
        using var client = host.CreateAdminClient();

        using var response = await client.GetAsync("/api/v1/applications", Token);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task BehindATrustedProxy_TheCookieIsSecure_BecauseTheClientUsesHttps()
    {
        using var host = BehindProxy("127.0.0.1", enabled: true);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        using var response = await SignInAsync(client, LogbookApiFactory.AdminPassword);

        Assert.Contains("secure", Assert.Single(response.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("203.0.113.5", true)]
    [InlineData("127.0.0.1", false)]
    public async Task ForwardedHeaders_AreIgnored_FromOthersThanATrustedProxy_OrWhenTurnedOff(string remoteAddress, bool enabled)
    {
        using var host = BehindProxy(remoteAddress, enabled);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        using var response = await SignInAsync(client, LogbookApiFactory.AdminPassword);

        Assert.DoesNotContain("secure", Assert.Single(response.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BehindATrustedProxy_ClientsHaveTheirOwnLimits()
    {
        using var host = BehindProxy("127.0.0.1", enabled: true, ("RateLimiting:InvalidSignInPermitsPerMinute", "1"));
        using var first = host.CreateClient();
        using var second = host.CreateClient();
        first.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.1");
        second.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.2");

        using var firstAttempt = await SignInAsync(first, "wrong-password-1");
        using var firstAgain = await SignInAsync(first, "wrong-password-2");
        using var secondAttempt = await SignInAsync(second, "wrong-password-3");

        Assert.Equal(HttpStatusCode.Unauthorized, firstAttempt.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, firstAgain.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, secondAttempt.StatusCode);
    }

    [Fact]
    public async Task KnownNetwork_IsTrusted_InsteadOfTheProxyOnTheSameHost()
    {
        using var host = BehindProxy("172.18.0.2", enabled: true, ("ReverseProxy:KnownNetworks:0", "172.18.0.0/16"));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        using var response = await SignInAsync(client, LogbookApiFactory.AdminPassword);

        Assert.Contains("secure", Assert.Single(response.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ReverseProxy:KnownProxies:0", "not an address")]
    [InlineData("ReverseProxy:KnownNetworks:0", "172.18.0.0")]
    [InlineData("ReverseProxy:ForwardLimit", "0")]
    public void Startup_WithUnusableProxySettings_Fails(string setting, string value)
    {
        using var misconfigured = factory.WithWebHostBuilder(builder => builder.UseSetting(setting, value));

        Assert.Throws<OptionsValidationException>(() => misconfigured.CreateClient());
    }

    [Fact]
    public void Startup_InProduction_WithoutAllowedHosts_Fails()
    {
        using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        var exception = Assert.Throws<OptionsValidationException>(() => production.CreateClient());

        Assert.Contains("AllowedHosts", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_InProduction_WithGoodSettings_Succeeds()
    {
        using var production = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("AllowedHosts", "localhost");

            // The container of the tests uses a default password, which production refuses. Liveness needs no database.
            builder.UseSetting("ConnectionStrings:Database", "Host=localhost;Port=1;Database=none;Username=none;Password=a-good-password;Timeout=2");
        });
        using var client = production.CreateClient();

        using var response = await client.GetAsync("/health/live", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(InsecureSettings))]
    public void Validator_InProduction_RefusesInsecureSettings(string expectedInMessage, Dictionary<string, string?> settings)
    {
        var result = Validate("Production", settings);

        Assert.True(result.Failed);
        Assert.Contains(expectedInMessage, result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AcceptableSettings))]
    public void Validator_InProduction_AcceptsSecureSettings(Dictionary<string, string?> settings)
    {
        Assert.True(Validate("Production", settings).Succeeded);
    }

    [Fact]
    public void Validator_OnlyChecksProduction()
    {
        var settings = new Dictionary<string, string?> { ["AllowedHosts"] = "*", ["SignIn:Administrator:Password"] = ProductionSettingsValidator.DevelopmentPassword };

        Assert.True(Validate("Development", settings).Succeeded);
    }

    [Fact]
    public void Validator_DoesNotRevealTheDatabasePassword()
    {
        var result = Validate("Production", new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "localhost",
            ["ConnectionStrings:Database"] = "Host=db;Password=postgres"
        });

        Assert.True(result.Failed);
        Assert.DoesNotContain("Password=postgres", result.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Settings that must not start, with a word of the message that explains why.
    /// </summary>
    /// <returns>The test cases.</returns>
    public static TheoryData<string, Dictionary<string, string?>> InsecureSettings()
    {
        return new TheoryData<string, Dictionary<string, string?>>
        {
            { "AllowedHosts", new() },
            { "AllowedHosts", new() { ["AllowedHosts"] = "*" } },
            { "AllowedHosts", new() { ["AllowedHosts"] = "logbook.example.com;*" } },
            { "in the clear", new() { ["AllowedHosts"] = "logbook.example.com" } },
            { "in the clear", new() { ["AllowedHosts"] = "logbook.example.com", ["urls"] = "http://+:8080" } },
            { "in the clear", new() { ["AllowedHosts"] = "logbook.example.com", ["urls"] = "http://+:8080;https://+:8443" } },
            { "in the clear", new() { ["AllowedHosts"] = "logbook.example.com", ["http_ports"] = "8080", ["https_ports"] = "8443" } },
            { "in the clear", new() { ["AllowedHosts"] = "logbook.example.com", ["Kestrel:Endpoints:A:Url"] = "http://+:8080", ["Kestrel:Endpoints:B:Url"] = "https://+:8443" } },
            { "in the clear", new() { ["AllowedHosts"] = "logbook.example.com", ["urls"] = "http://localhost:5000" } },
            { "development", new() { ["AllowedHosts"] = "localhost", ["SignIn:Administrator:Password"] = ProductionSettingsValidator.DevelopmentPassword } },
            { "well-known", new() { ["AllowedHosts"] = "localhost", ["ConnectionStrings:Database"] = "Host=db;Username=logbook;Password=12345" } },
            { "trusts every address", new() { ["AllowedHosts"] = "localhost", ["ReverseProxy:KnownNetworks:0"] = "0.0.0.0/0" } },
            { "trusts every address", new() { ["AllowedHosts"] = "localhost", ["ReverseProxy:KnownNetworks:0"] = "::/0" } }
        };
    }

    /// <summary>
    ///     Settings that must start.
    /// </summary>
    /// <returns>The test cases.</returns>
    public static TheoryData<Dictionary<string, string?>> AcceptableSettings()
    {
        return new TheoryData<Dictionary<string, string?>>
        {
            new Dictionary<string, string?> { ["AllowedHosts"] = "localhost;127.0.0.1" },
            new Dictionary<string, string?> { ["AllowedHosts"] = "logbook.example.com", ["ReverseProxy:Enabled"] = "true", ["ReverseProxy:KnownNetworks:0"] = "172.18.0.0/16" },
            new Dictionary<string, string?> { ["AllowedHosts"] = "logbook.example.com", ["urls"] = "https://+:443" },
            new Dictionary<string, string?> { ["AllowedHosts"] = "logbook.example.com", ["https_ports"] = "443" },
            new Dictionary<string, string?> { ["AllowedHosts"] = "logbook.example.com", ["Kestrel:Endpoints:Https:Url"] = "https://+:443" },
            new Dictionary<string, string?> { ["AllowedHosts"] = "logbook.example.com", ["urls"] = "https://+:443;http://localhost:5000" },
            new Dictionary<string, string?> { ["AllowedHosts"] = "logbook.example.com", ["http_ports"] = string.Empty, ["https_ports"] = "443" },
            new Dictionary<string, string?> { ["AllowedHosts"] = "logbook.example.com", ["http_ports"] = "8080", ["urls"] = "https://+:443" },
            new Dictionary<string, string?> { ["AllowedHosts"] = "logbook.example.com", ["urls"] = "http://+:8080", ["Kestrel:Endpoints:Https:Url"] = "https://+:443" },
            new Dictionary<string, string?> { ["AllowedHosts"] = "logbook.internal", ["Security:AllowInsecureHttp"] = "true" },
            new Dictionary<string, string?> { ["AllowedHosts"] = "logbook.example.com", ["ReverseProxy:Enabled"] = "true", ["http_ports"] = "8080" },
            new Dictionary<string, string?> { ["AllowedHosts"] = "localhost", ["ConnectionStrings:Database"] = "Host=db;Username=logbook;Password=a-good-password" }
        };
    }

    private static ValidateOptionsResult Validate(string environment, Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new ProductionSettingsValidator(new FakeEnvironment(environment), configuration).Validate(
            name: null,
            configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions());
    }

    private static Task<HttpResponseMessage> SignInAsync(HttpClient client, string password)
    {
        return client.PostAsJsonAsync(SignInEndpoint, new SignInRequest(LogbookApiFactory.AdminUserName, password), Token);
    }

    private WebApplicationFactory<Program> BehindProxy(string remoteAddress, bool enabled, params (string Setting, string Value)[] settings)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ReverseProxy:Enabled", enabled ? "true" : "false");

            foreach (var (setting, value) in settings)
            {
                builder.UseSetting(setting, value);
            }

            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new RemoteAddress(IPAddress.Parse(remoteAddress))));
        });
    }

    /// <summary>
    ///     The test server has no connection, so this sets the address that the next hop would have.
    /// </summary>
    private sealed class RemoteAddress(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use((context, nextMiddleware) =>
                {
                    context.Connection.RemoteIpAddress = address;

                    return nextMiddleware();
                });

                next(app);
            };
        }
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Logbook";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
