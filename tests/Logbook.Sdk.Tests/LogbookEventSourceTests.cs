// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Net;
using System.Text.Json;

using Microsoft.Extensions.Time.Testing;

namespace Logbook.Sdk.Tests;

/// <summary>
///     Checks what the client reports. Other tests run at the same time and report as well, so these look for a particular
///     report rather than counting them.
/// </summary>
public sealed class LogbookEventSourceTests : IDisposable
{
    private const string ApiKey = "the-secret-api-key";
    private const string Message = "the-secret-message";

    private readonly FakeTimeProvider _time = new();
    private readonly Listener _listener = new();

    /// <inheritdoc/>
    public void Dispose() => _listener.Dispose();

    [Fact]
    public void TheEventSource_IsValid_AndHasTheDocumentedName()
    {
        Assert.Equal("Logbook-Sdk", LogbookEventSource.Log.Name);
        Assert.NotNull(EventSource.GenerateManifest(typeof(LogbookEventSource), "Logbook.Sdk", EventManifestOptions.Strict));
    }

    [Fact]
    public void ADisabledClient_ReportsTheSetting()
    {
        using var client = new LogbookClient(new LogbookOptions { ApiKey = ApiKey }, handler: null, _time);

        Assert.Contains(_listener.Events, written => written.Name == nameof(LogbookEventSource.ClientDisabled) && Equals(written.Payload[0], "Endpoint"));
    }

    [Fact]
    public async Task ARefusal_IsReportedWithTheStatusCode()
    {
        using var handler = new RecordingHandler(_time, (_, _) => Respond(HttpStatusCode.Unauthorized));
        using var client = CreateClient(handler);

        Enqueue(client, 3);
        await handler.WaitForRequestAsync();
        await WaitForReportAsync(nameof(LogbookEventSource.RequestRefused), payload => Equals(payload[0], 401) && Equals(payload[1], 3));
    }

    [Fact]
    public async Task ATemporaryFailure_IsReportedWithTheStatusCodeAndTheDelay()
    {
        using var handler = new RecordingHandler(_time, (_, _) => Respond(HttpStatusCode.BadGateway));
        using var client = CreateClient(handler);

        Enqueue(client, 3);
        await handler.WaitForRequestAsync();

        await WaitForReportAsync(
            nameof(LogbookEventSource.RequestFailed),
            payload => Equals(payload[0], "502") && Equals(payload[1], 3) && (int)payload[2]! >= 500);
    }

    [Fact]
    public async Task AnExceptionWhileSending_IsReportedByItsType_WithoutItsMessage()
    {
        using var handler = new RecordingHandler(_time, (_, _) => throw new HttpRequestException(Message));
        using var client = CreateClient(handler);

        Enqueue(client, 3);
        await handler.WaitForRequestAsync();

        await WaitForReportAsync(nameof(LogbookEventSource.RequestFailed), payload => Equals(payload[0], nameof(HttpRequestException)));
        AssertNoSecrets();
    }

    [Fact]
    public async Task ARejectedBatch_IsReported()
    {
        using var handler = new RecordingHandler(_time, (_, _) => Respond(HttpStatusCode.RequestEntityTooLarge));
        using var client = CreateClient(handler);

        Enqueue(client, 3);
        await handler.WaitForRequestAsync();

        await WaitForReportAsync(nameof(LogbookEventSource.BatchRejected), payload => Equals(payload[0], 413) && Equals(payload[1], 3));
    }

    [Fact]
    public async Task ASuccess_IsReported()
    {
        using var handler = new RecordingHandler(_time);
        using var client = CreateClient(handler);

        Enqueue(client, 3);
        await handler.WaitForRequestAsync();

        await WaitForReportAsync(nameof(LogbookEventSource.BatchSent), payload => Equals(payload[0], 3) && (int)payload[1]! > 0);
        AssertNoSecrets();
    }

    [Fact]
    public async Task AFullBuffer_IsReportedByTheSender()
    {
        const int QueueSize = 7;

        using var handler = new RecordingHandler(_time, (_, _) => Respond(HttpStatusCode.ServiceUnavailable));
        using var client = new LogbookClient(
            new LogbookOptions
            {
                Endpoint = new Uri("https://logbook.example.com"),
                ApiKey = ApiKey,
                BatchSize = QueueSize,
                MaxQueueSize = QueueSize,
                ShutdownTimeout = TimeSpan.FromMilliseconds(50)
            },
            handler,
            _time);

        Enqueue(client, QueueSize + 11);
        await _time.AdvanceUntilAsync(TimeSpan.FromSeconds(1), () => _listener.Events.Any(IsBufferFull));

        static bool IsBufferFull(WrittenEvent written)
        {
            return written.Name == nameof(LogbookEventSource.EventsDropped)
                && Equals(written.Payload[0], LogbookEventSource.ReasonBufferFull)
                && (int)written.Payload[1]! > 0;
        }
    }

