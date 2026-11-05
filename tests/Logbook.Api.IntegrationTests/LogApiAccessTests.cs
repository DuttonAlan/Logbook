// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

using Logbook.Core.Application.Applications;
using Logbook.Core.Application.Applications.Create;
using Logbook.Core.Application.Applications.IssueApiKey;
using Logbook.Infrastructure.Persistence;

using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks at request level who can send Log Events, and that the API Key decides their Application.
/// </summary>
/// <param name="factory">The Api under test.</param>
public sealed class LogApiAccessTests(LogbookApiFactory factory) : IClassFixture<LogbookApiFactory>
{
    private const string Endpoint = "/api/v1/logs";
    private const string KeyHeader = "X-Logbook-Key";

    private readonly HttpClient _admin = factory.CreateAdminClient();

    [Fact]
    public async Task RequestWithoutApiKey_IsUnauthorized_EvenIfTheBodyIsInvalid()
    {
        using var client = factory.CreateClient();
        using var content = new StringContent("not json", Encoding.UTF8, "text/plain");

        using var response = await client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(KeyHeader, response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task RequestWithUnknownApiKey_IsUnauthorized()
    {
        using var response = await SendAsync("unknown-key");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RequestWithAdminKey_IsUnauthorized()
    {
        using var client = factory.CreateAdminClient();
        client.DefaultRequestHeaders.Add(KeyHeader, LogbookApiFactory.AdminKey);
        using var content = LogEventBodies.Json(LogEventBodies.Valid());

        using var response = await client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RequestWithApiKeyInBearerHeader_IsUnauthorized()
    {
        var application = await CreateApplicationAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", application.ApiKey.Key);
        using var content = LogEventBodies.Json(LogEventBodies.Valid());

        using var response = await client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminApi_WithApiKeyHeader_IsUnauthorized()
    {
        var application = await CreateApplicationAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(KeyHeader, application.ApiKey.Key);

        using var response = await client.GetAsync("/api/v1/applications", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RevokedApiKey_IsRejectedRightAway()
    {
        var application = await CreateApplicationAsync();

        using (var accepted = await SendAsync(application.ApiKey.Key))
        {
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        }

        using var revoked = await _admin.PostAsync(
            $"/api/v1/applications/{application.Id}/api-keys/{application.ApiKey.Id}/revoke",
            content: null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        using var response = await SendAsync(application.ApiKey.Key);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredApiKey_IsRejected()
    {
        var application = await CreateApplicationAsync();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        using var issued = await _admin.PostAsJsonAsync(
            $"/api/v1/applications/{application.Id}/api-keys",
            new IssueApiKeyRequest(expiresAt),
            TestContext.Current.CancellationToken);
        var apiKey = await issued.Content.ReadFromJsonAsync<IssuedApiKeyResponse>(TestContext.Current.CancellationToken);

        using (var accepted = await SendAsync(apiKey!.Key))
        {
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        }

        using var later = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.AddSingleton<TimeProvider>(new FixedTimeProvider(expiresAt.AddMinutes(1)))));
        using var client = later.CreateClient();
        client.DefaultRequestHeaders.Add(KeyHeader, apiKey.Key);
        using var content = LogEventBodies.Json(LogEventBodies.Valid());

        using var response = await client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApplicationOfTheLogEvent_ComesFromTheApiKey_NotFromTheBody()
    {
        var application = await CreateApplicationAsync();
        var other = await CreateApplicationAsync();
        var category = $"Spoof.{Guid.NewGuid():N}";
        var body = $$"""
            [{ "applicationId": "{{other.Id}}", "timestamp": "{{DateTimeOffset.UtcNow:O}}", "level": "Error", "message": "m", "category": "{{category}}" }]
            """;

        using var response = await SendAsync(application.ApiKey.Key, body);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LogbookContext>();
        var stored = await context.LogEvents.AsNoTracking().SingleAsync(
            logEvent => logEvent.Category == category,
            TestContext.Current.CancellationToken);

        Assert.Equal(application.Id, stored.ApplicationId);
    }

    private async Task<CreateApplicationResponse> CreateApplicationAsync()
    {
        using var response = await _admin.PostAsJsonAsync(
            "/api/v1/applications",
            new CreateApplicationRequest($"Log Access {Guid.NewGuid():N}"),
            TestContext.Current.CancellationToken);

        return (await response.Content.ReadFromJsonAsync<CreateApplicationResponse>(TestContext.Current.CancellationToken))!;
    }

    private async Task<HttpResponseMessage> SendAsync(string key, string? body = null)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(KeyHeader, key);
        using var content = LogEventBodies.Json(body ?? LogEventBodies.Valid());

        return await client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
