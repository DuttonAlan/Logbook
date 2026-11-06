// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Logbook.Core.Application.Applications.Create;
using Logbook.Core.Domain.Entities;
using Logbook.Infrastructure.Ingestion;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks following new Log Events through the stream endpoint.
/// </summary>
/// <param name="factory">The Api under test.</param>
public sealed class LogTailTests(LogbookApiFactory factory) : IClassFixture<LogbookApiFactory>, IAsyncLifetime
{
    private const string Endpoint = "/api/v1/logs/stream";

    private readonly HttpClient _admin = factory.CreateAdminClient();

    private HttpClient _sender = null!;
    private Guid _applicationId;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        using var response = await _admin.PostAsJsonAsync(
            "/api/v1/applications",
            new CreateApplicationRequest($"Tail {Guid.NewGuid():N}"),
            TestContext.Current.CancellationToken);

        var application = (await response.Content.ReadFromJsonAsync<CreateApplicationResponse>(TestContext.Current.CancellationToken))!;

        _applicationId = application.Id;
        _sender = factory.CreateClient();
        _sender.DefaultRequestHeaders.Add("X-Logbook-Key", application.ApiKey.Key);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _sender.Dispose();
        _admin.Dispose();

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Stream_WithoutAdminKey_IsUnauthorized()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Endpoint, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Stream_WithApiKey_IsUnauthorized_BecauseAnApiKeyCanOnlySend()
    {
        using var response = await _sender.GetAsync(Endpoint, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("level=Fatal")]
    [InlineData("property=")]
    [InlineData("property=NoSeparator")]
    [InlineData("applicationId=not-a-guid")]
    public async Task Stream_WithInvalidParameter_IsBadRequest(string query)
    {
        using var response = await _admin.GetAsync($"{Endpoint}?{query}", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Stream_SendsNewLogEvents_InTheFormatOfTheSearch()
    {
        await using var stream = await OpenAsync(_admin, $"applicationId={_applicationId}");

        Assert.Equal("text/event-stream", stream.ContentType);
        Assert.Equal("no", Assert.Single(stream.Headers.GetValues("X-Accel-Buffering")));
        Assert.Equal("connected", (await stream.ReadAsync()).Name);

        await SendAsync($$"""
            [{ "timestamp": "{{DateTimeOffset.UtcNow:O}}", "level": "Error", "message": "Order 42 failed", "properties": { "OrderId": 42 } }]
            """);

        var received = await stream.ReadAsync();

        Assert.Equal("log", received.Name);

        using var document = JsonDocument.Parse(received.Data);

        Assert.Equal(_applicationId, document.RootElement.GetProperty("applicationId").GetGuid());
        Assert.Equal("Error", document.RootElement.GetProperty("level").GetString());
        Assert.Equal("Order 42 failed", document.RootElement.GetProperty("message").GetString());
        Assert.Equal(42, document.RootElement.GetProperty("properties").GetProperty("OrderId").GetInt32());
    }

    [Fact]
    public async Task Stream_AppliesTheFiltersLikeTheSearch()
    {
        var query = $"applicationId={_applicationId}&level=Error&environment=Production&text=disk%20-ignored&traceId=aaaa&property=OrderId%3D42";

        await using var stream = await OpenAsync(_admin, query);
        await stream.ReadAsync();

        var now = DateTimeOffset.UtcNow;
        const string Match = """ "environment": "Production", "traceId": "aaaa", "properties": { "OrderId": 42 } """;

        await SendAsync($$"""
            [
              { "timestamp": "{{now:O}}", "level": "Warning", "message": "disk level", {{Match}} },
              { "timestamp": "{{now:O}}", "level": "Error", "message": "disk ignored", {{Match}} },
              { "timestamp": "{{now:O}}", "level": "Error", "message": "other text", {{Match}} },
              { "timestamp": "{{now:O}}", "level": "Error", "message": "disk environment", "environment": "Staging", "traceId": "aaaa", "properties": { "OrderId": 42 } },
              { "timestamp": "{{now:O}}", "level": "Error", "message": "disk trace", "environment": "Production", "traceId": "bbbb", "properties": { "OrderId": 42 } },
              { "timestamp": "{{now:O}}", "level": "Error", "message": "disk property", "environment": "Production", "traceId": "aaaa", "properties": { "OrderId": 43 } },
              { "timestamp": "{{now:O}}", "level": "Error", "message": "disk match", {{Match}} }
            ]
            """);

        using var document = JsonDocument.Parse((await stream.ReadAsync()).Data);

        Assert.Equal("disk match", document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Stream_SendsLogEventsThatArriveLate()
    {
        await using var stream = await OpenAsync(_admin, $"applicationId={_applicationId}");
        await stream.ReadAsync();

        await SendAsync($$"""
            [{ "timestamp": "{{DateTimeOffset.UtcNow.AddDays(-2):O}}", "level": "Error", "message": "from two days ago" }]
            """);

        using var document = JsonDocument.Parse((await stream.ReadAsync()).Data);

        Assert.Equal("from two days ago", document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Stream_SendsOneBatch_OldestFirst()
    {
        await using var stream = await OpenAsync(_admin, $"applicationId={_applicationId}");
        await stream.ReadAsync();

        var now = DateTimeOffset.UtcNow;

        await SendAsync($$"""
            [
              { "timestamp": "{{now:O}}", "level": "Error", "message": "newer" },
              { "timestamp": "{{now.AddSeconds(-1):O}}", "level": "Error", "message": "older" }
            ]
            """);

        using var first = JsonDocument.Parse((await stream.ReadAsync()).Data);
        using var second = JsonDocument.Parse((await stream.ReadAsync()).Data);

        Assert.Equal("older", first.RootElement.GetProperty("message").GetString());
        Assert.Equal("newer", second.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Stream_Ends_WhenTheServerStops_SoItDoesNotHoldUpTheShutdown()
    {
        using var server = factory.WithWebHostBuilder(_ => { });
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LogbookApiFactory.AdminKey);

        await using var stream = await OpenAsync(client, string.Empty);
        await stream.ReadAsync();

        server.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();

        await stream.WaitForEndAsync();
    }

    [Fact]
    public async Task Feed_TellsASlowFollowerAboutSkippedBatches()
    {
        var feed = new LogEventFeed(TimeProvider.System);
        using var subscription = feed.Subscribe();

        for (var i = 0; i < 100; i++)
        {
            feed.Publish([CreateLogEvent()]);
        }

        var first = await subscription.ReadAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var second = await subscription.ReadAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(first!.FollowsGap);
        Assert.False(second!.FollowsGap);
    }

    [Fact]
    public async Task Feed_OnTimeout_ReturnsNothing()
    {
        var feed = new LogEventFeed(TimeProvider.System);
        using var subscription = feed.Subscribe();

        Assert.Null(await subscription.ReadAsync(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Feed_WhenCancelled_Throws()
    {
        var feed = new LogEventFeed(TimeProvider.System);
        using var subscription = feed.Subscribe();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await subscription.ReadAsync(TimeSpan.FromMinutes(1), new CancellationToken(canceled: true)));
    }

    private static LogEvent CreateLogEvent() => LogEvent.Create(Guid.NewGuid(), DateTime.UtcNow, LogEventLevel.Error, "message");

    private static async Task<EventStream> OpenAsync(HttpClient client, string query)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Endpoint}?{query}");

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return new EventStream(response, await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
    }

    private async Task SendAsync(string body)
    {
        using var content = LogEventBodies.Json(body);
        using var response = await _sender.PostAsync("/api/v1/logs", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private sealed record ServerSentEvent(string Name, string Data);

    private sealed class EventStream(HttpResponseMessage response, Stream body) : IAsyncDisposable
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private readonly StreamReader _reader = new(body);

        public string? ContentType => response.Content.Headers.ContentType?.MediaType;

        public HttpResponseHeaders Headers => response.Headers;

        public async Task<ServerSentEvent> ReadAsync()
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(Timeout);

            var (name, data) = (string.Empty, string.Empty);

            while (await _reader.ReadLineAsync(timeout.Token) is { } line)
            {
                if (line.StartsWith("event: ", StringComparison.Ordinal))
                {
                    name = line["event: ".Length..];
                }
                else if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    data = line["data: ".Length..];
                }
                else if (line.Length == 0 && name != "heartbeat")
                {
                    return new ServerSentEvent(name, data);
                }
            }

            throw new InvalidOperationException("The stream ended.");
        }

        public async Task WaitForEndAsync()
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(Timeout);

            while (await _reader.ReadLineAsync(timeout.Token) is not null)
            {
            }
        }

        public ValueTask DisposeAsync()
        {
            _reader.Dispose();
            response.Dispose();

            return ValueTask.CompletedTask;
        }
    }
}
