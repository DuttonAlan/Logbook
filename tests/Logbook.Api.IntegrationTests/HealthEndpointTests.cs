// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net;
using System.Text.Json;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks that the public health endpoints only return the overall status.
/// </summary>
/// <param name="factory">The Api under test.</param>
public sealed class HealthEndpointTests(LogbookApiFactory factory) : IClassFixture<LogbookApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

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
    public async Task UnknownApiRoute_ReturnsNotFound()
    {
        using var response = await _client.GetAsync("/api/does-not-exist", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
