// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Logbook.Core.Abstractions;
using Logbook.Core.Application.Applications.Create;
using Logbook.Core.Application.LogEvents.Ingest;
using Logbook.Core.Application.LogEvents.Search;
using Logbook.Core.Application.LogEvents.Tail;
using Logbook.Core.Domain.Entities;
using Logbook.Infrastructure.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks receiving Log Events through the log endpoint, down to what is stored in PostgreSQL.
/// </summary>
/// <param name="factory">The Api under test.</param>
public sealed class LogIngestionTests(LogbookApiFactory factory) : IClassFixture<LogbookApiFactory>, IAsyncLifetime
{
    private const string Endpoint = "/api/v1/logs";

    private HttpClient _client = null!;
    private Guid _applicationId;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        using var admin = factory.CreateAdminClient();
        using var created = await admin.PostAsJsonAsync(
            "/api/v1/applications",
            new CreateApplicationRequest($"Ingestion {Guid.NewGuid():N}"),
            TestContext.Current.CancellationToken);
        var application = await created.Content.ReadFromJsonAsync<CreateApplicationResponse>(TestContext.Current.CancellationToken);

        _applicationId = application!.Id;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Logbook-Key", application.ApiKey.Key);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _client.Dispose();

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Post_StoresAllFields()
    {
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-1);
        var body = $$"""
            [{
              "timestamp": "{{timestamp:O}}",
              "level": "Error",
              "messageTemplate": "Order {OrderId} failed",
              "message": "Order 42 failed",
              "category": "Shop.Orders.OrderService",
              "environment": "Production",
              "eventId": 4001,
              "exception": { "type": "System.InvalidOperationException", "message": "m", "stackTrace": "s", "inner": null },
              "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
              "spanId": "00f067aa0ba902b7",
              "properties": { "OrderId": 42, "UserId": "abc" }
            }]
            """;

        using var response = await PostAsync(body);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<IngestLogEventsResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(1, accepted!.Accepted);

        var stored = Assert.Single(await WaitForStoredAsync(1));

        Assert.Equal(_applicationId, stored.ApplicationId);
        Assert.Equal(timestamp.UtcDateTime, stored.Timestamp, TimeSpan.FromMilliseconds(1));
        Assert.Equal(LogEventLevel.Error, stored.Level);
        Assert.Equal("Order {OrderId} failed", stored.MessageTemplate);
        Assert.Equal("Order 42 failed", stored.Message);
        Assert.Equal("Shop.Orders.OrderService", stored.Category);
        Assert.Equal("Production", stored.Environment);
        Assert.Equal(4001, stored.EventId);
        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", stored.TraceId);
        Assert.Equal("00f067aa0ba902b7", stored.SpanId);

        using var properties = JsonDocument.Parse(stored.Properties!);
        Assert.Equal(42, properties.RootElement.GetProperty("OrderId").GetInt32());
        using var exception = JsonDocument.Parse(stored.Exception!);
        Assert.Equal("System.InvalidOperationException", exception.RootElement.GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(""" "environment": null """)]
    [InlineData(""" "environment": "" """)]
    [InlineData(""" "environment": "   " """)]
    public async Task Post_WithoutEnvironment_StoresUnknown(string member)
    {
        using var response = await PostAsync(LogEventBodies.Valid(member));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(LogEvent.DefaultEnvironment, Assert.Single(await WaitForStoredAsync(1)).Environment);
    }

    [Fact]
    public async Task Post_WithEmptyMessage_IsAccepted()
    {
        // ILogger allows it, e.g. logger.LogError(exception, "").
        var body = $$"""[{ "timestamp": "{{DateTimeOffset.UtcNow:O}}", "level": "Error", "message": "" }]""";

        using var response = await PostAsync(body);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(string.Empty, Assert.Single(await WaitForStoredAsync(1)).Message);
    }

