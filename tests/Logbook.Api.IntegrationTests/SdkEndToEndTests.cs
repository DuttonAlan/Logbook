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
///     Sends through the real SDK to the real server. The two share no code, so this is what notices when the format of
///     the requests drifts apart.
/// </summary>
/// <param name="factory">The Api under test.</param>
public sealed class SdkEndToEndTests(LogbookApiFactory factory) : IClassFixture<LogbookApiFactory>
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task AnEventWithAllFields_ArrivesInTheServerUnchanged()
    {
        var application = await CreateApplicationAsync();
        var timestamp = DateTimeOffset.UtcNow;

        using (var client = CreateClient(application, batchSize: 1))
        {
            client.Enqueue(new LogbookEvent(
                timestamp,
                LogbookLevel.Error,
                "Order 42 failed äöü 🎉",
                messageTemplate: "Order {OrderId} failed",
                category: "Shop.Orders.OrderService",
                environment: "Production",
                eventId: 4001,
                exceptionJson: """{"type":"System.InvalidOperationException","message":"Boom","inner":null}""",
                traceId: "4bf92f3577b34da6a3ce929d0e0e4736",
                spanId: "00f067aa0ba902b7",
                propertiesJson: """{"OrderId":42,"User":{"Id":"abc"}}"""));

            var items = await WaitForLogEventsAsync(application.Id, 1);
            var stored = items[0];

            Assert.Equal(timestamp.ToUnixTimeMilliseconds(), stored.GetProperty("timestamp").GetDateTimeOffset().ToUnixTimeMilliseconds());
            Assert.Equal("Error", stored.GetProperty("level").GetString());
            Assert.Equal("Order 42 failed äöü 🎉", stored.GetProperty("message").GetString());
            Assert.Equal("Order {OrderId} failed", stored.GetProperty("messageTemplate").GetString());
            Assert.Equal("Shop.Orders.OrderService", stored.GetProperty("category").GetString());
            Assert.Equal("Production", stored.GetProperty("environment").GetString());
            Assert.Equal(4001, stored.GetProperty("eventId").GetInt32());
            Assert.Equal("System.InvalidOperationException", stored.GetProperty("exception").GetProperty("type").GetString());
            Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", stored.GetProperty("traceId").GetString());
            Assert.Equal("00f067aa0ba902b7", stored.GetProperty("spanId").GetString());
            Assert.Equal(42, stored.GetProperty("properties").GetProperty("OrderId").GetInt32());
            Assert.Equal("abc", stored.GetProperty("properties").GetProperty("User").GetProperty("Id").GetString());
        }
    }

    [Fact]
    public async Task EventsSentInBatchesByCountAndByTime_AllArrive()
    {
        var application = await CreateApplicationAsync();

        using (var client = CreateClient(application, batchSize: 10))
        {
            // Two full batches and a partial one, which only the timer sends.
            foreach (var index in Enumerable.Range(0, 25))
            {
                client.Enqueue(new LogbookEvent(DateTimeOffset.UtcNow, LogbookLevel.Information, $"Event {index}"));
            }

            var items = await WaitForLogEventsAsync(application.Id, 25);

            Assert.Equal(
                Enumerable.Range(0, 25).Select(index => $"Event {index}").Order(),
                items.Select(item => item.GetProperty("message").GetString()!).Order());
        }
    }

    [Fact]
    public async Task AnObjectAtTheDepthLimitOfTheSdk_IsAcceptedAndCanBeFoundBySearching()
    {
        const int MaxDepthTheSdkSends = 29;

        var application = await CreateApplicationAsync();
        var properties = string.Concat(Enumerable.Repeat("""{"a":""", MaxDepthTheSdkSends - 1)) + "{}" + new string('}', MaxDepthTheSdkSends - 1);

        using (var client = CreateClient(application, batchSize: 1))
        {
            client.Enqueue(new LogbookEvent(DateTimeOffset.UtcNow, LogbookLevel.Information, "Deep", propertiesJson: properties));

            var items = await WaitForLogEventsAsync(application.Id, 1);

            Assert.Equal(JsonValueKind.Object, items[0].GetProperty("properties").ValueKind);
        }
    }

    [Fact]
    public async Task ALoneSurrogateInTheMessage_IsAcceptedByTheServer()
    {
        var application = await CreateApplicationAsync();

        using (var client = CreateClient(application, batchSize: 2))
        {
            client.Enqueue(new LogbookEvent(DateTimeOffset.UtcNow, LogbookLevel.Information, "broken \uD800 text"));
            client.Enqueue(new LogbookEvent(DateTimeOffset.UtcNow, LogbookLevel.Information, "fine"));

            var items = await WaitForLogEventsAsync(application.Id, 2);

            Assert.Contains(items, item => item.GetProperty("message").GetString() == "broken � text");
        }
    }

    [Fact]
    public async Task AnEscapedLoneSurrogateInAJsonObject_DoesNotCostTheBatch()
    {
        var application = await CreateApplicationAsync();

        using (var client = CreateClient(application, batchSize: 2))
        {
            client.Enqueue(new LogbookEvent(DateTimeOffset.UtcNow, LogbookLevel.Information, "escaped", propertiesJson: """{"a":"\uD800"}"""));
            client.Enqueue(new LogbookEvent(DateTimeOffset.UtcNow, LogbookLevel.Information, "neighbor"));

            var items = await WaitForLogEventsAsync(application.Id, 2);

            Assert.Contains(items, item => item.GetProperty("message").GetString() == "neighbor");
        }
    }

    private LogbookClient CreateClient(CreateApplicationResponse application, int batchSize)
    {
        var options = new LogbookOptions
        {
            Endpoint = factory.Server.BaseAddress,
            ApiKey = application.ApiKey.Key,
            BatchSize = batchSize,
            FlushInterval = TimeSpan.FromMilliseconds(50)
        };

        return new LogbookClient(options, factory.Server.CreateHandler(), TimeProvider.System);
    }

    private async Task<CreateApplicationResponse> CreateApplicationAsync()
    {
        using var admin = factory.CreateAdminClient();

        using var response = await admin.PostAsJsonAsync(
            "/api/v1/applications",
            new { name = $"SDK test {Guid.NewGuid():N}" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var application = await response.Content.ReadFromJsonAsync<CreateApplicationResponse>(TestContext.Current.CancellationToken);

        return application!;
    }

    // Log Events are written in the background. The limit is only there so that a bug ends in a failure.
    private async Task<JsonElement[]> WaitForLogEventsAsync(Guid applicationId, int count)
    {
        using var admin = factory.CreateAdminClient();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limit.CancelAfter(WaitLimit);

        while (true)
        {
            using var response = await admin.GetAsync($"/api/v1/logs?applicationId={applicationId}&limit=1000", limit.Token);
            response.EnsureSuccessStatusCode();

            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(limit.Token), cancellationToken: limit.Token);
            var items = document.RootElement.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToArray();

            if (items.Length >= count)
            {
                Assert.Equal(count, items.Length);
                return items;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), limit.Token);
        }
    }
}
