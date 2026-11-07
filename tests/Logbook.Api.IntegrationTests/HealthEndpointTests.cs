// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks that the public health endpoints only return the overall status, and who can see the details.
/// </summary>
/// <param name="factory">The Api under test.</param>
public sealed class HealthEndpointTests(LogbookApiFactory factory) : IClassFixture<LogbookApiFactory>
{
    private const string DetailsEndpoint = "/health/details";
    private const string MonitoringKey = "integration-tests-monitoring-key-0123456789";

    private readonly HttpClient _client = factory.CreateClient();

    private WebApplicationFactory<Program> WithMonitoringKey => factory.WithWebHostBuilder(
        builder => builder.UseSetting("HealthDetails:MonitoringKey", MonitoringKey));

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    public async Task PublicEndpoint_ReturnsOnlyStatus(string endpoint)
    {
        using var response = await _client.GetAsync(endpoint, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var property = Assert.Single(body.RootElement.EnumerateObject());

        Assert.Equal("status", property.Name);
        Assert.Equal("Healthy", property.Value.GetString());
    }

    [Fact]
    public async Task Details_WithoutCredentials_AreUnauthorized_AndRevealNothing()
    {
        using var response = await _client.GetAsync(DetailsEndpoint, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Details_WithSession_ListAllChecks()
    {
        using var client = factory.CreateAdminClient();

        using var response = await client.GetAsync(DetailsEndpoint, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = body.RootElement;

        Assert.Equal("Healthy", root.GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("version").GetString()));

        var checks = root.GetProperty("checks").EnumerateArray().ToDictionary(check => check.GetProperty("name").GetString()!);

        Assert.Equal(["database", "migrations"], checks.Keys.Order());
        Assert.Equal("Healthy", checks["migrations"].GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(checks["migrations"].GetProperty("data").GetProperty("current").GetString()));
    }

    [Fact]
    public async Task Details_WithMonitoringKey_AreAllowed()
    {
        using var host = WithMonitoringKey;
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MonitoringKey);

        using var response = await client.GetAsync(DetailsEndpoint, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Details_WithWrongMonitoringKey_AreUnauthorized()
    {
        using var host = WithMonitoringKey;
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MonitoringKey + "x");

        using var response = await client.GetAsync(DetailsEndpoint, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Details_WithAnyBearerToken_AreUnauthorized_WhenNoMonitoringKeyIsConfigured()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", string.Empty.PadRight(40, 'x'));

        using var response = await client.GetAsync(DetailsEndpoint, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/applications")]
    [InlineData("/api/v1/logs")]
    public async Task MonitoringKey_GivesNoAccessToAnythingElse(string endpoint)
    {
        using var host = WithMonitoringKey;
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MonitoringKey);

        using var response = await client.GetAsync(endpoint, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("too-short")]
    [InlineData("0123456789012345678901234567890 1")]
    public void Startup_WithUnusableMonitoringKey_Fails(string key)
    {
        using var misconfigured = factory.WithWebHostBuilder(builder => builder.UseSetting("HealthDetails:MonitoringKey", key));

        Assert.Throws<OptionsValidationException>(() => misconfigured.CreateClient());
    }

    [Fact]
    public async Task UnknownApiRoute_ReturnsNotFound()
    {
        using var response = await _client.GetAsync("/api/does-not-exist", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