    [Fact]
    public async Task Post_WithOnlyRequiredFields_StoresNullForTheOthers()
    {
        using var response = await PostAsync(LogEventBodies.Valid());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var stored = Assert.Single(await WaitForStoredAsync(1));
        Assert.Null(stored.MessageTemplate);
        Assert.Null(stored.Category);
        Assert.Null(stored.EventId);
        Assert.Null(stored.Exception);
        Assert.Null(stored.TraceId);
        Assert.Null(stored.SpanId);
        Assert.Null(stored.Properties);
    }

    [Fact]
    public async Task Post_WithOffset_StoresTimestampInUtc()
    {
        var utc = DateTimeOffset.UtcNow.AddMinutes(-5);
        var body = $$"""[{ "timestamp": "{{utc.ToOffset(TimeSpan.FromHours(2)):O}}", "level": "Debug", "message": "m" }]""";

        using var response = await PostAsync(body);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(utc.UtcDateTime, Assert.Single(await WaitForStoredAsync(1)).Timestamp, TimeSpan.FromMilliseconds(1));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""[]""")]
    [InlineData("""[null]""")]
    [InlineData("""not json""")]
    [InlineData("""[{ "level": "Error", "message": "no timestamp" }]""")]
    [InlineData("""[{ "timestamp": "not a time", "level": "Error", "message": "m" }]""")]
    [InlineData("""[{ "timestamp": "2026-11-04T10:15:00Z", "message": "no level" }]""")]
    public async Task Post_WithInvalidBody_IsBadRequest(string body)
    {
        using var response = await PostAsync(body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(""" "level": "Fatal" """)]
    [InlineData(""" "level": 99 """)]
    public async Task Post_WithUnknownLevel_IsBadRequest(string level)
    {
        var body = $$"""[{ "timestamp": "{{DateTimeOffset.UtcNow:O}}", "message": "m", {{level}} }]""";

        using var response = await PostAsync(body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(""" "properties": [1, 2] """)]
    [InlineData(""" "properties": "text" """)]
    [InlineData(""" "exception": 42 """)]
    [InlineData(""" "properties": { "broken": "\ud800" } """)]
    [InlineData(""" "properties": { "\ud800": 1 } """)]
    public async Task Post_WithInvalidJsonField_IsBadRequest(string member)
    {
        using var response = await PostAsync(LogEventBodies.Valid(member));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithOneInvalidEvent_StoresNothing_AndNamesThePosition()
    {
        var now = DateTimeOffset.UtcNow;
        var body = $$"""
            [
              { "timestamp": "{{now:O}}", "level": "Error", "message": "valid" },
              { "timestamp": "{{now.AddYears(-1):O}}", "level": "Error", "message": "too old" }
            ]
            """;

        using var response = await PostAsync(body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Log Event 1", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        await AssertRejectedBatchWasNotStoredAsync();
    }

    [Theory]
    [InlineData(-8)]
    [InlineData(2)]
    public async Task Post_WithTimestampOutsideTheWindow_IsBadRequest(int days)
    {
        var body = $$"""[{ "timestamp": "{{DateTimeOffset.UtcNow.AddDays(days):O}}", "level": "Error", "message": "m" }]""";

        using var response = await PostAsync(body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithOtherContentType_IsUnsupportedMediaType()
    {
        using var content = new StringContent(LogEventBodies.Valid(), Encoding.UTF8, "text/plain");

        using var response = await _client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithGzip_IsDecompressed()
    {
        using var content = LogEventBodies.Gzip(LogEventBodies.Valid());

        using var response = await _client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Single(await WaitForStoredAsync(1));
    }

    [Fact]
    public async Task Post_WithInvalidGzip_IsBadRequest()
    {
        using var content = LogEventBodies.Json(LogEventBodies.Valid());
        content.Headers.ContentEncoding.Add("gzip");

        using var response = await _client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Post_WithOtherContentEncoding_IsUnsupportedMediaType()
    {
        using var content = LogEventBodies.Json(LogEventBodies.Valid());
        content.Headers.ContentEncoding.Add("br");

        using var response = await _client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Post_LargerThanTheLimit_IsPayloadTooLarge()
    {
        using var client = CreateClientWithRequestLimit(1024);
        using var content = LogEventBodies.Json(LogEventBodies.Valid($""" "category": "{new string('x', 2048)}" """));

        using var response = await client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Post_LargerThanTheLimit_WithoutContentLength_IsPayloadTooLarge()
    {
        using var client = CreateClientWithRequestLimit(1024);
        using var content = new UnknownLengthContent(LogEventBodies.Valid($""" "category": "{new string('x', 2048)}" """));
        Assert.Null(content.Headers.ContentLength);

        using var response = await client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Post_GzipThatExpandsBeyondTheLimit_IsPayloadTooLarge()
    {
        using var client = CreateClientWithRequestLimit(64 * 1024);
        using var content = LogEventBodies.Gzip(LogEventBodies.Valid($""" "category": "{new string('x', 10 * 1024 * 1024)}" """));
        Assert.True(content.Headers.ContentLength < 64 * 1024, "The compressed body must be below the limit.");

        using var response = await client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithEventLargerThanTheLimit_IsBadRequest()
    {
        // Default limit: 256 KiB per event. The message has its own limit, so the size comes from the properties.
        var member = $$""" "properties": { "large": "{{new string('x', 300 * 1024)}}" } """;

        using var response = await PostAsync(LogEventBodies.Valid(member));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertRejectedBatchWasNotStoredAsync();
    }

    [Theory]
    [InlineData("properties", false)]
    [InlineData("properties", true)]
    [InlineData("exception", false)]
    [InlineData("exception", true)]
    public async Task Post_WithJsonNestedDeeperThanTheLimit_IsBadRequest(string field, bool arrays)
    {
        const int Levels = LogEvent.MaxJsonDepth + 1;

        var value = arrays ? LogEventBodies.NestedArrays(Levels) : LogEventBodies.NestedObjects(Levels);

        using var response = await PostAsync(LogEventBodies.Valid($""" "{field}": {value} """));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertRejectedBatchWasNotStoredAsync();
    }

    [Fact]
    public async Task Post_WithMessageLongerThanTheLimit_IsBadRequest()
    {
        var body = $$"""[{ "timestamp": "{{DateTimeOffset.UtcNow:O}}", "level": "Error", "message": "{{new string('x', LogEvent.MessageMaxLength + 1)}}" }]""";

        using var response = await PostAsync(body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithLongestMessageOfDistinctWords_FitsTheFullTextIndex()
    {
        // The worst case for the size of a tsvector: as many different words as possible.
        var words = new StringBuilder();

        for (var word = 0; words.Length < LogEvent.MessageMaxLength - 8; word++)
        {
            words.Append(word.ToString("x", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
        }

        var body = $$"""[{ "timestamp": "{{DateTimeOffset.UtcNow:O}}", "level": "Error", "message": "{{words}}" }]""";

        using var response = await PostAsync(body);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Single(await WaitForStoredAsync(1));
    }

    [Fact]
    public async Task Post_WithValuesPostgresRejects_StoresThemInAReadableForm()
    {
        var body = $$"""
            [{
              "timestamp": "{{DateTimeOffset.UtcNow:O}}",
              "level": "Error",
              "message": "null\u0000character",
              "category": "cat\u0000egory",
              "properties": {
                "text": "a\u0000b",
                "key\u0000": 1,
                "nested": [{ "text": "\u0000" }],
                "huge": 1e999999,
                "tiny": 1e-20000,
                "long": 0.{{new string('1', 20000)}},
                "normal": 1.5e3
              }
            }]
            """;

        using var response = await PostAsync(body);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var stored = Assert.Single(await WaitForStoredAsync(1));
        Assert.Equal("null\uFFFDcharacter", stored.Message);
        Assert.Equal("cat\uFFFDegory", stored.Category);

        using var properties = JsonDocument.Parse(stored.Properties!);
        var root = properties.RootElement;
        Assert.Equal("a\uFFFDb", root.GetProperty("text").GetString());
        Assert.Equal(1, root.GetProperty("key\uFFFD").GetInt32());
        Assert.Equal("1e999999", root.GetProperty("huge").GetString());
        Assert.Equal("1e-20000", root.GetProperty("tiny").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("long").ValueKind);
        Assert.Equal(1500, root.GetProperty("normal").GetDouble());
    }

    [Fact]
    public async Task Post_AcrossDays_WritesEachLogEventIntoThePartitionOfItsUtcDay()
    {
        var today = DateTimeOffset.UtcNow;
        var yesterday = today.AddDays(-1);
        var fiveDaysAgo = today.AddDays(-5);
        var body = $$"""
            [
              { "timestamp": "{{today:O}}", "level": "Error", "message": "a" },
              { "timestamp": "{{yesterday:O}}", "level": "Error", "message": "b" },
              { "timestamp": "{{fiveDaysAgo:O}}", "level": "Error", "message": "c" }
            ]
            """;

        using var response = await PostAsync(body);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await WaitForStoredAsync(3);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LogbookContext>();
        var partitions = await context.Database
            .SqlQuery<string>($"""
                SELECT tableoid::regclass::text AS "Value" FROM telemetry.log_events
                WHERE "ApplicationId" = {_applicationId} ORDER BY "Timestamp"
                """)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [PartitionOf(fiveDaysAgo), PartitionOf(yesterday), PartitionOf(today)],
            partitions);
    }

    [Fact]
    public async Task EnsurePartitions_ConcurrentlyForTheSameDay_AllSucceed()
    {
        // One writer per server, but several servers can share a database.
        var calls = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<LogbookContext>();

            await context.Database.ExecuteSqlRawAsync(
                "SELECT telemetry.ensure_log_event_partitions(ARRAY[DATE '2021-03-01'])",
                TestContext.Current.CancellationToken);
        });

        await Task.WhenAll(calls);
    }

    [Fact]
    public async Task Post_ManyBatches_StoresEveryLogEventOnce()
    {
        var requests = Enumerable.Range(0, 20).Select(async _ =>
        {
            using var response = await PostAsync(LogEventBodies.Valid());

            return response.StatusCode;
        });

        var results = await Task.WhenAll(requests);

        Assert.All(results, status => Assert.Equal(HttpStatusCode.Accepted, status));
        Assert.Equal(20, (await WaitForStoredAsync(20)).Count);
    }

    [Fact]
    public async Task Shutdown_WritesWhatIsLeftInTheQueue()
    {
        // The interval keeps the Log Event in the queue until the host stops.
        var waiting = factory.WithWebHostBuilder(builder => builder.UseSetting("LogIngestion:WriteInterval", "01:00:00"));
        using var client = CreateClientWithHeadersOf(waiting);
        using var content = LogEventBodies.Json(LogEventBodies.Valid());

        using var response = await client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(await GetStoredAsync());

        await waiting.DisposeAsync();

        Assert.Single(await GetStoredAsync());
    }

    [Fact]
    public async Task Post_WhileTheQueueIsFull_IsServiceUnavailable_UntilLogEventsAreWritten()
    {
        var repository = new BlockedLogEventRepository();
        await using var blocked = factory.WithWebHostBuilder(builder => builder
            .UseSetting("LogIngestion:MaxQueuedBytes", "1")
            .ConfigureTestServices(services => services.AddSingleton<ILogEventRepository>(repository)));
        using var client = CreateClientWithHeadersOf(blocked);

        // An empty queue takes any batch.
        using (var content = LogEventBodies.Json(LogEventBodies.Valid()))
        using (var first = await client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        }

        using (var content = LogEventBodies.Json(LogEventBodies.Valid()))
        using (var second = await client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
            Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
            Assert.Equal(TimeSpan.FromSeconds(5), second.Headers.RetryAfter?.Delta);
        }

        repository.Unblock();

        HttpStatusCode status;

        do
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);

            using var content = LogEventBodies.Json(LogEventBodies.Valid());
            using var next = await client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);
            status = next.StatusCode;
        }
        while (status != HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Partitions_InheritAllIndexes()
    {
        var now = DateTimeOffset.UtcNow;
        using var response = await PostAsync(LogEventBodies.Valid());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await WaitForStoredAsync(1);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LogbookContext>();
        var table = $"log_events_{now:yyyyMMdd}";
        var indexes = await context.Database
            .SqlQuery<string>($"""SELECT indexdef AS "Value" FROM pg_indexes WHERE schemaname = 'telemetry' AND tablename = {table}""")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Contains(indexes, index => index.Contains("to_tsvector('simple'", StringComparison.Ordinal));
        Assert.Contains(indexes, index => index.Contains("\"Properties\" jsonb_path_ops", StringComparison.Ordinal));
        Assert.Contains(indexes, index => index.Contains("\"Exception\" jsonb_path_ops", StringComparison.Ordinal));
        Assert.Contains(indexes, index => index.Contains("UNIQUE", StringComparison.Ordinal));
        Assert.Contains(indexes, index => index.Contains("(\"ApplicationId\", \"Timestamp\", \"Id\")", StringComparison.Ordinal));
        Assert.Contains(indexes, index => index.Contains("(\"Environment\", \"Timestamp\", \"Id\")", StringComparison.Ordinal));
        Assert.Contains(indexes, index => index.Contains("WHERE (\"Level\" >= 3)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnsurePartitions_WorksForAUserWithoutRightsToChangeTheSchema()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LogbookContext>();
        var cancellationToken = TestContext.Current.CancellationToken;

        await context.Database.OpenConnectionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE ROLE logbook_application NOLOGIN;
            GRANT USAGE ON SCHEMA telemetry TO logbook_application;
            GRANT INSERT, SELECT ON telemetry.log_events TO logbook_application;
            SET ROLE logbook_application;
            """,
            cancellationToken);

        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync(
                "CREATE TABLE telemetry.forbidden (id int)",
                cancellationToken));

            await context.Database.ExecuteSqlRawAsync(
                "SELECT telemetry.ensure_log_event_partitions(ARRAY[DATE '2020-02-29'])",
                cancellationToken);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("RESET ROLE", CancellationToken.None);
        }

        var exists = await context.Database
            .SqlQuery<bool>($"""SELECT to_regclass('telemetry.log_events_20200229') IS NOT NULL AS "Value" """)
            .SingleAsync(cancellationToken);

        Assert.True(exists);
    }

    [Fact]
    public void Startup_WithInvalidLimits_Fails()
    {
        using var misconfigured = factory.WithWebHostBuilder(builder => builder.UseSetting("LogIngestion:MaxRequestBytes", "0"));

        Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => misconfigured.CreateClient());
    }

    [Fact]
    public void OpenApiDocument_DescribesTheApiKeyForLogs_AndTheSessionForApplications()
    {
        var provider = factory.Services.GetRequiredService<Swashbuckle.AspNetCore.Swagger.ISwaggerProvider>();

        var document = provider.GetSwagger("v1");

        var logs = document.Paths["/api/v1/logs"].Operations![System.Net.Http.HttpMethod.Post];
        var applications = document.Paths["/api/v1/applications"].Operations![System.Net.Http.HttpMethod.Get];

        Assert.Equal("ApiKey", Assert.Single(Assert.Single(logs.Security!)!.Keys).Reference.Id);
        Assert.Equal("Session", Assert.Single(Assert.Single(applications.Security!)!.Keys).Reference.Id);

        var search = document.Paths["/api/v1/logs"].Operations![System.Net.Http.HttpMethod.Get];
        Assert.Equal("Session", Assert.Single(Assert.Single(search.Security!)!.Keys).Reference.Id);
        Assert.Contains(search.Parameters!, parameter => parameter.Name == "level");
    }

    [Fact]
    public void OpenApiDocument_Documents503_OnEveryOperationThatUsesTheDatabase()
    {
        var provider = factory.Services.GetRequiredService<Swashbuckle.AspNetCore.Swagger.ISwaggerProvider>();

        var operations = provider.GetSwagger("v1").Paths
            .Where(path => path.Key.StartsWith("/api/", StringComparison.Ordinal))
            .SelectMany(path => path.Value.Operations!.Select(operation => (Name: $"{operation.Key} {path.Key}", operation.Value)))

            // The stream has answered 200 before it first uses the database, so it can only end, never answer 503.
            .Where(operation => operation.Name != "GET /api/v1/logs/stream")

            // These only look at the session cookie, and if its keys cannot be read, there is no session.
            .Where(operation => operation.Name is not ("GET /api/v1/auth/session" or "POST /api/v1/auth/sign-out"))
            .ToList();

        Assert.NotEmpty(operations);
        Assert.All(operations, operation => Assert.True(operation.Value.Responses!.ContainsKey("503"), operation.Name));
    }

    private static string PartitionOf(DateTimeOffset timestamp) => $"telemetry.log_events_{timestamp.UtcDateTime:yyyyMMdd}";

    private HttpClient CreateClientWithRequestLimit(long maxBytes)
    {
        var limited = factory.WithWebHostBuilder(builder => builder.UseSetting(
            "LogIngestion:MaxRequestBytes",
            maxBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)).UseSetting(
            "LogIngestion:MaxEventBytes",
            maxBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        return CreateClientWithHeadersOf(limited);
    }

    private HttpClient CreateClientWithHeadersOf(WebApplicationFactory<Program> application)
    {
        var client = application.CreateClient();

        foreach (var header in _client.DefaultRequestHeaders)
        {
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }

        return client;
    }

    /// <summary>
    ///     Proves that a rejected batch is not on its way either: it was rejected before a Log Event sent afterwards, so
    ///     it would be stored no later than that one.
    /// </summary>
    private async Task AssertRejectedBatchWasNotStoredAsync()
    {
        using var response = await PostAsync(LogEventBodies.Valid());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        Assert.Single(await WaitForStoredAsync(1));
    }

    private async Task<List<LogEvent>> WaitForStoredAsync(int count)
    {
        var stored = await GetStoredAsync();

        while (stored.Count < count)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);

            stored = await GetStoredAsync();
        }

        return stored;
    }

    private async Task<HttpResponseMessage> PostAsync(string body)
    {
        using var content = LogEventBodies.Json(body);

        return await _client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);
    }

    private async Task<List<LogEvent>> GetStoredAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LogbookContext>();

        return await context.LogEvents
            .AsNoTracking()
            .Where(logEvent => logEvent.ApplicationId == _applicationId)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     A repository whose writes wait until they are unblocked, so the queue stays full.
    /// </summary>
    private sealed class BlockedLogEventRepository : ILogEventRepository
    {
        private readonly TaskCompletionSource _unblocked = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Unblock() => _unblocked.TrySetResult();

        public Task AddRange(IReadOnlyCollection<LogEvent> logEvents, CancellationToken cancellationToken = default)
        {
            return _unblocked.Task.WaitAsync(cancellationToken);
        }

        public Task<IReadOnlyList<LogEvent>> Search(LogEventFilter filter, LogEventCursor? after, int take, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<LogEvent>> GetWritten(WrittenLogEvents written, LogEventFilter filter, int take, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<LogEvent>> GetByTrace(string traceId, int take, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<Logbook.Core.Application.ExceptionGroups.Search.ExceptionGroupResponse>> GetExceptionGroups(
            LogEventFilter filter,
            Logbook.Core.Application.ExceptionGroups.Search.ExceptionGroupSort sort,
            int take,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<long> DeleteByApplication(Guid applicationId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<long> DeleteExpired(Guid applicationId, int defaultRetentionDays, DateTime now, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<DateOnly?> DropOldestExpiredPartition(int defaultRetentionDays, DateTime now, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    ///     Content without a Content-Length, like a request sent in chunks.
    /// </summary>
    /// <param name="body">The JSON.</param>
    private sealed class UnknownLengthContent(string body) : HttpContent
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(body);

        protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            stream.Write(_bytes);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return stream.WriteAsync(_bytes).AsTask();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;

            return false;
        }
    }
}
