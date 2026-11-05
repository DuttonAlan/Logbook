// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

using Logbook.Core.Application.Applications;
using Logbook.Core.Application.Applications.Create;
using Logbook.Core.Application.Applications.Get;
using Logbook.Core.Application.Applications.IssueApiKey;
using Logbook.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks that API Keys are shown once when issued, stored only as hashes, and can be rotated and revoked.
/// </summary>
/// <param name="factory">The Api under test.</param>
public sealed class ApiKeyTests(LogbookApiFactory factory) : IClassFixture<LogbookApiFactory>
{
    private const string Endpoint = "/api/v1/applications";

    private readonly HttpClient _client = factory.CreateAdminClient();

    [Fact]
    public async Task CreatingApplication_IssuesFirstApiKeyWithoutExpiry_AndForbidsCaching()
    {
        using var response = await _client.PostAsJsonAsync(
            Endpoint,
            new CreateApplicationRequest(UniqueName()),
            TestContext.Current.CancellationToken);

        Assert.True(response.Headers.CacheControl?.NoStore);

        var created = await response.Content.ReadFromJsonAsync<CreateApplicationResponse>(TestContext.Current.CancellationToken);
        var apiKey = Assert.Single((await GetAsync(created!.Id)).ApiKeys);

        Assert.Equal(created.ApiKey.Id, apiKey.Id);
        Assert.Null(apiKey.ExpiresAt);
        Assert.True(apiKey.IsActive);
    }

