// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Logbook.Core.Application.Applications.Create;

using Microsoft.Extensions.Options;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks at request level who can call the admin API, and that Logbook does not start without a usable admin key.
/// </summary>
/// <param name="factory">The Api under test.</param>
public sealed class AdminApiAccessTests(LogbookApiFactory factory) : IClassFixture<LogbookApiFactory>
{
    private const string Endpoint = "/api/v1/applications";

    [Fact]
    public async Task RequestWithoutCredentials_IsUnauthorized()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Endpoint, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData(LogbookApiFactory.AdminKey + "x")]
    public async Task RequestWithWrongKey_IsUnauthorized(string key)
    {
        using var response = await SendWithBearerAsync(key);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer error=\"invalid_token\"", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task RequestWithAdminKeyInOtherScheme_IsUnauthorized()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", LogbookApiFactory.AdminKey);

        using var response = await client.GetAsync(Endpoint, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RequestWithApiKeyOfAnApplication_IsUnauthorized()
    {
        using var admin = factory.CreateAdminClient();
        using var created = await admin.PostAsJsonAsync(
            Endpoint,
            new CreateApplicationRequest($"Access {Guid.NewGuid():N}"),
            TestContext.Current.CancellationToken);
        var application = await created.Content.ReadFromJsonAsync<CreateApplicationResponse>(TestContext.Current.CancellationToken);

        using var response = await SendWithBearerAsync(application!.ApiKey.Key);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RequestWithAdminKey_Succeeds()
    {
        using var client = factory.CreateAdminClient();

        using var response = await client.GetAsync(Endpoint, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    public void Startup_WithoutUsableAdminKey_Fails(string key)
    {
        using var misconfigured = factory.WithWebHostBuilder(builder => builder.UseSetting("AdminApi:Key", key));

        Assert.Throws<OptionsValidationException>(() => misconfigured.CreateClient());
    }

    private async Task<HttpResponseMessage> SendWithBearerAsync(string key)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        return await client.GetAsync(Endpoint, TestContext.Current.CancellationToken);
    }
}
