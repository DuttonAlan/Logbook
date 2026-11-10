// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net;
using System.Net.Http.Json;

using Logbook.Core.Application.SavedSearches;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks the endpoints for saved searches, against a real database.
/// </summary>
/// <param name="factory">The Api under test.</param>
public sealed class SavedSearchTests(LogbookApiFactory factory) : IClassFixture<LogbookApiFactory>, IDisposable
{
    private const string Path = "/api/v1/saved-searches";

    private readonly HttpClient _admin = factory.CreateAdminClient();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <inheritdoc/>
    public void Dispose() => _admin.Dispose();

    [Fact]
    public async Task ASavedSearch_CanBeCreated_Listed_Changed_AndDeleted()
    {
        var name = $"Errors {Guid.NewGuid():N}";

        using var created = await _admin.PostAsJsonAsync(Path, new { name = $"  {name}  ", query = "?level=Error&range=24h" }, Token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var savedSearch = (await created.Content.ReadFromJsonAsync<SavedSearchResponse>(Token))!;
        Assert.Equal(name, savedSearch.Name);
        Assert.Equal("level=Error&range=24h", savedSearch.Query);
        Assert.Null(savedSearch.ModifiedAt);
        Assert.Equal($"/api/v1/saved-searches/{savedSearch.Id}", created.Headers.Location?.OriginalString);

        Assert.Contains(await GetAllAsync(), item => item.Id == savedSearch.Id && item.Query == savedSearch.Query);

        using var changed = await _admin.PutAsJsonAsync($"{Path}/{savedSearch.Id}", new { name = $"{name} (EU)", query = "level=Error&env=EU" }, Token);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        var after = (await changed.Content.ReadFromJsonAsync<SavedSearchResponse>(Token))!;
        Assert.Equal($"{name} (EU)", after.Name);
        Assert.Equal("level=Error&env=EU", after.Query);
        Assert.NotNull(after.ModifiedAt);

        using var deleted = await _admin.DeleteAsync(new Uri($"{Path}/{savedSearch.Id}", UriKind.Relative), Token);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Assert.DoesNotContain(await GetAllAsync(), item => item.Id == savedSearch.Id);

        // Deleting again also succeeds.
        using var again = await _admin.DeleteAsync(new Uri($"{Path}/{savedSearch.Id}", UriKind.Relative), Token);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
    }

    [Fact]
    public async Task TheSameName_IsAConflict()
    {
        var name = $"Duplicate {Guid.NewGuid():N}";

        using var first = await _admin.PostAsJsonAsync(Path, new { name, query = "q=a" }, Token);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var second = await _admin.PostAsJsonAsync(Path, new { name, query = "q=b" }, Token);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        using var other = await _admin.PostAsJsonAsync(Path, new { name = $"{name} 2", query = "q=b" }, Token);
        var otherSearch = (await other.Content.ReadFromJsonAsync<SavedSearchResponse>(Token))!;

        using var renamed = await _admin.PutAsJsonAsync($"{Path}/{otherSearch.Id}", new { name, query = "q=b" }, Token);
        Assert.Equal(HttpStatusCode.Conflict, renamed.StatusCode);
    }

    [Theory]
    [InlineData("", "q=a")]
    [InlineData("   ", "q=a")]
    [InlineData("Name", "q=with space")]
    [InlineData("Name", "q=a#fragment")]
    [InlineData("Line\nbreak", "q=a")]
    public async Task AnInvalidSavedSearch_IsRejected(string name, string query)
    {
        using var response = await _admin.PostAsJsonAsync(Path, new { name, query }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ATooLongQuery_IsRejected()
    {
        using var response = await _admin.PostAsJsonAsync(Path, new { name = $"Long {Guid.NewGuid():N}", query = new string('a', 4001) }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangingASavedSearchThatDoesNotExist_IsNotFound()
    {
        using var response = await _admin.PutAsJsonAsync($"{Path}/{Guid.NewGuid()}", new { name = "Missing", query = "q=a" }, Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WithoutASession_IsUnauthorized()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri(Path, UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<List<SavedSearchResponse>> GetAllAsync()
    {
        return (await _admin.GetFromJsonAsync<List<SavedSearchResponse>>(Path, Token))!;
    }
}
