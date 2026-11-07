// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Logbook.Core.Application.Applications.Create;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks the endpoints for spans, traces and groups of exceptions, against a real database.
/// </summary>
/// <param name="factory">The Api under test.</param>
public sealed class TelemetryApiTests(LogbookApiFactory factory) : IClassFixture<LogbookApiFactory>, IDisposable
{
    private const string TraceA = "4bf92f3577b34da6a3ce929d0e0e4736";

    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    private readonly HttpClient _admin = factory.CreateAdminClient();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <inheritdoc/>
    public void Dispose() => _admin.Dispose();

    [Fact]
    public async Task SendingASpan_WithAllFields_CanBeSearched()
    {
        var application = await CreateApplicationAsync();
        var traceId = NewTraceId();

        using var response = await SendSpansAsync(application, $$"""
            [{
              "timestamp": "{{DateTimeOffset.UtcNow:O}}",
              "kind": "Dependency",
              "name": "GET payments.example.com",
              "durationMs": 12.5,
              "success": false,
              "traceId": "{{traceId}}",
              "spanId": "00f067aa0ba902b7",
              "parentSpanId": "53995c3f42cd8ad8",
              "environment": "Production",
              "statusCode": 502,
              "dependencyType": "HTTP",
              "target": "payments.example.com",
              "data": "https://payments.example.com/charge"
            }]
            """);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var span = (await WaitForSpansAsync($"applicationId={application.Id}", 1))[0];

        Assert.Equal(application.Id, span.GetProperty("applicationId").GetGuid());
        Assert.Equal("Dependency", span.GetProperty("kind").GetString());
        Assert.Equal("GET payments.example.com", span.GetProperty("name").GetString());
        Assert.Equal(12.5, span.GetProperty("durationMs").GetDouble());
        Assert.False(span.GetProperty("success").GetBoolean());
        Assert.Equal(traceId, span.GetProperty("traceId").GetString());
        Assert.Equal("00f067aa0ba902b7", span.GetProperty("spanId").GetString());
        Assert.Equal("53995c3f42cd8ad8", span.GetProperty("parentSpanId").GetString());
        Assert.Equal("Production", span.GetProperty("environment").GetString());
        Assert.Equal(502, span.GetProperty("statusCode").GetInt32());
        Assert.Equal("HTTP", span.GetProperty("dependencyType").GetString());
        Assert.Equal("payments.example.com", span.GetProperty("target").GetString());
        Assert.Equal("https://payments.example.com/charge", span.GetProperty("data").GetString());
    }

