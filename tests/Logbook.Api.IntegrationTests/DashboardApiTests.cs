// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Logbook.Core.Application.Applications.Create;
using Logbook.Sdk;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks the endpoints for custom metrics and the overview, against a real database.
/// </summary>
/// <param name="factory">The Api under test.</param>
public sealed class DashboardApiTests(LogbookApiFactory factory) : IClassFixture<LogbookApiFactory>, IDisposable
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    private readonly HttpClient _admin = factory.CreateAdminClient();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <inheritdoc/>
    public void Dispose() => _admin.Dispose();

    [Fact]
    public async Task Metrics_AreCombinedIntoBuckets_AndFilteredByDimensions()
    {
        var application = await CreateApplicationAsync();
        var name = $"orders.placed.{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var minute = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);

        using var sent = await SendAsync("/api/v1/metrics", application, $$"""
            [
              {"timestamp":"{{minute.AddMinutes(-10):O}}","name":"{{name}}","count":2,"sum":10,"min":4,"max":6,"environment":"Production","dimensions":{"Region":"EU","Shop":"A"} },
              {"timestamp":"{{minute.AddMinutes(-10).AddSeconds(30):O}}","name":"{{name}}","count":1,"sum":1,"min":1,"max":1,"environment":"Production","dimensions":{"Region":"US"} },
              {"timestamp":"{{minute.AddMinutes(-5):O}}","name":"{{name}}","count":3,"sum":30,"min":5,"max":20,"environment":"Staging"}
            ]
            """);
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);

        var range = $"from={Uri.EscapeDataString(minute.AddHours(-1).ToString("O"))}&to={Uri.EscapeDataString(minute.AddMinutes(1).ToString("O"))}";
        var scope = $"name={name}&applicationId={application.Id}&{range}";

        var series = await WaitForSeriesAsync(scope, count: 6);

        Assert.Equal(name, series.GetProperty("name").GetString());
        Assert.Equal(60, series.GetProperty("bucketSeconds").GetInt64());

        var buckets = series.GetProperty("buckets").EnumerateArray().ToList();
        Assert.Equal(61, buckets.Count);

        var tenAgo = Assert.Single(buckets, bucket => bucket.GetProperty("timestamp").GetDateTimeOffset() == minute.AddMinutes(-10));
        Assert.Equal(3, tenAgo.GetProperty("count").GetInt64());
        Assert.Equal(11, tenAgo.GetProperty("sum").GetDouble());
        Assert.Equal(1, tenAgo.GetProperty("min").GetDouble());
        Assert.Equal(6, tenAgo.GetProperty("max").GetDouble());

        var empty = Assert.Single(buckets, bucket => bucket.GetProperty("timestamp").GetDateTimeOffset() == minute.AddMinutes(-9));
        Assert.Equal(0, empty.GetProperty("count").GetInt64());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("min").ValueKind);

        Assert.Equal(2, CountOf(await GetSeriesAsync($"{scope}&dimension=Region%3DEU")));
        Assert.Equal(2, CountOf(await GetSeriesAsync($"{scope}&dimension=Region%3DEU&dimension=Shop%3DA")));
        Assert.Equal(0, CountOf(await GetSeriesAsync($"{scope}&dimension=Region%3DEU&dimension=Shop%3DB")));
        Assert.Equal(3, CountOf(await GetSeriesAsync($"{scope}&environment=Staging")));

        using var namesResponse = await _admin.GetAsync(new Uri($"/api/v1/metrics/names?applicationId={application.Id}&{range}", UriKind.Relative), Token);
        var names = await ReadAsync(namesResponse);
        var found = Assert.Single(names.GetProperty("items").EnumerateArray());
        Assert.Equal(name, found.GetProperty("name").GetString());
        Assert.Equal(minute.AddMinutes(-5), found.GetProperty("lastSeen").GetDateTimeOffset());
    }

    [Theory]
    [InlineData("""[]""")]
    [InlineData("""[null]""")]
    [InlineData("""[{"timestamp":"2020-01-01T00:00:00Z","name":"a","count":1,"sum":1,"min":1,"max":1}]""")]
    [InlineData("""[{"timestamp":"NOW","name":" ","count":1,"sum":1,"min":1,"max":1}]""")]
    [InlineData("""[{"timestamp":"NOW","name":"a","count":0,"sum":1,"min":1,"max":1}]""")]
    [InlineData("""[{"timestamp":"NOW","name":"a","count":1,"sum":1,"min":2,"max":1}]""")]
    [InlineData("""[{"timestamp":"NOW","name":"a","sum":1,"min":1,"max":1}]""")]
    [InlineData("""[{"timestamp":"NOW","name":"a","count":1,"min":1,"max":1}]""")]
    [InlineData("""[{"timestamp":"NOW","name":"a","count":1,"sum":1,"min":1,"max":1,"dimensions":{" ":"x"}}]""")]
    [InlineData("""[{"timestamp":"NOW","name":"a","count":1,"sum":1,"min":1,"max":1,"dimensions":{"a":"1","b":"2","c":"3","d":"4","e":"5","f":"6","g":"7","h":"8","i":"9","j":"10","k":"11"}}]""")]
    public async Task SendingAnInvalidMetricPoint_IsRejected(string body)
    {
        var application = await CreateApplicationAsync();

        using var response = await SendAsync("/api/v1/metrics", application, body.Replace("NOW", DateTimeOffset.UtcNow.ToString("O"), StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/metrics?name=a&from=2026-11-05T00:00:00Z&to=2026-11-04T00:00:00Z")]
    [InlineData("/api/v1/metrics?name=a&from=2000-01-01T00:00:00Z&to=2026-11-04T00:00:00Z")]
    [InlineData("/api/v1/metrics?name=a&dimension=Region")]
    [InlineData("/api/v1/metrics")]
    [InlineData("/api/v1/metrics/names?from=2026-11-05T00:00:00Z&to=2026-11-04T00:00:00Z")]
    [InlineData("/api/v1/overview?from=2026-11-05T00:00:00Z&to=2026-11-04T00:00:00Z")]
    [InlineData("/api/v1/overview?slowest=51")]
    public async Task Reading_WithAnInvalidParameter_IsRejected(string path)
    {
        using var response = await _admin.GetAsync(new Uri(path, UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/metrics?name=a")]
    [InlineData("/api/v1/metrics/names")]
    [InlineData("/api/v1/overview")]
    public async Task Reading_WithAnApiKeyOnly_IsNotAllowed(string path)
    {
        var application = await CreateApplicationAsync();

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Logbook-Key", application.ApiKey.Key);

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Overview_CountsLogEventsAndRequests_AndFindsTheSlowest()
    {
        var application = await CreateApplicationAsync();
        var now = DateTimeOffset.UtcNow;
        var minute = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero).AddMinutes(-5);

        using var logs = await SendAsync("/api/v1/logs", application, $$"""
            [
              {"timestamp":"{{minute.AddSeconds(1):O}}","level":"Information","message":"a","environment":"Production"},
              {"timestamp":"{{minute.AddSeconds(2):O}}","level":"Warning","message":"b","environment":"Production"},
              {"timestamp":"{{minute.AddSeconds(3):O}}","level":"Error","message":"c","environment":"Production"},
              {"timestamp":"{{minute.AddSeconds(4):O}}","level":"Critical","message":"d","environment":"Production"},
              {"timestamp":"{{minute.AddSeconds(5):O}}","level":"Error","message":"other environment","environment":"Staging"}
            ]
            """);
        logs.EnsureSuccessStatusCode();

        using var spans = await SendAsync("/api/v1/spans", application, $$"""
            [
              {{Span(minute.AddSeconds(1), "GET /fast", 10, true)}},
              {{Span(minute.AddSeconds(2), "GET /fast", 20, true)}},
              {{Span(minute.AddSeconds(3), "GET /slow", 900, false)}},
              {{Span(minute.AddSeconds(4), "GET /slow", 1100, true)}},
              {"timestamp":"{{minute.AddSeconds(5):O}}","kind":"Dependency","name":"SQL","durationMs":5000,"success":false,"traceId":"t","spanId":"s","environment":"Production"}
            ]
            """);
        spans.EnsureSuccessStatusCode();

        var range = $"from={Uri.EscapeDataString(minute.AddMinutes(-30).ToString("O"))}&to={Uri.EscapeDataString(minute.AddMinutes(30).ToString("O"))}";
        var query = $"applicationId={application.Id}&environment=Production&{range}";

        var overview = await WaitForOverviewAsync(query, logEvents: 4, requests: 4);

        Assert.Equal(60, overview.GetProperty("bucketSeconds").GetInt64());
        Assert.Equal(4, overview.GetProperty("requestCount").GetInt64());
        Assert.Equal(1, overview.GetProperty("failedRequestCount").GetInt64());

        var logBucket = Assert.Single(overview.GetProperty("logEvents").EnumerateArray(), bucket => bucket.GetProperty("total").GetInt64() > 0);
        Assert.Equal(minute, logBucket.GetProperty("timestamp").GetDateTimeOffset());
        Assert.Equal((4L, 1L, 2L), (logBucket.GetProperty("total").GetInt64(), logBucket.GetProperty("warnings").GetInt64(), logBucket.GetProperty("errors").GetInt64()));

        var requestBucket = Assert.Single(overview.GetProperty("requests").EnumerateArray(), bucket => bucket.GetProperty("count").GetInt64() > 0);
        Assert.Equal(1, requestBucket.GetProperty("failed").GetInt64());

        var slowest = overview.GetProperty("slowestRequests").EnumerateArray().ToList();
        Assert.Equal(["GET /slow", "GET /fast"], slowest.Select(item => item.GetProperty("name").GetString()));
        Assert.Equal(application.Id, slowest[0].GetProperty("applicationId").GetGuid());
        Assert.Equal(2, slowest[0].GetProperty("count").GetInt64());
        Assert.Equal(1, slowest[0].GetProperty("failed").GetInt64());
        Assert.Equal(1000, slowest[0].GetProperty("averageDurationMs").GetDouble());
        Assert.Equal(1100, slowest[0].GetProperty("maxDurationMs").GetDouble());
        Assert.InRange(slowest[0].GetProperty("p95DurationMs").GetDouble(), 1000, 1100);
    }

    [Fact]
    public async Task DeletingAnApplication_DeletesItsMetricPoints()
    {
        var application = await CreateApplicationAsync();
        var name = $"deleted.{Guid.NewGuid():N}";

        using var sent = await SendAsync("/api/v1/metrics", application, $$"""
            [{"timestamp":"{{DateTimeOffset.UtcNow:O}}","name":"{{name}}","count":1,"sum":1,"min":1,"max":1}]
            """);
        sent.EnsureSuccessStatusCode();

        await WaitForSeriesAsync($"name={name}", count: 1);

        using var deleted = await _admin.DeleteAsync(new Uri($"/api/v1/applications/{application.Id}", UriKind.Relative), Token);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Assert.Equal(0, CountOf(await GetSeriesAsync($"name={name}")));
    }

    [Fact]
    public async Task MetricsRecordedWithTheSdk_ArriveAggregated()
    {
        var application = await CreateApplicationAsync();
        var name = $"sdk.metric.{Guid.NewGuid():N}";

        var options = new LogbookOptions
        {
            Endpoint = factory.Server.BaseAddress,
            ApiKey = application.ApiKey.Key,
            FlushInterval = TimeSpan.FromMilliseconds(50),
            ShutdownTimeout = TimeSpan.FromSeconds(5)
        };

        using (var client = new LogbookClient(options, factory.Server.CreateHandler(), TimeProvider.System))
        {
            using var metrics = new LogbookMetrics(client, options, "Production");

            metrics.Record(name, 4, ("Region", "EU"));
            metrics.Record(name, 6, ("Region", "EU"));
            metrics.Record(name, 100, ("Region", "US"), ("Shop", new string('s', 600)));
        }

        var series = await WaitForSeriesAsync($"name={name}&applicationId={application.Id}&environment=Production", count: 3);
        Assert.Equal(110, series.GetProperty("buckets").EnumerateArray().Sum(bucket => bucket.GetProperty("sum").GetDouble()));

        var europe = await GetSeriesAsync($"name={name}&dimension=Region%3DEU");
        var bucket = Assert.Single(europe.GetProperty("buckets").EnumerateArray(), item => item.GetProperty("count").GetInt64() > 0);
        Assert.Equal((2L, 4d, 6d), (bucket.GetProperty("count").GetInt64(), bucket.GetProperty("min").GetDouble(), bucket.GetProperty("max").GetDouble()));
    }

    private static string Span(DateTimeOffset timestamp, string name, double durationMs, bool success)
    {
        return $$"""{"timestamp":"{{timestamp:O}}","kind":"Request","name":"{{name}}","durationMs":{{durationMs}},"success":{{(success ? "true" : "false")}},"traceId":"{{Guid.NewGuid():N}}","spanId":"00f067aa0ba902b7","environment":"Production"}""";
    }

    private static long CountOf(JsonElement series) => series.GetProperty("buckets").EnumerateArray().Sum(bucket => bucket.GetProperty("count").GetInt64());

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));

        return document.RootElement.Clone();
    }

    private async Task<CreateApplicationResponse> CreateApplicationAsync()
    {
        using var response = await _admin.PostAsJsonAsync("/api/v1/applications", new { name = $"Dashboard {Guid.NewGuid():N}" }, Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CreateApplicationResponse>(Token))!;
    }

    private async Task<HttpResponseMessage> SendAsync(string path, CreateApplicationResponse application, string body)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Logbook-Key", application.ApiKey.Key);

        using var content = LogEventBodies.Json(body);

        return await client.PostAsync(new Uri(path, UriKind.Relative), content, Token);
    }

    private async Task<JsonElement> GetSeriesAsync(string query)
    {
        using var response = await _admin.GetAsync(new Uri($"/api/v1/metrics?{query}", UriKind.Relative), Token);

        return await ReadAsync(response);
    }

    // Everything is written in the background. The limit is only there so that a bug ends in a failure.
    private async Task<JsonElement> WaitForSeriesAsync(string query, long count)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(Token);
        limit.CancelAfter(WaitLimit);

        while (true)
        {
            var series = await GetSeriesAsync(query);

            if (CountOf(series) >= count)
            {
                return series;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), limit.Token);
        }
    }

    private async Task<JsonElement> WaitForOverviewAsync(string query, long logEvents, long requests)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(Token);
        limit.CancelAfter(WaitLimit);

        while (true)
        {
            using var response = await _admin.GetAsync(new Uri($"/api/v1/overview?{query}", UriKind.Relative), limit.Token);
            var overview = await ReadAsync(response);

            var logEventCount = overview.GetProperty("logEvents").EnumerateArray().Sum(bucket => bucket.GetProperty("total").GetInt64());

            if (logEventCount >= logEvents && overview.GetProperty("requestCount").GetInt64() >= requests)
            {
                return overview;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), limit.Token);
        }
    }
}
