// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Diagnostics.Tracing;
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

    [Fact]
    public async Task AHugeMessage_IsCutAndMarked_AndDoesNotCostItsNeighbor()
    {
        var application = await CreateApplicationAsync();

        using (var client = CreateClient(application, batchSize: 2))
        {
            client.Enqueue(new LogbookEvent(DateTimeOffset.UtcNow, LogbookLevel.Information, new string('x', 1024 * 1024)));
            client.Enqueue(new LogbookEvent(DateTimeOffset.UtcNow, LogbookLevel.Information, "neighbor"));

            var items = await WaitForLogEventsAsync(application.Id, 2);

            var huge = Assert.Single(items, item => item.GetProperty("message").GetString() != "neighbor");
            Assert.Equal(65_536, huge.GetProperty("message").GetString()!.Length);
            Assert.True(huge.GetProperty("properties").GetProperty("logbook.truncated").GetBoolean());
        }
    }

    [Fact]
    public async Task MultiByteTextOverTheEventLimit_IsCutToWhatTheServerAccepts()
    {
        var application = await CreateApplicationAsync();

        // Each text is within the limit of 65,536 characters, but together they are about 400 KiB in UTF-8.
        var text = new string('日', 65_536);

        using (var client = CreateClient(application, batchSize: 1))
        {
            client.Enqueue(new LogbookEvent(DateTimeOffset.UtcNow, LogbookLevel.Information, text, messageTemplate: text));

            var items = await WaitForLogEventsAsync(application.Id, 1);

            Assert.True(items[0].GetProperty("message").GetString()!.Length < text.Length);
            Assert.True(items[0].GetProperty("properties").GetProperty("logbook.truncated").GetBoolean());
        }
    }

    [Fact]
    public async Task AHugeException_KeepsItsStructure_WithTheLongTextCut()
    {
        var application = await CreateApplicationAsync();
        var stackTrace = new string('s', 2 * 1024 * 1024);
        var exception = JsonSerializer.Serialize(new { type = "System.Exception", stackTrace, inner = new { type = "System.IO.IOException" } });

        using (var client = CreateClient(application, batchSize: 1))
        {
            client.Enqueue(new LogbookEvent(DateTimeOffset.UtcNow, LogbookLevel.Error, "Failed", exceptionJson: exception, propertiesJson: """{"OrderId":42}"""));

            var items = await WaitForLogEventsAsync(application.Id, 1);

            var stored = items[0].GetProperty("exception");
            Assert.Equal("System.Exception", stored.GetProperty("type").GetString());
            Assert.Equal("System.IO.IOException", stored.GetProperty("inner").GetProperty("type").GetString());
            Assert.True(stored.GetProperty("stackTrace").GetString()!.Length < stackTrace.Length);
            Assert.Equal("Failed", items[0].GetProperty("message").GetString());
            Assert.Equal(42, items[0].GetProperty("properties").GetProperty("OrderId").GetInt32());
            Assert.True(items[0].GetProperty("properties").GetProperty("logbook.truncated").GetBoolean());
        }
    }

    [Fact]
    public async Task FieldsOverTheFixedLimitsOfTheServer_AreCutAndAccepted()
    {
        var application = await CreateApplicationAsync();

        using (var client = CreateClient(application, batchSize: 1))
        {
            client.Enqueue(new LogbookEvent(
                DateTimeOffset.UtcNow,
                LogbookLevel.Information,
                "Long fields",
                category: new string('c', 1000),
                environment: new string('e', 1000),
                traceId: new string('t', 1000),
                spanId: new string('s', 1000)));

            var items = await WaitForLogEventsAsync(application.Id, 1);

            Assert.Equal(500, items[0].GetProperty("category").GetString()!.Length);
            Assert.Equal(100, items[0].GetProperty("environment").GetString()!.Length);
            Assert.Equal(64, items[0].GetProperty("traceId").GetString()!.Length);
            Assert.Equal(64, items[0].GetProperty("spanId").GetString()!.Length);
        }
    }

    [Fact]
    public async Task EventsOutsideOfTheTimeWindow_AreLeftOut_AndDoNotCostTheBatch()
    {
        var application = await CreateApplicationAsync();

        using (var client = CreateClient(application, batchSize: 4))
        {
            client.Enqueue(new LogbookEvent(DateTimeOffset.UtcNow.AddDays(-8), LogbookLevel.Information, "too old"));
            client.Enqueue(new LogbookEvent(DateTimeOffset.UtcNow.AddDays(-6), LogbookLevel.Information, "old enough"));
            client.Enqueue(new LogbookEvent(DateTimeOffset.UtcNow.AddHours(12), LogbookLevel.Information, "soon enough"));
            client.Enqueue(new LogbookEvent(DateTimeOffset.UtcNow.AddDays(2), LogbookLevel.Information, "too far ahead"));

            var items = await WaitForLogEventsAsync(application.Id, 2);

            Assert.Equal(["old enough", "soon enough"], items.Select(item => item.GetProperty("message").GetString()!).Order());
            Assert.Equal(2, client.Buffer!.DroppedCount);
        }
    }

    [Fact]
    public async Task Dispose_SendsWhatIsLeft_WithoutWaitingForTheFlushInterval()
    {
        var application = await CreateApplicationAsync();

        var client = new LogbookClient(
            new LogbookOptions
            {
                Endpoint = factory.Server.BaseAddress,
                ApiKey = application.ApiKey.Key,
                BatchSize = 10,
                FlushInterval = TimeSpan.FromHours(1),
                ShutdownTimeout = WaitLimit
            },
            factory.Server.CreateHandler(),
            TimeProvider.System);

        foreach (var index in Enumerable.Range(0, 25))
        {
            client.Enqueue(new LogbookEvent(DateTimeOffset.UtcNow, LogbookLevel.Information, $"Event {index}"));
        }

        client.Dispose();

        Assert.Equal(25, (await WaitForLogEventsAsync(application.Id, 25)).Length);
        Assert.Equal(0, client.Buffer!.DroppedCount);
    }

    [Fact]
    public async Task AWrongApiKey_IsReported_AndKeepsTheEvents()
    {
        var application = await CreateApplicationAsync();
        var refused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new RefusalListener(refused);

        using var client = new LogbookClient(
            new LogbookOptions
            {
                Endpoint = factory.Server.BaseAddress,
                ApiKey = "not-an-api-key",
                BatchSize = 1,
                ShutdownTimeout = TimeSpan.Zero
            },
            factory.Server.CreateHandler(),
            TimeProvider.System);

        client.Enqueue(new LogbookEvent(DateTimeOffset.UtcNow, LogbookLevel.Information, "Kept"));

        await refused.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

        Assert.Equal(1, client.Buffer!.Count);
        Assert.Equal(0, client.Buffer.DroppedCount);
        Assert.Empty(await SearchAsync(application.Id, TestContext.Current.CancellationToken));
    }

    private LogbookClient CreateClient(CreateApplicationResponse application, int batchSize)
    {
        var options = new LogbookOptions
        {
            Endpoint = factory.Server.BaseAddress,
            ApiKey = application.ApiKey.Key,
            BatchSize = batchSize,
            FlushInterval = TimeSpan.FromMilliseconds(50),
            ShutdownTimeout = TimeSpan.Zero
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
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limit.CancelAfter(WaitLimit);

        while (true)
        {
            var items = await SearchAsync(applicationId, limit.Token);

            if (items.Length >= count)
            {
                Assert.Equal(count, items.Length);
                return items;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), limit.Token);
        }
    }

    private async Task<JsonElement[]> SearchAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        using var admin = factory.CreateAdminClient();
        using var response = await admin.GetAsync($"/api/v1/logs?applicationId={applicationId}&limit=1000", cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        return [.. document.RootElement.GetProperty("items").EnumerateArray().Select(item => item.Clone())];
    }

    private sealed class RefusalListener(TaskCompletionSource refused) : EventListener
    {
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "Logbook-Sdk")
            {
                EnableEvents(eventSource, EventLevel.Warning);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventName == "RequestRefused" && Equals(eventData.Payload?[0], 401))
            {
                refused.TrySetResult();
            }
        }
    }
}