    [Theory]
    [InlineData("""[]""")]
    [InlineData("""[null]""")]
    [InlineData("""[{"timestamp":"2020-01-01T00:00:00Z","kind":"Request","name":"a","durationMs":1,"success":true,"traceId":"t","spanId":"s"}]""")]
    [InlineData("""[{"timestamp":"NOW","kind":"Other","name":"a","durationMs":1,"success":true,"traceId":"t","spanId":"s"}]""")]
    [InlineData("""[{"timestamp":"NOW","kind":"Request","name":" ","durationMs":1,"success":true,"traceId":"t","spanId":"s"}]""")]
    [InlineData("""[{"timestamp":"NOW","kind":"Request","name":"a","durationMs":-1,"success":true,"traceId":"t","spanId":"s"}]""")]
    [InlineData("""[{"timestamp":"NOW","kind":"Request","name":"a","success":true,"traceId":"t","spanId":"s"}]""")]
    [InlineData("""[{"timestamp":"NOW","kind":"Request","name":"a","durationMs":1,"traceId":"t","spanId":"s"}]""")]
    [InlineData("""[{"timestamp":"NOW","kind":"Request","name":"a","durationMs":1,"success":true,"spanId":"s"}]""")]
    [InlineData("""[{"timestamp":"NOW","kind":"Request","name":"a","durationMs":1,"success":true,"traceId":"t","spanId":""}]""")]
    public async Task SendingAnInvalidSpan_IsRejected(string body)
    {
        var application = await CreateApplicationAsync();

        using var response = await SendSpansAsync(application, body.Replace("NOW", DateTimeOffset.UtcNow.ToString("O"), StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SendingSpans_Compressed_IsAccepted()
    {
        var application = await CreateApplicationAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/spans") { Content = LogEventBodies.Gzip(Span(NewTraceId())) };
        request.Headers.Add("X-Logbook-Key", application.ApiKey.Key);

        using var client = factory.CreateClient();
        using var response = await client.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task SendingSpans_WithoutAnApiKey_IsUnauthorized()
    {
        using var client = factory.CreateClient();
        using var content = LogEventBodies.Json(Span(TraceA));
        using var response = await client.PostAsync(new Uri("/api/v1/spans", UriKind.Relative), content, Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/spans")]
    [InlineData("/api/v1/traces/" + TraceA)]
    [InlineData("/api/v1/exceptions")]
    public async Task Reading_WithAnApiKeyOnly_IsNotAllowed(string path)
    {
        var application = await CreateApplicationAsync();

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Logbook-Key", application.ApiKey.Key);

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Search_Filters_AndPages()
    {
        var application = await CreateApplicationAsync();
        var now = DateTimeOffset.UtcNow;

        using var sent = await SendSpansAsync(application, $$"""
            [
              {"timestamp":"{{now.AddSeconds(-3):O}}","kind":"Request","name":"GET /orders/{id}","durationMs":10,"success":true,"traceId":"t1","spanId":"s1","environment":"Production"},
              {"timestamp":"{{now.AddSeconds(-2):O}}","kind":"Request","name":"POST /orders","durationMs":900,"success":false,"traceId":"t2","spanId":"s2","environment":"Staging"},
              {"timestamp":"{{now.AddSeconds(-1):O}}","kind":"Dependency","name":"GET 100%_sure","durationMs":5,"success":true,"traceId":"t3","spanId":"s3","environment":"Production"}
            ]
            """);
        sent.EnsureSuccessStatusCode();

        var scope = $"applicationId={application.Id}";

        var all = await WaitForSpansAsync(scope, 3);
        Assert.Equal(["s3", "s2", "s1"], all.Select(span => span.GetProperty("spanId").GetString()));

        Assert.Equal(["s3"], await SpanIdsAsync($"{scope}&kind=Dependency"));
        Assert.Equal(["s2"], await SpanIdsAsync($"{scope}&success=false"));
        Assert.Equal(["s2"], await SpanIdsAsync($"{scope}&minDurationMs=100"));
        Assert.Equal(["s2"], await SpanIdsAsync($"{scope}&environment=Staging"));
        Assert.Equal(["s1"], await SpanIdsAsync($"{scope}&traceId=t1"));
        Assert.Equal(["s2", "s1"], await SpanIdsAsync($"{scope}&name=ORDERS"));

        // A % and a _ are text to search for, not wildcards.
        Assert.Equal(["s3"], await SpanIdsAsync($"{scope}&name=0%25_s"));
        Assert.Empty(await SpanIdsAsync($"{scope}&name=%25x%25"));

        using var firstPage = await _admin.GetAsync(new Uri($"/api/v1/spans?{scope}&limit=2", UriKind.Relative), Token);
        var page = await ReadAsync(firstPage);
        var cursor = page.GetProperty("nextCursor").GetString();
        Assert.NotNull(cursor);

        using var secondPage = await _admin.GetAsync(new Uri($"/api/v1/spans?{scope}&limit=2&cursor={cursor}", UriKind.Relative), Token);
        var next = await ReadAsync(secondPage);
        Assert.Equal("s1", next.GetProperty("items")[0].GetProperty("spanId").GetString());
        Assert.Equal(JsonValueKind.Null, next.GetProperty("nextCursor").ValueKind);
    }

    [Theory]
    [InlineData("kind=Other")]
    [InlineData("limit=0")]
    [InlineData("limit=1001")]
    [InlineData("cursor=nonsense")]
    [InlineData("from=2026-11-05T00:00:00Z&to=2026-11-04T00:00:00Z")]
    [InlineData("minDurationMs=-1")]
    public async Task Search_WithAnInvalidParameter_IsRejected(string query)
    {
        using var response = await _admin.GetAsync(new Uri($"/api/v1/spans?{query}", UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Trace_ContainsSpansAndLogEvents_AcrossApplications_OldestFirst()
    {
        var first = await CreateApplicationAsync();
        var second = await CreateApplicationAsync();
        var traceId = NewTraceId();
        var now = DateTimeOffset.UtcNow;

        using var request = await SendSpansAsync(first, $$"""
            [{"timestamp":"{{now.AddSeconds(-3):O}}","kind":"Request","name":"GET /a","durationMs":100,"success":true,"traceId":"{{traceId}}","spanId":"aaaaaaaaaaaaaaaa"}]
            """);
        request.EnsureSuccessStatusCode();

        using var dependency = await SendSpansAsync(second, $$"""
            [{"timestamp":"{{now.AddSeconds(-2):O}}","kind":"Request","name":"GET /b","durationMs":50,"success":true,"traceId":"{{traceId}}","spanId":"bbbbbbbbbbbbbbbb","parentSpanId":"aaaaaaaaaaaaaaaa"}]
            """);
        dependency.EnsureSuccessStatusCode();

        using var logs = await SendLogsAsync(first, $$"""
            [{"timestamp":"{{now.AddSeconds(-1):O}}","level":"Warning","message":"In the trace","traceId":"{{traceId}}","spanId":"aaaaaaaaaaaaaaaa"},
             {"timestamp":"{{now:O}}","level":"Warning","message":"Not in the trace","traceId":"{{NewTraceId()}}"}]
            """);
        logs.EnsureSuccessStatusCode();

        var trace = await WaitForTraceAsync(traceId, spans: 2, logEvents: 1);

        Assert.Equal(traceId, trace.GetProperty("traceId").GetString());
        Assert.False(trace.GetProperty("isTruncated").GetBoolean());
        Assert.Equal(["GET /a", "GET /b"], trace.GetProperty("spans").EnumerateArray().Select(span => span.GetProperty("name").GetString()));
        Assert.Equal("In the trace", Assert.Single(trace.GetProperty("logEvents").EnumerateArray()).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Trace_WithNothingStored_IsNotFound()
    {
        using var response = await _admin.GetAsync(new Uri($"/api/v1/traces/{NewTraceId()}", UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Trace_WithATooLongId_IsRejected()
    {
        using var response = await _admin.GetAsync(new Uri($"/api/v1/traces/{new string('a', 65)}", UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExceptionGroups_GroupByStack_WithCountsAndOccurrences()
    {
        var application = await CreateApplicationAsync();
        var other = await CreateApplicationAsync();
        var now = DateTimeOffset.UtcNow;

        var failure = """{"type":"System.InvalidOperationException","message":"MESSAGE","stackTrace":"   at Shop.Orders.Place() in /src/Orders.cs:line 42"}""";
        var otherFailure = """{"type":"System.ArgumentException","message":"Bad","stackTrace":"   at Shop.Billing.Send()"}""";

        using var logs = await SendLogsAsync(application, $$"""
            [
              {"timestamp":"{{now.AddDays(-3):O}}","level":"Error","message":"Old","exception":{{failure.Replace("MESSAGE", "Order 1", StringComparison.Ordinal)}}},
              {"timestamp":"{{now.AddMinutes(-3):O}}","level":"Error","message":"Second","exception":{{failure.Replace("MESSAGE", "Order 2", StringComparison.Ordinal)}}},
              {"timestamp":"{{now.AddMinutes(-2):O}}","level":"Error","message":"Latest","exception":{{failure.Replace("MESSAGE", "Order 3", StringComparison.Ordinal)}}},
              {"timestamp":"{{now.AddMinutes(-1):O}}","level":"Error","message":"Other","exception":{{otherFailure}}},
              {"timestamp":"{{now:O}}","level":"Error","message":"Without an exception"}
            ]
            """);
        logs.EnsureSuccessStatusCode();

        using var otherLogs = await SendLogsAsync(other, $$"""
            [{"timestamp":"{{now:O}}","level":"Error","message":"Same stack, other Application","exception":{{failure}}}]
            """);
        otherLogs.EnsureSuccessStatusCode();

        var byLast = await WaitForGroupsAsync($"applicationId={application.Id}", 2);

        Assert.Equal(
            ["System.ArgumentException", "System.InvalidOperationException"],
            byLast.Select(group => group.GetProperty("exception").GetProperty("type").GetString()));

        var group = byLast[1];
        Assert.Equal(3, group.GetProperty("count").GetInt64());
        Assert.Equal(application.Id, group.GetProperty("applicationId").GetGuid());
        Assert.Equal("Order 3", group.GetProperty("exception").GetProperty("message").GetString());
        Assert.Equal(now.AddMinutes(-2).ToUnixTimeSeconds(), group.GetProperty("lastOccurrence").GetDateTimeOffset().ToUnixTimeSeconds());
        Assert.Equal(now.AddDays(-3).ToUnixTimeSeconds(), group.GetProperty("firstOccurrence").GetDateTimeOffset().ToUnixTimeSeconds());

        var byCount = await GetGroupsAsync($"applicationId={application.Id}&sort=Count");
        Assert.Equal(3, byCount[0].GetProperty("count").GetInt64());

        // The time range limits what is counted, while the first occurrence still tells that this is not new.
        var from = Uri.EscapeDataString(now.AddHours(-1).ToString("O"));
        var recent = await GetGroupsAsync($"applicationId={application.Id}&from={from}");
        var recentGroup = Assert.Single(recent, item => item.GetProperty("fingerprint").GetString() == group.GetProperty("fingerprint").GetString());
        Assert.Equal(2, recentGroup.GetProperty("count").GetInt64());
        Assert.Equal(now.AddDays(-3).ToUnixTimeSeconds(), recentGroup.GetProperty("firstOccurrence").GetDateTimeOffset().ToUnixTimeSeconds());

        // Each occurrence can be found with the fingerprint.
        var fingerprint = group.GetProperty("fingerprint").GetString();
        using var occurrences = await _admin.GetAsync(new Uri($"/api/v1/logs?applicationId={application.Id}&exceptionFingerprint={fingerprint}", UriKind.Relative), Token);
        var found = await ReadAsync(occurrences);
        Assert.Equal(["Latest", "Second", "Old"], found.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("message").GetString()));
        Assert.Equal(fingerprint, found.GetProperty("items")[0].GetProperty("exceptionFingerprint").GetString());
    }

    [Fact]
    public async Task ExceptionGroups_WithAnEnvironment_ShowTheExceptionOfThatEnvironment_AtTheSameTimestamp()
    {
        var application = await CreateApplicationAsync();
        var now = DateTimeOffset.UtcNow;

        var failure = """{"type":"System.InvalidOperationException","message":"MESSAGE","stackTrace":"   at Shop.Orders.Place()"}""";

        using var logs = await SendLogsAsync(application, $$"""
            [
              {"timestamp":"{{now:O}}","level":"Error","message":"Staging","environment":"Staging","exception":{{failure.Replace("MESSAGE", "From Staging", StringComparison.Ordinal)}}},
              {"timestamp":"{{now:O}}","level":"Error","message":"Production","environment":"Production","exception":{{failure.Replace("MESSAGE", "From Production", StringComparison.Ordinal)}}}
            ]
            """);
        logs.EnsureSuccessStatusCode();

        await WaitForGroupsAsync($"applicationId={application.Id}&environment=Production", 1);
        await WaitForGroupsAsync($"applicationId={application.Id}&environment=Staging", 1);

        var production = Assert.Single(await GetGroupsAsync($"applicationId={application.Id}&environment=Production"));
        var staging = Assert.Single(await GetGroupsAsync($"applicationId={application.Id}&environment=Staging"));

        Assert.Equal("From Production", production.GetProperty("exception").GetProperty("message").GetString());
        Assert.Equal("From Staging", staging.GetProperty("exception").GetProperty("message").GetString());
    }

    [Fact]
    public async Task ExceptionGroups_WithAnInvalidSort_IsRejected()
    {
        using var response = await _admin.GetAsync(new Uri("/api/v1/exceptions?sort=Other", UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeletingAnApplication_DeletesItsSpans()
    {
        var application = await CreateApplicationAsync();

        using var sent = await SendSpansAsync(application, Span(NewTraceId()));
        sent.EnsureSuccessStatusCode();

        await WaitForSpansAsync($"applicationId={application.Id}", 1);

        using var deleted = await _admin.DeleteAsync(new Uri($"/api/v1/applications/{application.Id}", UriKind.Relative), Token);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Assert.Empty(await SpanIdsAsync($"applicationId={application.Id}"));
    }

    private static string NewTraceId() => Guid.NewGuid().ToString("N");

    private static string Span(string traceId)
    {
        return $$"""[{"timestamp":"{{DateTimeOffset.UtcNow:O}}","kind":"Request","name":"GET /","durationMs":1,"success":true,"traceId":"{{traceId}}","spanId":"00f067aa0ba902b7"}]""";
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));

        return document.RootElement.Clone();
    }

    private async Task<CreateApplicationResponse> CreateApplicationAsync()
    {
        using var response = await _admin.PostAsJsonAsync("/api/v1/applications", new { name = $"Telemetry {Guid.NewGuid():N}" }, Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CreateApplicationResponse>(Token))!;
    }

    private Task<HttpResponseMessage> SendSpansAsync(CreateApplicationResponse application, string body) => SendAsync("/api/v1/spans", application, body);

    private Task<HttpResponseMessage> SendLogsAsync(CreateApplicationResponse application, string body) => SendAsync("/api/v1/logs", application, body);

    private async Task<HttpResponseMessage> SendAsync(string path, CreateApplicationResponse application, string body)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Logbook-Key", application.ApiKey.Key);

        using var content = LogEventBodies.Json(body);

        return await client.PostAsync(new Uri(path, UriKind.Relative), content, Token);
    }

    private async Task<List<string?>> SpanIdsAsync(string query)
    {
        using var response = await _admin.GetAsync(new Uri($"/api/v1/spans?{query}", UriKind.Relative), Token);
        var page = await ReadAsync(response);

        return [.. page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("spanId").GetString())];
    }

    private async Task<List<JsonElement>> GetGroupsAsync(string query)
    {
        using var response = await _admin.GetAsync(new Uri($"/api/v1/exceptions?{query}", UriKind.Relative), Token);
        var groups = await ReadAsync(response);

        return [.. groups.GetProperty("items").EnumerateArray()];
    }

    // Everything is written in the background. The limit is only there so that a bug ends in a failure.
    private async Task<List<JsonElement>> WaitForSpansAsync(string query, int count)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(Token);
        limit.CancelAfter(WaitLimit);

        while (true)
        {
            using var response = await _admin.GetAsync(new Uri($"/api/v1/spans?{query}", UriKind.Relative), limit.Token);
            var page = await ReadAsync(response);
            var items = page.GetProperty("items").EnumerateArray().ToList();

            if (items.Count >= count)
            {
                return items;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), limit.Token);
        }
    }

    private async Task<List<JsonElement>> WaitForGroupsAsync(string query, int count)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(Token);
        limit.CancelAfter(WaitLimit);

        while (true)
        {
            var groups = await GetGroupsAsync(query);

            if (groups.Count >= count)
            {
                return groups;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), limit.Token);
        }
    }

    private async Task<JsonElement> WaitForTraceAsync(string traceId, int spans, int logEvents)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(Token);
        limit.CancelAfter(WaitLimit);

        while (true)
        {
            using var response = await _admin.GetAsync(new Uri($"/api/v1/traces/{traceId}", UriKind.Relative), limit.Token);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var trace = await ReadAsync(response);

                if (trace.GetProperty("spans").GetArrayLength() >= spans && trace.GetProperty("logEvents").GetArrayLength() >= logEvents)
                {
                    return trace;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), limit.Token);
        }
    }
}
