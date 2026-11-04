// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Logbook.Core.Application.Applications.Create;
using Logbook.Core.Application.Applications.Get;
using Logbook.Core.Application.Applications.GetAll;
using Logbook.Core.Application.Applications.Rename;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks creating, listing, renaming and deleting Applications through the admin API.
/// </summary>
/// <param name="factory">The Api under test.</param>
public sealed class ApplicationTests(LogbookApiFactory factory) : IClassFixture<LogbookApiFactory>
{
    private const string Endpoint = "/api/v1/applications";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = factory.CreateAdminClient();

    [Fact]
    public async Task Create_ReturnsLocationAndTrimmedName()
    {
        var name = UniqueName();

        using var response = await _client.PostAsJsonAsync(
            Endpoint,
            new CreateApplicationRequest($"  {name}  "),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<CreateApplicationResponse>(TestContext.Current.CancellationToken);
        Assert.EndsWith($"{Endpoint}/{created!.Id}", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        var application = await GetAsync(created.Id);
        Assert.Equal(name, application.Name);
        Assert.Null(application.ModifiedAt);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"name":""}""")]
    [InlineData("""{"name":"   "}""")]
    public async Task Create_WithoutName_IsBadRequest(string body)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await _client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Create_WithOtherContentType_IsUnsupportedMediaType()
    {
        using var content = new StringContent("""{"name":"Shop"}""", Encoding.UTF8, "text/plain");

        using var response = await _client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithTooLongName_IsBadRequest()
    {
        using var response = await _client.PostAsJsonAsync(
            Endpoint,
            new CreateApplicationRequest(new string('a', 101)),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithTakenName_IsConflict()
    {
        var name = UniqueName();
        await CreateAsync(name);

        using var response = await _client.PostAsJsonAsync(
            Endpoint,
            new CreateApplicationRequest(name),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_ReturnsApplicationsOrderedByNameWithoutApiKeys()
    {
        var prefix = UniqueName();
        var second = await CreateAsync($"{prefix} b");
        var first = await CreateAsync($"{prefix} a");

        var json = await _client.GetStringAsync(Endpoint, TestContext.Current.CancellationToken);

        var ids = JsonSerializer.Deserialize<List<GetAllApplicationsResponse>>(json, JsonOptions)!
            .Where(application => application.Name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(application => application.Id);

        Assert.Equal([first.Id, second.Id], ids);
        Assert.DoesNotContain(first.ApiKey.Key, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_OfUnknownApplication_IsNotFoundProblem()
    {
        using var response = await _client.GetAsync($"{Endpoint}/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Rename_ChangesNameAndSetsModifiedAt()
    {
        var created = await CreateAsync(UniqueName());
        var newName = UniqueName();

        using var response = await RenameAsync(created.Id, newName);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var application = await GetAsync(created.Id);
        Assert.Equal(newName, application.Name);
        Assert.NotNull(application.ModifiedAt);
    }

    [Fact]
    public async Task Rename_ToCurrentName_ChangesNothing()
    {
        var name = UniqueName();
        var created = await CreateAsync(name);

        using var response = await RenameAsync(created.Id, name);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null((await GetAsync(created.Id)).ModifiedAt);
    }

    [Fact]
    public async Task Rename_ToNameOfOtherApplication_IsConflictAndKeepsName()
    {
        var takenName = UniqueName();
        var ownName = UniqueName();
        await CreateAsync(takenName);
        var created = await CreateAsync(ownName);

        using var response = await RenameAsync(created.Id, takenName);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ownName, (await GetAsync(created.Id)).Name);
    }

    [Fact]
    public async Task Rename_OfUnknownApplication_IsNotFound()
    {
        using var response = await RenameAsync(Guid.NewGuid(), UniqueName());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesApplication_AndFreesItsName()
    {
        var name = UniqueName();
        var created = await CreateAsync(name);

        using var response = await _client.DeleteAsync($"{Endpoint}/{created.Id}", TestContext.Current.CancellationToken);
        using var afterDelete = await _client.GetAsync($"{Endpoint}/{created.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);

        await CreateAsync(name);
    }

    [Fact]
    public async Task Delete_OfUnknownApplication_Succeeds()
    {
        using var response = await _client.DeleteAsync($"{Endpoint}/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static string UniqueName() => $"App {Guid.NewGuid():N}";

    private async Task<CreateApplicationResponse> CreateAsync(string name)
    {
        using var response = await _client.PostAsJsonAsync(
            Endpoint,
            new CreateApplicationRequest(name),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CreateApplicationResponse>(TestContext.Current.CancellationToken))!;
    }

    private async Task<GetApplicationResponse> GetAsync(Guid id)
    {
        var application = await _client.GetFromJsonAsync<GetApplicationResponse>(
            $"{Endpoint}/{id}",
            TestContext.Current.CancellationToken);

        return application!;
    }

    private async Task<HttpResponseMessage> RenameAsync(Guid id, string name)
    {
        return await _client.PutAsJsonAsync(
            $"{Endpoint}/{id}",
            new RenameApplicationRequest(name),
            TestContext.Current.CancellationToken);
    }
}