    [Fact]
    public async Task ExpiredAndTooLargeEvents_AreReportedWithTheirReason()
    {
        using var handler = new RecordingHandler(_time);
        using var client = CreateClient(handler);
        var tooLarge = JsonSerializer.Serialize(Enumerable.Range(0, 50_000).ToDictionary(index => $"property{index}", index => index));

        client.Enqueue(new LogbookEvent(_time.GetUtcNow().AddDays(-8), LogbookLevel.Information, Message));
        client.Enqueue(new LogbookEvent(_time.GetUtcNow(), LogbookLevel.Information, Message, propertiesJson: tooLarge));
        client.Enqueue(new LogbookEvent(_time.GetUtcNow(), LogbookLevel.Information, Message));

        await handler.WaitForRequestAsync();

        await WaitForReportAsync(nameof(LogbookEventSource.EventsDropped), payload => Equals(payload[0], LogbookEventSource.ReasonExpired));
        await WaitForReportAsync(nameof(LogbookEventSource.EventsDropped), payload => Equals(payload[0], LogbookEventSource.ReasonTooLarge));
        AssertNoSecrets();
    }

    [Fact]
    public async Task ACutEvent_IsReportedOnceItIsDelivered()
    {
        using var handler = new RecordingHandler(_time);
        using var client = CreateClient(handler);

        client.Enqueue(new LogbookEvent(_time.GetUtcNow(), LogbookLevel.Information, Message + new string('x', 100_000)));
        Enqueue(client, 2);

        await handler.WaitForRequestAsync();

        await WaitForReportAsync(nameof(LogbookEventSource.EventsTruncated), payload => Equals(payload[0], 1));
        AssertNoSecrets();
    }

    [Fact]
    public async Task EventsLostInTheShutdown_AreReported()
    {
        using var handler = new RecordingHandler(_time, (_, _) => Respond(HttpStatusCode.ServiceUnavailable));
        using var client = CreateClient(handler);

        Enqueue(client, 3);
        await handler.WaitForRequestAsync();
        client.Dispose();

        await WaitForReportAsync(nameof(LogbookEventSource.EventsDropped), payload => Equals(payload[0], LogbookEventSource.ReasonShutdown) && Equals(payload[1], 3));
    }

    private static Task<HttpResponseMessage> Respond(HttpStatusCode status) => Task.FromResult(new HttpResponseMessage(status));

    private void AssertNoSecrets()
    {
        foreach (var written in _listener.Events)
        {
            foreach (var value in written.Payload)
            {
                Assert.DoesNotContain(ApiKey, value?.ToString() ?? string.Empty, StringComparison.Ordinal);
                Assert.DoesNotContain(Message, value?.ToString() ?? string.Empty, StringComparison.Ordinal);
            }
        }
    }

    // The sender reports right after a request ends. The limit is only there so that a bug ends in a failure.
    private async Task WaitForReportAsync(string name, Func<IReadOnlyList<object?>, bool> matches)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(30));

        while (!_listener.Events.Any(written => written.Name == name && matches(written.Payload)))
        {
            await Task.Delay(1, limit.Token);
        }
    }

    private void Enqueue(LogbookClient client, int count)
    {
        for (var index = 0; index < count; index++)
        {
            client.Enqueue(new LogbookEvent(_time.GetUtcNow(), LogbookLevel.Information, Message));
        }
    }

    private LogbookClient CreateClient(RecordingHandler handler)
    {
        return new LogbookClient(
            new LogbookOptions
            {
                Endpoint = new Uri("https://logbook.example.com"),
                ApiKey = ApiKey,
                BatchSize = 3,
                ShutdownTimeout = TimeSpan.FromMilliseconds(500)
            },
            handler,
            _time);
    }

    private sealed record WrittenEvent(string? Name, IReadOnlyList<object?> Payload);

    private sealed class Listener : EventListener
    {
        private readonly ConcurrentQueue<WrittenEvent> _events = new();

        public IReadOnlyCollection<WrittenEvent> Events => _events;

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == LogbookEventSource.SourceName)
            {
                EnableEvents(eventSource, EventLevel.Verbose);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            // Called from the base constructor as well, before the field is set.
            _events?.Enqueue(new WrittenEvent(eventData.EventName, eventData.Payload ?? []));
        }
    }
}
