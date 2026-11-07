// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Logbook.Core.Application.Applications.Create;
using Logbook.Sdk;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Npgsql;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Runs a real ASP.NET Core application with <c>Logbook.AspNetCore</c> against the real server, because what is tracked
///     depends on what the runtime and the database driver announce.
/// </summary>
/// <param name="factory">The Api under test.</param>
public sealed class AspNetCoreTrackingTests(LogbookApiFactory factory) : IClassFixture<LogbookApiFactory>
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARequest_IsStoredWithItsRouteStatusAndDuration()
    {
        var application = await CreateApplicationAsync();

        await using var host = await StartApplicationAsync(application);

        using var response = await host.GetAsync(new Uri("/orders/42?secret=abc", UriKind.Relative), Token);
        response.EnsureSuccessStatusCode();

        var span = Assert.Single(await WaitForSpansAsync(application, 1, "kind=Request"));

        Assert.Equal("GET /orders/{id}", span.GetProperty("name").GetString());
        Assert.Equal("/orders/42", span.GetProperty("data").GetString());
        Assert.Equal(200, span.GetProperty("statusCode").GetInt32());
        Assert.True(span.GetProperty("success").GetBoolean());
        Assert.True(span.GetProperty("durationMs").GetDouble() > 0);
        Assert.Equal("Testing", span.GetProperty("environment").GetString());
        Assert.Equal(32, span.GetProperty("traceId").GetString()!.Length);
    }

    [Fact]
    public async Task AFailedRequest_IsMarked_AndItsExceptionFormsAGroupInTheSameTrace()
    {
        var application = await CreateApplicationAsync();

        await using var host = await StartApplicationAsync(application);

        using var response = await host.GetAsync(new Uri("/fail", UriKind.Relative), Token);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var span = Assert.Single(await WaitForSpansAsync(application, 1, "kind=Request"));

        Assert.Equal("GET /fail", span.GetProperty("name").GetString());
        Assert.Equal(500, span.GetProperty("statusCode").GetInt32());
        Assert.False(span.GetProperty("success").GetBoolean());

        var traceId = span.GetProperty("traceId").GetString();
        var group = await WaitForExceptionGroupAsync(application);

        Assert.Equal("System.InvalidOperationException", group.GetProperty("exception").GetProperty("type").GetString());
        Assert.Equal(1, group.GetProperty("count").GetInt64());

        using var admin = factory.CreateAdminClient();
        var trace = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/traces/{traceId}", Token);

        Assert.Contains(
            trace.GetProperty("logEvents").EnumerateArray(),
            logEvent => logEvent.TryGetProperty("exceptionFingerprint", out var fingerprint)
                && fingerprint.GetString() == group.GetProperty("fingerprint").GetString());
    }

    [Fact]
    public async Task AMissingRoute_IsNotAFailure_AndHasNoRouteInItsName()
    {
        var application = await CreateApplicationAsync();

        await using var host = await StartApplicationAsync(application);

        using var response = await host.GetAsync(new Uri("/does/not/exist", UriKind.Relative), Token);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var span = Assert.Single(await WaitForSpansAsync(application, 1, "kind=Request"));

        Assert.Equal("GET (no route)", span.GetProperty("name").GetString());
        Assert.Equal("/does/not/exist", span.GetProperty("data").GetString());
        Assert.Equal(404, span.GetProperty("statusCode").GetInt32());
        Assert.True(span.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task AnExcludedPath_IsNotTracked()
    {
        var application = await CreateApplicationAsync();

        await using var host = await StartApplicationAsync(application, options => options.ExcludedRequestPaths.Add("/orders"));

        using var excluded = await host.GetAsync(new Uri("/orders/42", UriKind.Relative), Token);
        using var tracked = await host.GetAsync(new Uri("/ok", UriKind.Relative), Token);

        var span = Assert.Single(await WaitForSpansAsync(application, 1, "kind=Request"));

        Assert.Equal("GET /ok", span.GetProperty("name").GetString());
    }

    [Fact]
    public async Task AnExcludedPath_WithoutASlash_IsExcluded_AndABlankOne_ExcludesNothing()
    {
        var application = await CreateApplicationAsync();

        await using var host = await StartApplicationAsync(
            application,
            options =>
            {
                options.ExcludedRequestPaths.Add("orders");
                options.ExcludedRequestPaths.Add(string.Empty);
                options.ExcludedRequestPaths.Add("  ");
            });

        using var excluded = await host.GetAsync(new Uri("/orders/42", UriKind.Relative), Token);
        using var tracked = await host.GetAsync(new Uri("/ok", UriKind.Relative), Token);

        var span = Assert.Single(await WaitForSpansAsync(application, 1, "kind=Request"));

        Assert.Equal("GET /ok", span.GetProperty("name").GetString());
    }

    [Fact]
    public async Task AnHttpClientCall_IsADependencyOfTheRequest_InTheSameTrace_AndCarriesTheTraceOn()
    {
        var application = await CreateApplicationAsync();

        await using var host = await StartApplicationAsync(application);

        using var response = await host.GetAsync(new Uri("/call?target=/ok%3Fsecret%3Dabc", UriKind.Relative), Token);
        response.EnsureSuccessStatusCode();

        // The request, the call it made, and the request that call received in the same application.
        var requests = await WaitForSpansAsync(application, 2, "kind=Request");
        var outer = Assert.Single(requests, span => span.GetProperty("name").GetString() == "GET /call");
        var dependency = Assert.Single(await WaitForDependenciesAsync(application, outer));
        var inner = Assert.Single(requests, span => span.GetProperty("name").GetString() == "GET /ok");

        Assert.Equal(outer.GetProperty("traceId").GetString(), dependency.GetProperty("traceId").GetString());
        Assert.Equal(outer.GetProperty("traceId").GetString(), inner.GetProperty("traceId").GetString());
        Assert.Equal(outer.GetProperty("spanId").GetString(), dependency.GetProperty("parentSpanId").GetString());
        Assert.Equal(dependency.GetProperty("spanId").GetString(), inner.GetProperty("parentSpanId").GetString());

        Assert.Equal("HTTP", dependency.GetProperty("dependencyType").GetString());
        Assert.Equal(200, dependency.GetProperty("statusCode").GetInt32());
        Assert.True(dependency.GetProperty("success").GetBoolean());
        Assert.StartsWith("GET ", dependency.GetProperty("name").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("/ok", dependency.GetProperty("data").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedHttpClientCall_IsAFailedDependency()
    {
        var application = await CreateApplicationAsync();

        await using var host = await StartApplicationAsync(application);

        using var response = await host.GetAsync(new Uri("/call?target=/fail", UriKind.Relative), Token);

        var requests = await WaitForSpansAsync(application, 2, "kind=Request");
        var outer = Assert.Single(requests, span => span.GetProperty("name").GetString() == "GET /call");
        var dependency = Assert.Single(await WaitForDependenciesAsync(application, outer));

        Assert.False(dependency.GetProperty("success").GetBoolean());
        Assert.Equal(500, dependency.GetProperty("statusCode").GetInt32());
    }

    [Fact]
    public async Task Sending_DoesNotTrackItself()
    {
        var application = await CreateApplicationAsync();

        // Over a real socket to a real server, as a test handler would bypass what announces a request.
        await using var receiver = await StartReceiverAsync();
        await using var host = await StartApplicationAsync(application, endpoint: receiver.Address);

        using var response = await host.GetAsync(new Uri("/ok", UriKind.Relative), Token);
        response.EnsureSuccessStatusCode();

        await WaitUntilAsync(() => receiver.SpanRequests > 0);

        // Long enough for several flush intervals, which would each send another span if sending were tracked.
        await Task.Delay(TimeSpan.FromMilliseconds(500), Token);

        Assert.DoesNotContain(
            receiver.DependencySpans,
            span => span.TryGetProperty("data", out var data) && data.GetString()!.StartsWith(receiver.Address.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task SqlIsOnlyTrackedWhenAskedFor_AndItsTextOnlyWhenAskedFor()
    {
        var withoutSql = await CreateApplicationAsync();

        await using (var host = await StartApplicationAsync(withoutSql))
        {
            using var response = await host.GetAsync(new Uri("/sql", UriKind.Relative), Token);
            response.EnsureSuccessStatusCode();

            await WaitForSpansAsync(withoutSql, 1, "kind=Request");
        }

        // Other tests in this process run commands too, so only what this application tracked can tell.
        Assert.DoesNotContain(await SearchAsync(withoutSql, "kind=Dependency", Token), span => span.GetProperty("dependencyType").GetString() == "SQL");

        var withSql = await CreateApplicationAsync();

        await using (var host = await StartApplicationAsync(withSql, options => options.TrackSqlDependencies = true))
        {
            using var response = await host.GetAsync(new Uri("/sql", UriKind.Relative), Token);
            response.EnsureSuccessStatusCode();

            var request = Assert.Single(await WaitForSpansAsync(withSql, 1, "kind=Request"));
            var dependency = Assert.Single(await WaitForDependenciesAsync(withSql, request));

            Assert.Equal("SQL", dependency.GetProperty("dependencyType").GetString());
            Assert.True(dependency.GetProperty("success").GetBoolean());
            Assert.Equal(JsonValueKind.Null, dependency.GetProperty("data").ValueKind);
            Assert.Equal(request.GetProperty("spanId").GetString(), dependency.GetProperty("parentSpanId").GetString());
        }

        var withText = await CreateApplicationAsync();

        await using (var host = await StartApplicationAsync(withText, options =>
        {
            options.TrackSqlDependencies = true;
            options.CaptureSqlText = true;
        }))
        {
            using var response = await host.GetAsync(new Uri("/sql", UriKind.Relative), Token);
            response.EnsureSuccessStatusCode();

            var request = Assert.Single(await WaitForSpansAsync(withText, 1, "kind=Request"));
            var dependency = Assert.Single(await WaitForDependenciesAsync(withText, request));

            Assert.Contains("SELECT 1", dependency.GetProperty("data").GetString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task WithoutSettings_TheApplicationWorks_AndNothingIsSent()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddLogbookTelemetry();

        await using var app = builder.Build();
        app.MapGet("/ok", () => "ok");

        await app.StartAsync(Token);

        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
        using var response = await client.GetAsync(new Uri("/ok", UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(app.Services.GetRequiredService<LogbookClient>().IsEnabled);

        await app.StopAsync(Token);
    }

    private async Task<CreateApplicationResponse> CreateApplicationAsync()
    {
        using var admin = factory.CreateAdminClient();
        using var response = await admin.PostAsJsonAsync("/api/v1/applications", new { name = $"Tracking {Guid.NewGuid():N}" }, Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CreateApplicationResponse>(Token))!;
    }

    private async Task<RunningApplication> StartApplicationAsync(
        CreateApplicationResponse application,
        Action<Logbook.AspNetCore.LogbookTelemetryOptions>? configure = null,
        Uri? endpoint = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });

        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddHttpClient();

        // The real server runs in memory without an address, unless a test brings its own receiver.
        builder.Services.AddSingleton(_ => endpoint is null
            ? new LogbookClient(Options(factory.Server.BaseAddress, application), factory.Server.CreateHandler(), TimeProvider.System)
            : new LogbookClient(Options(endpoint, application)));

        builder.Services.AddLogbookTelemetry(configure);

        var app = builder.Build();

        app.MapGet("/orders/{id}", (int id) => Results.Ok(id));
        app.MapGet("/ok", () => "ok");
        app.MapGet("/fail", string () => throw new InvalidOperationException("A failure for the test."));

        app.MapGet("/call", async (string target, HttpContext context, IHttpClientFactory clients) =>
        {
            using var client = clients.CreateClient();
            using var response = await client.GetAsync(new Uri(app.Urls.First() + target), context.RequestAborted);

            return Results.Text(((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture));
        });

        app.MapGet("/sql", async (HttpContext context) =>
        {
            await using var connection = new NpgsqlConnection(factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("Database"));
            await connection.OpenAsync(context.RequestAborted);

            await using var command = new NpgsqlCommand("SELECT 1", connection);

            return Results.Ok(await command.ExecuteScalarAsync(context.RequestAborted));
        });

        await app.StartAsync(Token);

        return new RunningApplication(app);
    }

    private static LogbookOptions Options(Uri endpoint, CreateApplicationResponse application)
    {
        return new LogbookOptions
        {
            Endpoint = endpoint,
            ApiKey = application.ApiKey.Key,
            BatchSize = 10,
            FlushInterval = TimeSpan.FromMilliseconds(50),
            ShutdownTimeout = TimeSpan.FromSeconds(5)
        };
    }

    private static async Task<Receiver> StartReceiverAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        var receiver = new Receiver(app);

        app.MapPost("/api/v1/spans", async (HttpContext context) =>
        {
            using var body = new MemoryStream();
            await using var gzip = new System.IO.Compression.GZipStream(context.Request.Body, System.IO.Compression.CompressionMode.Decompress);
            await gzip.CopyToAsync(body, context.RequestAborted);

            using var document = JsonDocument.Parse(body.ToArray());
            receiver.Add(document.RootElement);

            return Results.Accepted();
        });

        app.MapPost("/api/v1/logs", () => Results.Accepted());

        await app.StartAsync(Token);

        receiver.Address = new Uri(app.Urls.First());

        return receiver;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(Token);
        limit.CancelAfter(WaitLimit);

        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), limit.Token);
        }
    }

    private async Task<JsonElement> WaitForExceptionGroupAsync(CreateApplicationResponse application)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(Token);
        limit.CancelAfter(WaitLimit);

        using var admin = factory.CreateAdminClient();

        while (true)
        {
            var groups = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/exceptions?applicationId={application.Id}", limit.Token);
            var items = groups.GetProperty("items");

            if (items.GetArrayLength() > 0)
            {
                return items[0];
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), limit.Token);
        }
    }

    private async Task<JsonElement[]> WaitForSpansAsync(CreateApplicationResponse application, int count, string query)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(Token);
        limit.CancelAfter(WaitLimit);

        while (true)
        {
            var items = await SearchAsync(application, query, limit.Token);

            if (items.Length >= count)
            {
                return items;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), limit.Token);
        }
    }

    // By the trace, as the listener sees everything in this process, including the commands of the server under test.
    private async Task<JsonElement[]> WaitForDependenciesAsync(CreateApplicationResponse application, JsonElement request)
    {
        return await WaitForSpansAsync(application, 1, $"kind=Dependency&traceId={request.GetProperty("traceId").GetString()}");
    }

    private async Task<JsonElement[]> SearchAsync(CreateApplicationResponse application, string query, CancellationToken cancellationToken)
    {
        using var admin = factory.CreateAdminClient();

        var page = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/spans?applicationId={application.Id}&{query}", cancellationToken);

        return [.. page.GetProperty("items").EnumerateArray()];
    }

    private sealed class RunningApplication(WebApplication app) : IAsyncDisposable
    {
        public HttpClient Client { get; } = new() { BaseAddress = new Uri(app.Urls.First()) };

        /// <summary>
        ///     Calls the application as an outside client would. The listener sees all of this process, so without the mark
        ///     this call would itself be tracked, and start the trace of the request.
        /// </summary>
        public async Task<HttpResponseMessage> GetAsync(Uri path, CancellationToken cancellationToken)
        {
            SendingFlow.Mark();

            return await Client.GetAsync(path, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();

            await app.StopAsync(CancellationToken.None);
            await app.DisposeAsync();
        }
    }

    private sealed class Receiver(WebApplication app) : IAsyncDisposable
    {
        private readonly List<JsonElement> _spans = [];
        private int _spanRequests;

        public Uri Address { get; set; } = null!;

        public int SpanRequests => Volatile.Read(ref _spanRequests);

        public IReadOnlyList<JsonElement> DependencySpans
        {
            get
            {
                lock (_spans)
                {
                    return [.. _spans.Where(span => span.GetProperty("kind").GetString() == "Dependency")];
                }
            }
        }

        public void Add(JsonElement batch)
        {
            lock (_spans)
            {
                _spans.AddRange(batch.EnumerateArray().Select(span => span.Clone()));
            }

            Interlocked.Increment(ref _spanRequests);
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync(CancellationToken.None);
            await app.DisposeAsync();
        }
    }
}