    [Fact]
    public async Task ApiKey_IsStoredOnlyAsSha256Hash()
    {
        var created = await CreateAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LogbookContext>();
        var stored = await context.Applications
            .AsNoTracking()
            .Where(application => application.Id == created.Id)
            .SelectMany(application => application.ApiKeys)
            .SingleAsync(TestContext.Current.CancellationToken);

        var expected = Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(created.ApiKey.Key)));

        Assert.Equal(expected, stored.KeyHash.Value);
        Assert.NotEqual(created.ApiKey.Key, stored.KeyHash.Value);
    }

    [Fact]
    public async Task ApiKey_HasAtLeast256BitsAndDiffersOnEveryIssue()
    {
        var created = await CreateAsync();
        var issued = await IssueAsync(created.Id);

        Assert.Equal(32, Base64Url.DecodeFromChars(created.ApiKey.Key).Length);
        Assert.NotEqual(created.ApiKey.Key, issued.Key);
    }

    [Fact]
    public async Task ApiKey_IsNeverReturnedAgain()
    {
        var created = await CreateAsync();

        var json = await _client.GetStringAsync($"{Endpoint}/{created.Id}", TestContext.Current.CancellationToken);

        Assert.DoesNotContain(created.ApiKey.Key, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Issue_WithoutBody_KeepsExistingApiKeysActive()
    {
        var created = await CreateAsync();

        using var response = await _client.PostAsync($"{Endpoint}/{created.Id}/api-keys", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);

        var apiKeys = (await GetAsync(created.Id)).ApiKeys;
        Assert.Equal(2, apiKeys.Count);
        Assert.All(apiKeys, apiKey => Assert.True(apiKey.IsActive));
    }

    [Fact]
    public async Task Issue_WithExpiry_ReturnsItInUtc()
    {
        var created = await CreateAsync();
        var expiresAt = new DateTimeOffset(2099, 1, 1, 12, 0, 0, TimeSpan.FromHours(2));

        var issued = await IssueAsync(created.Id, new IssueApiKeyRequest(expiresAt));

        Assert.Equal(expiresAt.UtcDateTime, issued.ExpiresAt);
        Assert.Contains(
            (await GetAsync(created.Id)).ApiKeys,
            apiKey => apiKey.Id == issued.Id && apiKey.ExpiresAt == expiresAt.UtcDateTime && apiKey.IsActive);
    }

    [Fact]
    public async Task Issue_WithExpiryInThePast_IsBadRequest()
    {
        var created = await CreateAsync();

        using var response = await _client.PostAsJsonAsync(
            $"{Endpoint}/{created.Id}/api-keys",
            new IssueApiKeyRequest(DateTimeOffset.UtcNow.AddMinutes(-1)),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Issue_ForUnknownApplication_IsNotFound()
    {
        using var response = await _client.PostAsync($"{Endpoint}/{Guid.NewGuid()}/api-keys", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_DeactivatesOnlyThatApiKey()
    {
        var created = await CreateAsync();
        var other = await IssueAsync(created.Id);

        using var response = await RevokeAsync(created.Id, created.ApiKey.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var apiKeys = (await GetAsync(created.Id)).ApiKeys;
        var revoked = Assert.Single(apiKeys, apiKey => apiKey.Id == created.ApiKey.Id);
        var kept = Assert.Single(apiKeys, apiKey => apiKey.Id == other.Id);

        Assert.False(revoked.IsActive);
        Assert.NotNull(revoked.RevokedAt);
        Assert.True(kept.IsActive);
    }

    [Fact]
    public async Task Revoke_Twice_KeepsOriginalRevocationTime()
    {
        var created = await CreateAsync();

        using (await RevokeAsync(created.Id, created.ApiKey.Id))
        {
        }

        var firstRevocation = Assert.Single((await GetAsync(created.Id)).ApiKeys).RevokedAt;

        using var response = await RevokeAsync(created.Id, created.ApiKey.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(firstRevocation, Assert.Single((await GetAsync(created.Id)).ApiKeys).RevokedAt);
    }

    [Fact]
    public async Task Revoke_OfApiKeyOfOtherApplication_IsNotFoundAndKeepsItActive()
    {
        var created = await CreateAsync();
        var other = await CreateAsync();

        using var response = await RevokeAsync(created.Id, other.ApiKey.Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(Assert.Single((await GetAsync(other.Id)).ApiKeys).IsActive);
    }

    [Fact]
    public async Task Revoke_OfUnknownApplication_IsNotFound()
    {
        using var response = await RevokeAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeletingApplication_RemovesItsApiKeys()
    {
        var created = await CreateAsync();

        using var response = await _client.DeleteAsync($"{Endpoint}/{created.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LogbookContext>();

        Assert.False(await context.Database
            .SqlQuery<int>($"""SELECT 1 AS "Value" FROM config.api_keys WHERE "Id" = {created.ApiKey.Id}""")
            .AnyAsync(TestContext.Current.CancellationToken));
    }

    private static string UniqueName() => $"App {Guid.NewGuid():N}";

    private async Task<CreateApplicationResponse> CreateAsync()
    {
        using var response = await _client.PostAsJsonAsync(
            Endpoint,
            new CreateApplicationRequest(UniqueName()),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CreateApplicationResponse>(TestContext.Current.CancellationToken))!;
    }

    private async Task<IssuedApiKeyResponse> IssueAsync(Guid applicationId, IssueApiKeyRequest? request = null)
    {
        using var response = await _client.PostAsJsonAsync(
            $"{Endpoint}/{applicationId}/api-keys",
            request ?? new IssueApiKeyRequest(ExpiresAt: null),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<IssuedApiKeyResponse>(TestContext.Current.CancellationToken))!;
    }

    private async Task<GetApplicationResponse> GetAsync(Guid id)
    {
        var application = await _client.GetFromJsonAsync<GetApplicationResponse>(
            $"{Endpoint}/{id}",
            TestContext.Current.CancellationToken);

        return application!;
    }

    private async Task<HttpResponseMessage> RevokeAsync(Guid applicationId, Guid apiKeyId)
    {
        return await _client.PostAsync(
            $"{Endpoint}/{applicationId}/api-keys/{apiKeyId}/revoke",
            content: null,
            TestContext.Current.CancellationToken);
    }
}
