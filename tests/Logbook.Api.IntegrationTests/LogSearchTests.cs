// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Logbook.Core.Application.Applications.Create;
using Logbook.Core.Application.LogEvents.Search;
using Logbook.Core.Domain.Entities;
using Logbook.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks searching Log Events through the log endpoint.
/// </summary>
/// <param name="factory">The Api under test.</param>
public sealed class LogSearchTests(LogbookApiFactory factory) : IClassFixture<LogbookApiFactory>, IAsyncLifetime
{
    private const string Endpoint = "/api/v1/logs";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _admin = factory.CreateAdminClient();

    private HttpClient _sender = null!;
    private Guid _applicationId;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        var application = await CreateApplicationAsync();

        _applicationId = application.Id;
        _sender = CreateSender(application);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _sender.Dispose();
        _admin.Dispose();

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Search_WithoutAdminKey_IsUnauthorized()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Endpoint, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Search_WithApiKey_IsUnauthorized_BecauseAnApiKeyCanOnlySend()
    {
        using var response = await _sender.GetAsync(Endpoint, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Search_ReturnsAllFields_WithJsonAsObjects()
    {
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-1);

        await SendAsync(_sender, $$"""
            [{
              "timestamp": "{{timestamp:O}}",
              "level": "Error",
              "messageTemplate": "Order {OrderId} failed",
              "message": "Order 42 failed",
              "category": "Shop.Orders.OrderService",
              "environment": "Production",
              "eventId": 4001,
              "exception": { "type": "System.InvalidOperationException", "inner": null },
              "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
              "spanId": "00f067aa0ba902b7",
              "properties": { "OrderId": 42, "Nested": { "UserId": "abc" } }
            }]
            """);

        using var document = JsonDocument.Parse(await WaitForRawAsync(1));
        var item = document.RootElement.GetProperty("items")[0];

        Assert.Equal(_applicationId, item.GetProperty("applicationId").GetGuid());
        Assert.Equal(timestamp.UtcDateTime, item.GetProperty("timestamp").GetDateTime(), TimeSpan.FromMilliseconds(1));
        Assert.EndsWith("Z", item.GetProperty("timestamp").GetString(), StringComparison.Ordinal);
        Assert.Equal("Error", item.GetProperty("level").GetString());
        Assert.Equal("Order 42 failed", item.GetProperty("message").GetString());
        Assert.Equal("Order {OrderId} failed", item.GetProperty("messageTemplate").GetString());
        Assert.Equal("Shop.Orders.OrderService", item.GetProperty("category").GetString());
        Assert.Equal("Production", item.GetProperty("environment").GetString());
        Assert.Equal(4001, item.GetProperty("eventId").GetInt32());
        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", item.GetProperty("traceId").GetString());
        Assert.Equal("00f067aa0ba902b7", item.GetProperty("spanId").GetString());

        // Objects, not strings with escaped JSON.
        Assert.Equal(42, item.GetProperty("properties").GetProperty("OrderId").GetInt32());
        Assert.Equal("abc", item.GetProperty("properties").GetProperty("Nested").GetProperty("UserId").GetString());
        Assert.Equal("System.InvalidOperationException", item.GetProperty("exception").GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task Search_WithoutOptionalFields_ReturnsNull()
    {
        await SendAsync(_sender, LogEventBodies.Valid());

        using var document = JsonDocument.Parse(await WaitForRawAsync(1));
        var item = document.RootElement.GetProperty("items")[0];

        Assert.Equal(JsonValueKind.Null, item.GetProperty("properties").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("exception").ValueKind);
    }

    [Fact]
    public async Task Search_IsNewestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        await SendAsync(_sender, Events(("old", now.AddHours(-3)), ("newest", now), ("middle", now.AddHours(-1))));

        var page = await WaitForAsync(3);

        Assert.Equal(["newest", "middle", "old"], page.Items.Select(item => item.Message));
    }

    [Fact]
    public async Task Paging_ReturnsEveryLogEventOnce_AlsoWithIdenticalTimestampsAndAcrossDays()
    {
        var now = DateTimeOffset.UtcNow;
        var sameTime = now.AddDays(-1);
        var events = Enumerable.Range(0, 25).Select(i => ($"same {i}", sameTime))
            .Concat(Enumerable.Range(0, 10).Select(i => ($"other {i}", now.AddDays(-3).AddMinutes(i))))
            .ToArray();
        await SendAsync(_sender, Events(events));
        await WaitForAsync(events.Length);

        var seen = new List<LogEventResponse>();
        string? cursor = null;
        var pages = 0;

        do
        {
            var page = await SearchAsync($"applicationId={_applicationId}&limit=7" + (cursor is null ? string.Empty : $"&cursor={cursor}"));

            Assert.True(page.Items.Count <= 7);
            seen.AddRange(page.Items);
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        Assert.Equal(5, pages);
        Assert.Equal(events.Length, seen.Select(item => item.Id).Distinct().Count());
        Assert.Equal(seen.Select(item => item.Timestamp).OrderDescending(), seen.Select(item => item.Timestamp));
    }

    [Fact]
    public async Task Paging_LastFullPage_HasNoNextCursor()
    {
        await SendAsync(_sender, Events(("a", DateTimeOffset.UtcNow), ("b", DateTimeOffset.UtcNow)));
        await WaitForAsync(2);

        var page = await SearchAsync($"applicationId={_applicationId}&limit=2");

        Assert.Equal(2, page.Items.Count);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task Paging_IsNotDisturbed_ByNewLogEvents()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-10);
        await SendAsync(_sender, Events(("first", now), ("second", now.AddMinutes(-1)), ("third", now.AddMinutes(-2))));
        await WaitForAsync(3);

        var firstPage = await SearchAsync($"applicationId={_applicationId}&limit=1");

        await SendAsync(_sender, Events(("newer", DateTimeOffset.UtcNow)));
        await WaitForAsync(4);

        var secondPage = await SearchAsync($"applicationId={_applicationId}&limit=1&cursor={firstPage.NextCursor}");

        Assert.Equal("first", Assert.Single(firstPage.Items).Message);
        Assert.Equal("second", Assert.Single(secondPage.Items).Message);
    }

    [Fact]
    public async Task Filter_TimeRange_IncludesFromAndExcludesTo()
    {
        var start = DateTimeOffset.UtcNow.AddHours(-5).AddTicks(-(DateTimeOffset.UtcNow.Ticks % TimeSpan.TicksPerSecond));
        await SendAsync(_sender, Events(("before", start.AddSeconds(-1)), ("at from", start), ("at to", start.AddHours(1)), ("after", start.AddHours(2))));
        await WaitForAsync(4);

        var page = await SearchAsync($"applicationId={_applicationId}&from={Uri.EscapeDataString(start.ToString("O"))}&to={Uri.EscapeDataString(start.AddHours(1).ToString("O"))}");

        Assert.Equal("at from", Assert.Single(page.Items).Message);
    }

    [Fact]
    public async Task Filter_TimeRange_RespectsTheOffset()
    {
        var at = DateTimeOffset.UtcNow.AddHours(-5);
        await SendAsync(_sender, Events(("match", at)));
        await WaitForAsync(1);

        var withOffset = at.AddMinutes(-1).ToOffset(TimeSpan.FromHours(5));
        var page = await SearchAsync($"applicationId={_applicationId}&from={Uri.EscapeDataString(withOffset.ToString("O"))}");

        Assert.Single(page.Items);
    }

    [Fact]
    public async Task Filter_Level_AllowsSeveralLevels()
    {
        var now = DateTimeOffset.UtcNow;
        await SendAsync(_sender, $$"""
            [
              { "timestamp": "{{now:O}}", "level": "Information", "message": "i" },
              { "timestamp": "{{now:O}}", "level": "Warning", "message": "w" },
              { "timestamp": "{{now:O}}", "level": "Error", "message": "e" }
            ]
            """);
        await WaitForAsync(3);

        var page = await SearchAsync($"applicationId={_applicationId}&level=Warning&level=Error");

        Assert.Equal(["e", "w"], page.Items.Select(item => item.Message).Order());
    }

    [Fact]
    public async Task Filter_Application_ReturnsOnlyThatApplication()
    {
        var other = await CreateApplicationAsync();
        using var otherSender = CreateSender(other);
        await SendAsync(_sender, Events(("mine", DateTimeOffset.UtcNow)));
        await SendAsync(otherSender, Events(("theirs", DateTimeOffset.UtcNow)));
        await WaitForAsync(1);

        var page = await SearchAsync($"applicationId={_applicationId}");

        Assert.Equal("mine", Assert.Single(page.Items).Message);
    }

    [Fact]
    public async Task Filter_Environment_IsExact_AndFindsTheDefault()
    {
        var now = DateTimeOffset.UtcNow;
        await SendAsync(_sender, $$"""
            [
              { "timestamp": "{{now:O}}", "level": "Error", "message": "p", "environment": "Production" },
              { "timestamp": "{{now:O}}", "level": "Error", "message": "lower", "environment": "production" },
              { "timestamp": "{{now:O}}", "level": "Error", "message": "none" }
            ]
            """);
        await WaitForAsync(3);

        Assert.Equal("p", Assert.Single((await SearchAsync($"applicationId={_applicationId}&environment=Production")).Items).Message);
        Assert.Equal("none", Assert.Single((await SearchAsync($"applicationId={_applicationId}&environment=Unknown")).Items).Message);
    }

    [Theory]
    [InlineData("failed", new[] { "Order 42 failed", "Payment failed for order 7" })]
    [InlineData("FAILED", new[] { "Order 42 failed", "Payment failed for order 7" })]
    [InlineData("order failed", new[] { "Order 42 failed", "Payment failed for order 7" })]
    [InlineData("\"order 42\"", new[] { "Order 42 failed" })]
    [InlineData("payment or shipped", new[] { "Payment failed for order 7", "Shipped order 9" })]
    [InlineData("order -failed", new[] { "Shipped order 9" })]
    [InlineData("fail", new string[0])]
    [InlineData("nothing-matches-this", new string[0])]
    public async Task Filter_Text_SearchesWordsOfTheMessage(string text, string[] expected)
    {
        var now = DateTimeOffset.UtcNow;
        await SendAsync(_sender, Events(("Order 42 failed", now), ("Payment failed for order 7", now), ("Shipped order 9", now)));
        await WaitForAsync(3);

        var page = await SearchAsync($"applicationId={_applicationId}&text={Uri.EscapeDataString(text)}");

        Assert.Equal(expected.Order(), page.Items.Select(item => item.Message).Order());
    }

    [Theory]
    [InlineData("'")]
    [InlineData("\"")]
    [InlineData("a & b | !c")]
    [InlineData("(:*")]
    [InlineData("\\")]
    [InlineData("!!!")]
    [InlineData("%00")]
    public async Task Filter_Text_WithAnyInput_IsNotAnError(string text)
    {
        var encoded = text == "%00" ? text : Uri.EscapeDataString(text);

        using var response = await _admin.GetAsync($"{Endpoint}?text={encoded}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Filter_Environment_WithNullCharacter_IsNotAnError()
    {
        using var response = await _admin.GetAsync($"{Endpoint}?environment=%00", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("  ")]
    [InlineData("")]
    public async Task Filter_EmptyText_IsNotApplied(string text)
    {
        await SendAsync(_sender, Events(("a", DateTimeOffset.UtcNow)));
        await WaitForAsync(1);

        var page = await SearchAsync($"applicationId={_applicationId}&text={Uri.EscapeDataString(text)}");

        Assert.Single(page.Items);
    }

    [Fact]
    public async Task Filters_AreCombined()
    {
        var now = DateTimeOffset.UtcNow;
        await SendAsync(_sender, $$"""
            [
              { "timestamp": "{{now:O}}", "level": "Error", "message": "disk full", "environment": "Production" },
              { "timestamp": "{{now:O}}", "level": "Warning", "message": "disk full", "environment": "Production" },
              { "timestamp": "{{now:O}}", "level": "Error", "message": "disk full", "environment": "Staging" },
              { "timestamp": "{{now:O}}", "level": "Error", "message": "other", "environment": "Production" }
            ]
            """);
        await WaitForAsync(4);

        var page = await SearchAsync($"applicationId={_applicationId}&level=Error&environment=Production&text=disk");

        var item = Assert.Single(page.Items);
        Assert.Equal(LogEventLevel.Error, item.Level);
        Assert.Equal("Production", item.Environment);
    }

    [Theory]
    [InlineData("level=Fatal")]
    [InlineData("level=99")]
    [InlineData("limit=0")]
    [InlineData("limit=1001")]
    [InlineData("limit=abc")]
    [InlineData("from=yesterday")]
    [InlineData("from=2026-11-05T00:00:00+02:00")]
    [InlineData("applicationId=not-a-guid")]
    [InlineData("cursor=not-a-cursor")]
    [InlineData("cursor=AAAA")]
    [InlineData("cursor=_____________________________________w")]
    [InlineData("from=2026-11-05T00:00:00Z&to=2026-11-05T00:00:00Z")]
    [InlineData("from=2026-11-06T00:00:00Z&to=2026-11-05T00:00:00Z")]
    public async Task Search_WithInvalidParameter_IsBadRequest(string query)
    {
        using var response = await _admin.GetAsync($"{Endpoint}?{query}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Search_WithTooLongText_IsBadRequest()
    {
        using var response = await _admin.GetAsync($"{Endpoint}?text={new string('x', 501)}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TraceFilter_FindsOnlyThatTrace()
    {
        var now = DateTimeOffset.UtcNow;
        await SendAsync(_sender, $$"""
            [
              { "timestamp": "{{now:O}}", "level": "Error", "message": "first", "traceId": "aaaa" },
              { "timestamp": "{{now:O}}", "level": "Error", "message": "second", "traceId": "bbbb" },
              { "timestamp": "{{now:O}}", "level": "Error", "message": "none" }
            ]
            """);
        await WaitForAsync(3);

        var page = await SearchAsync($"applicationId={_applicationId}&traceId=aaaa");

        Assert.Equal("first", Assert.Single(page.Items).Message);
    }

    [Theory]
    [InlineData("OrderId%3D42", "number")]
    [InlineData("OrderId%3D42.0", "number")]
    [InlineData("Code%3D42", "text number")]
    [InlineData("UserId%3Dabc", "number")]
    [InlineData("Name%3Da%3Db", "equals")]
    [InlineData("Active%3Dtrue", "boolean")]
    [InlineData("Empty%3D", "empty")]
    [InlineData("Big%3D12345678901234567", "long")]
    public async Task PropertyFilter_MatchesTheValue_WhateverItsJsonType(string filter, string expected)
    {
        var now = DateTimeOffset.UtcNow;
        await SendAsync(_sender, $$"""
            [
              { "timestamp": "{{now:O}}", "level": "Error", "message": "number", "properties": { "OrderId": 42, "UserId": "abc" } },
              { "timestamp": "{{now:O}}", "level": "Error", "message": "text number", "properties": { "Code": "42" } },
              { "timestamp": "{{now:O}}", "level": "Error", "message": "equals", "properties": { "Name": "a=b" } },
              { "timestamp": "{{now:O}}", "level": "Error", "message": "boolean", "properties": { "Active": true } },
              { "timestamp": "{{now:O}}", "level": "Error", "message": "empty", "properties": { "Empty": "" } },
              { "timestamp": "{{now:O}}", "level": "Error", "message": "long", "properties": { "Big": 12345678901234567 } },
              { "timestamp": "{{now:O}}", "level": "Error", "message": "without properties" }
            ]
            """);
        await WaitForAsync(7);

        var page = await SearchAsync($"applicationId={_applicationId}&property={filter}");

        Assert.Equal(expected, Assert.Single(page.Items).Message);
    }

    [Fact]
    public async Task PropertyFilters_MustAllMatch()
    {
        var now = DateTimeOffset.UtcNow;
        await SendAsync(_sender, $$"""
            [
              { "timestamp": "{{now:O}}", "level": "Error", "message": "both", "properties": { "A": 1, "B": 2 } },
              { "timestamp": "{{now:O}}", "level": "Error", "message": "one", "properties": { "A": 1 } }
            ]
            """);
        await WaitForAsync(2);

        var page = await SearchAsync($"applicationId={_applicationId}&property=A%3D1&property=B%3D2");

        Assert.Equal("both", Assert.Single(page.Items).Message);
    }

    [Theory]
    [InlineData("property=")]
    [InlineData("property=NoSeparator")]
    [InlineData("property=%3DValue")]
    [InlineData("property=A%3D1&property=A%3D1&property=A%3D1&property=A%3D1&property=A%3D1&property=A%3D1&property=A%3D1&property=A%3D1&property=A%3D1&property=A%3D1&property=A%3D1")]
    public async Task PropertyFilter_NotValid_IsBadRequest(string query)
    {
        using var response = await _admin.GetAsync($"{Endpoint}?{query}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("%00")]
    [InlineData("%22%5C")]
    [InlineData("1e999999")]
    public async Task PropertyFilter_WithUnusualValue_IsNotAnError(string value)
    {
        var page = await SearchAsync($"applicationId={_applicationId}&property=Name%3D{value}");

        Assert.Empty(page.Items);
    }

    [Fact]
    public void Cursor_SurvivesEncoding_ToTheStoredPrecision()
    {
        var cursor = new LogEventCursor(new DateTime(2026, 11, 5, 10, 15, 0, DateTimeKind.Utc).AddTicks(1230), Guid.CreateVersion7());

        Assert.True(LogEventCursor.TryDecode(cursor.Encode(), out var decoded));
        Assert.Equal(cursor, decoded);
        Assert.Equal(DateTimeKind.Utc, decoded!.Timestamp.Kind);
    }

    [Fact]
    public async Task TextSearch_UsesTheFullTextIndexExpression()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LogbookContext>();

        var sql = context.LogEvents
            .Where(logEvent => EF.Functions.ToTsVector("simple", logEvent.Message).Matches(EF.Functions.WebSearchToTsQuery("simple", "x")))
            .ToQueryString();

        // A parameter instead of the literal would not match the index.
        Assert.Contains("to_tsvector('simple', l.\"Message\")", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_ReturnsJsonAtTheDepthLimit()
    {
        var members = $""" "properties": {LogEventBodies.NestedObjects(LogEvent.MaxJsonDepth)}, "exception": {LogEventBodies.NestedArrays(LogEvent.MaxJsonDepth)} """;

        await SendAsync(_sender, LogEventBodies.Valid(members));

        var result = await WaitForAsync(1);

        Assert.Equal(JsonValueKind.Object, Assert.Single(result.Items).Properties!.Value.ValueKind);
    }

    private static string Events(params (string Message, DateTimeOffset Timestamp)[] events)
    {
        return JsonSerializer.Serialize(events.Select(e => new { timestamp = e.Timestamp, level = "Error", message = e.Message }));
    }

    private static async Task SendAsync(HttpClient sender, string body)
    {
        using var content = LogEventBodies.Json(body);
        using var response = await sender.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private HttpClient CreateSender(CreateApplicationResponse application)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Logbook-Key", application.ApiKey.Key);

        return client;
    }

    private async Task<CreateApplicationResponse> CreateApplicationAsync()
    {
        using var response = await _admin.PostAsJsonAsync(
            "/api/v1/applications",
            new CreateApplicationRequest($"Search {Guid.NewGuid():N}"),
            TestContext.Current.CancellationToken);

        return (await response.Content.ReadFromJsonAsync<CreateApplicationResponse>(TestContext.Current.CancellationToken))!;
    }

    private async Task<SearchLogEventsResponse> SearchAsync(string query)
    {
        using var response = await _admin.GetAsync($"{Endpoint}?{query}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<SearchLogEventsResponse>(JsonOptions, TestContext.Current.CancellationToken))!;
    }

    private async Task<SearchLogEventsResponse> WaitForAsync(int count, Guid? applicationId = null)
    {
        while (true)
        {
            var page = await SearchAsync($"applicationId={applicationId ?? _applicationId}&limit=1000");

            if (page.Items.Count >= count)
            {
                return page;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
    }

    private async Task<string> WaitForRawAsync(int count)
    {
        await WaitForAsync(count);

        return await _admin.GetStringAsync($"{Endpoint}?applicationId={_applicationId}", TestContext.Current.CancellationToken);
    }
}
