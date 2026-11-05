// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks that a database that cannot be reached is reported as 503, which tells clients to send the request again.
/// </summary>
/// <param name="factory">The Api under test.</param>
public sealed class DatabaseUnavailableTests(LogbookApiFactory factory) : IClassFixture<LogbookApiFactory>
{
    private WebApplicationFactory<Program> Unavailable => factory.WithWebHostBuilder(builder => builder.UseSetting(
        "ConnectionStrings:Database",
        "Host=localhost;Port=1;Database=none;Username=none;Password=none;Timeout=2"));

    [Fact]
    public async Task SendingLogEvents_IsServiceUnavailable_BecauseTheApiKeyCannotBeChecked()
    {
        using var client = Unavailable.CreateClient();
        client.DefaultRequestHeaders.Add("X-Logbook-Key", "any-key");
        using var content = LogEventBodies.Json(LogEventBodies.Valid());

        using var response = await client.PostAsync("/api/v1/logs", content, TestContext.Current.CancellationToken);

        await AssertServiceUnavailableAsync(response);
    }

    [Theory]
    [InlineData("/api/v1/logs")]
    [InlineData("/api/v1/applications")]
    public async Task AdminRequests_AreServiceUnavailable(string endpoint)
    {
        using var client = Unavailable.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", LogbookApiFactory.AdminKey);

        using var response = await client.GetAsync(endpoint, TestContext.Current.CancellationToken);

        await AssertServiceUnavailableAsync(response);
    }

    [Fact]
    public async Task FailedLookups_DoNotCountAsInvalidApiKeys()
    {
        using var unavailable = Unavailable.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:InvalidApiKeyPermitsPerMinute", "1"));
        using var client = unavailable.CreateClient();
        client.DefaultRequestHeaders.Add("X-Logbook-Key", "any-key");

        for (var i = 0; i < 3; i++)
        {
            using var content = LogEventBodies.Json(LogEventBodies.Valid());
            using var response = await client.PostAsync("/api/v1/logs", content, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
    }

    private static async Task AssertServiceUnavailableAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TimeSpan.FromSeconds(5), response.Headers.RetryAfter?.Delta);

        // The address of the database stays hidden.
        Assert.DoesNotContain("localhost", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }
}
