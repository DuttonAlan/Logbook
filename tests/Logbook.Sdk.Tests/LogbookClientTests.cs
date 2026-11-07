// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Diagnostics;
using System.Net;
using System.Text;

using Microsoft.Extensions.Time.Testing;

namespace Logbook.Sdk.Tests;

/// <summary>
///     Checks the client with its real buffer and sender against a stand-in for the server. Waiting times run on a fake
///     clock, so the tests do not depend on how fast the machine is.
/// </summary>
public sealed class LogbookClientTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    // These tests are not about the shutdown, so it should not make those that end with a request under way slow.
    private static readonly TimeSpan ShortShutdown = TimeSpan.FromMilliseconds(50);

    private static readonly LogbookOptions BaseOptions = new()
    {
        Endpoint = new Uri("https://logbook.example.com"),
        ApiKey = "the-api-key",
        BatchSize = 3,
        FlushInterval = Interval,
        ShutdownTimeout = ShortShutdown
    };

    private readonly FakeTimeProvider _time = new();

    [Fact]
    public async Task AFullBatch_IsSentWithoutTheClockMoving()
    {
        using var handler = new RecordingHandler(_time);
        using var client = CreateClient(handler);

        Enqueue(client, "1", "2", "3");
        await handler.WaitForRequestAsync();

        Assert.Equal(["1", "2", "3"], handler.Requests[0].Messages);
        Assert.Equal(_time.GetUtcNow(), handler.Requests[0].ArrivedAt);
    }

    [Fact]
    public async Task APartialBatch_IsSentWhenTheOldestEventHasWaitedTheInterval_AndNotBefore()
    {
        using var handler = new RecordingHandler(_time);
        using var client = CreateClient(handler);
        var start = _time.GetUtcNow();

        Enqueue(client, "1");
        _time.Advance(Interval - TimeSpan.FromMilliseconds(1));

        // Only gives a wrong early send the chance to happen. The assertions below hold however long this takes.
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Empty(handler.Requests);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        await handler.WaitForRequestAsync();

        Assert.Equal(["1"], handler.Requests[0].Messages);
        Assert.Equal(start + Interval, handler.Requests[0].ArrivedAt);
    }

    [Fact]
    public async Task AnEventLeftOverFromAFullBatch_IsSentByTheTimer()
    {
        using var handler = new RecordingHandler(_time);
        using var client = CreateClient(handler);

        Enqueue(client, "1", "2", "3", "4");
        await handler.WaitForRequestAsync();
        Assert.Equal(["1", "2", "3"], handler.Requests[0].Messages);

        _time.Advance(Interval);
        await handler.WaitForRequestAsync();

        Assert.Equal(["4"], handler.Requests[1].Messages);
    }

    [Fact]
    public async Task ABacklog_IsSentInBatchesOfBatchSize_InOrder()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new RecordingHandler(_time, async (number, _) =>
        {
            if (number == 0)
            {
                await gate.Task;
            }

            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        using var client = CreateClient(handler);

        Enqueue(client, "1", "2", "3");
        await handler.WaitForRequestAsync();
        Enqueue(client, "4", "5", "6", "7", "8", "9", "10");
        gate.SetResult();

        await handler.WaitForRequestAsync();
        await handler.WaitForRequestAsync();

        Assert.Equal(["1", "2", "3"], handler.Requests[0].Messages);
        Assert.Equal(["4", "5", "6"], handler.Requests[1].Messages);
        Assert.Equal(["7", "8", "9"], handler.Requests[2].Messages);

        _time.Advance(Interval);
        await handler.WaitForRequestAsync();

        Assert.Equal(["10"], handler.Requests[3].Messages);
    }

    [Fact]
    public async Task ARequest_IsSplitBeforeItExceedsMaxRequestBytes()
    {
        using var handler = new RecordingHandler(_time);
        using var client = CreateClient(handler, new LogbookOptions
        {
            Endpoint = BaseOptions.Endpoint,
            ApiKey = BaseOptions.ApiKey,
            ShutdownTimeout = ShortShutdown,
            FlushInterval = Interval,
            BatchSize = 100,
            MaxRequestBytes = 2500
        });

        Enqueue(client, new string('a', 1000), new string('b', 1000), new string('c', 1000));
        _time.Advance(Interval);
        await handler.WaitForRequestAsync();
        await handler.WaitForRequestAsync();

        Assert.All(handler.Requests, request => Assert.True(Encoding.UTF8.GetByteCount(request.Body) <= 2500));
        Assert.Equal(2, handler.Requests[0].Messages.Count);
        Assert.Single(handler.Requests[1].Messages);
    }

    [Fact]
    public async Task AnEventTooLargeForARequest_IsDropped_AndTheOthersAreSent()
    {
        using var handler = new RecordingHandler(_time);
        using var client = CreateClient(handler, new LogbookOptions
        {
            Endpoint = BaseOptions.Endpoint,
            ApiKey = BaseOptions.ApiKey,
            ShutdownTimeout = ShortShutdown,
            FlushInterval = Interval,
            BatchSize = 1,
            MaxRequestBytes = 500,
            MaxEventBytes = 10_000
        });

        Enqueue(client, new string('x', 1000), "small");
        await handler.WaitForRequestAsync();

        Assert.Equal(["small"], handler.Requests[0].Messages);
        Assert.Equal(1, client.Buffer!.DroppedCount);
    }

    [Fact]
    public async Task ARequest_IsCompressedAndCarriesTheHeadersOfTheLogApi()
    {
        using var handler = new RecordingHandler(_time);
        using var client = CreateClient(handler);

        Enqueue(client, "1", "2", "3");
        await handler.WaitForRequestAsync();

        var request = handler.Requests[0].Request;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("https://logbook.example.com/api/v1/logs"), request.RequestUri);
        Assert.Equal("the-api-key", Assert.Single(request.Headers.GetValues("X-Logbook-Key")));
        Assert.Equal("gzip", Assert.Single(request.Content!.Headers.ContentEncoding));
        Assert.Equal("application/json", request.Content.Headers.ContentType!.MediaType);
    }

    [Theory]
    [InlineData("https://example.com/logbook", "https://example.com/logbook/api/v1/logs")]
    [InlineData("https://example.com/logbook/", "https://example.com/logbook/api/v1/logs")]
    [InlineData("http://localhost:5080/", "http://localhost:5080/api/v1/logs")]
    public async Task AnEndpointBelowAPath_KeepsThePath(string endpoint, string expected)
    {
        using var handler = new RecordingHandler(_time);
        using var client = CreateClient(handler, new LogbookOptions { Endpoint = new Uri(endpoint), ApiKey = "key", BatchSize = 1 });

        Enqueue(client, "1");
        await handler.WaitForRequestAsync();

        Assert.Equal(new Uri(expected), handler.Requests[0].Request.RequestUri);
    }

    [Fact]
    public async Task AFailedRequest_KeepsTheEvents_AndTheyAreSentAgainWithoutDuplicates()
    {
        using var handler = new RecordingHandler(_time, (number, _) =>
            Task.FromResult(new HttpResponseMessage(number == 0 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Accepted)));
        using var client = CreateClient(handler);

        Enqueue(client, "1", "2", "3");
        await handler.WaitForRequestAsync();
        await AdvanceUntilAsync(() => handler.Requests.Count == 2 && client.Buffer!.Count == 0);

        Assert.Equal(["1", "2", "3"], handler.Requests[0].Messages);
        Assert.Equal(["1", "2", "3"], handler.Requests[1].Messages);
        Assert.Equal(0, client.Buffer!.DroppedCount);

        // Nothing is sent any more once the server has accepted them.
        _time.Advance(Interval);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task AThrowingHandler_DoesNotEndTheSender()
    {
        using var handler = new RecordingHandler(_time, (number, _) =>
            number < 2 ? throw new HttpRequestException("Connection refused") : Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)));
        using var client = CreateClient(handler);

        Enqueue(client, "1", "2", "3");
        await handler.WaitForRequestAsync();
        await AdvanceUntilAsync(() => client.Buffer!.Count == 0);

        Assert.Equal(["1", "2", "3"], handler.Requests[^1].Messages);
    }

    [Fact]
    public async Task ARedirect_CountsAsFailed_SoTheEventsAreNotLost()
    {
        using var handler = new RecordingHandler(_time, (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)));
        using var client = CreateClient(handler);

        Enqueue(client, "1", "2", "3");
        await handler.WaitForRequestAsync();

        await AdvanceUntilAsync(() => handler.Requests.Count >= 2);

        Assert.Equal(3, client.Buffer!.Count);
    }

    [Fact]
    public void TheDefaultHandler_DoesNotFollowRedirects()
    {
        using var handler = LogbookClient.CreateDefaultHandler();

        var allowsRedirects = handler switch
        {
            SocketsHttpHandler sockets => sockets.AllowAutoRedirect,
            HttpClientHandler client => client.AllowAutoRedirect,
            _ => throw new InvalidOperationException("Unknown handler.")
        };

        Assert.False(allowsRedirects);
    }

    [Fact]
    public async Task WhileAnOldBatchIsBeingSent_NewEventsPushItOut_AndItsDeliveryKeepsTheNewOnes()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new RecordingHandler(_time, async (number, _) =>
        {
            if (number == 0)
            {
                await gate.Task;
            }

            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        using var client = CreateClient(handler, new LogbookOptions
        {
            Endpoint = BaseOptions.Endpoint,
            ApiKey = BaseOptions.ApiKey,
            ShutdownTimeout = ShortShutdown,
            FlushInterval = Interval,
            BatchSize = 3,
            MaxQueueSize = 3
        });

        Enqueue(client, "old 1", "old 2", "old 3");
        await handler.WaitForRequestAsync();

        Enqueue(client, "new 1", "new 2", "new 3");
        Assert.Equal(3, client.Buffer!.DroppedCount);

        gate.SetResult();
        await handler.WaitForRequestAsync();

        Assert.Equal(["new 1", "new 2", "new 3"], handler.Requests[1].Messages);
    }

    [Fact]
    public async Task DuringAnOutage_OnlyTheOldestEventsAreLost_AndOnlyOnceTheBufferIsFull()
    {
        // The first two attempts fail. The body of a request is recorded before it is answered, so what the test adds
        // after seeing the first request cannot be mixed up with how that request ends.
        using var handler = new RecordingHandler(_time, (number, _) =>
            Task.FromResult(new HttpResponseMessage(number < 2 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Accepted)));
        using var client = CreateClient(handler, new LogbookOptions
        {
            Endpoint = BaseOptions.Endpoint,
            ApiKey = BaseOptions.ApiKey,
            ShutdownTimeout = ShortShutdown,
            FlushInterval = Interval,
            BatchSize = 100,
            MaxQueueSize = 5
        });

        Enqueue(client, "1", "2", "3", "4", "5");
        await handler.WaitForRequestAsync();
        Assert.Equal(0, client.Buffer!.DroppedCount);

        Enqueue(client, "6", "7");
        Assert.Equal(2, client.Buffer.DroppedCount);

        await AdvanceUntilAsync(() => client.Buffer.Count == 0);

        Assert.Equal(["1", "2", "3", "4", "5"], handler.Requests[0].Messages);
        Assert.Equal(["3", "4", "5", "6", "7"], handler.Requests[1].Messages);
        Assert.Equal(["3", "4", "5", "6", "7"], handler.Requests[2].Messages);
        Assert.Equal(2, client.Buffer.DroppedCount);
    }

    [Fact]
    public async Task Enqueue_DoesNotBlock_WhileTheServerDoesNotAnswer()
    {
        var neverAnswers = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new RecordingHandler(_time, async (_, cancellationToken) => await neverAnswers.Task.WaitAsync(cancellationToken));
        using var client = CreateClient(handler, new LogbookOptions
        {
            Endpoint = BaseOptions.Endpoint,
            ApiKey = BaseOptions.ApiKey,
            ShutdownTimeout = ShortShutdown,
            FlushInterval = Interval,
            BatchSize = 10,
            MaxQueueSize = 100
        });

        Enqueue(client, Enumerable.Range(0, 10).Select(index => "first").ToArray());
        await handler.WaitForRequestAsync();

        // If this blocked on the request that never ends, the test would never get past this loop.
        for (var index = 0; index < 100_000; index++)
        {
            client.Enqueue(new LogbookEvent(_time.GetUtcNow(), LogbookLevel.Information, "more"));
        }

        Assert.Equal(100, client.Buffer!.Count);
        Assert.Equal(100_010 - 100, client.Buffer.DroppedCount);
    }

    [Fact]
    public async Task TheSender_DoesNotInheritTheActivityOfWhoeverCreatedTheClient()
    {
        using var source = new ActivitySource(nameof(TheSender_DoesNotInheritTheActivityOfWhoeverCreatedTheClient));
        using var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var handler = new RecordingHandler(_time);
        LogbookClient client;

        using (var activity = source.StartActivity("request"))
        {
            Assert.NotNull(activity);
            client = CreateClient(handler);
        }

        using (client)
        {
            Enqueue(client, "1", "2", "3");
            await handler.WaitForRequestAsync();
        }

        Assert.Null(handler.Requests[0].CurrentActivity);
    }

    [Theory]
    [InlineData(null, "key", 1, 1000, 1, 1, 1)]
    [InlineData("/relative", "key", 1, 1000, 1, 1, 1)]
    [InlineData("ftp://example.com", "key", 1, 1000, 1, 1, 1)]
    [InlineData("https://example.com", null, 1, 1000, 1, 1, 1)]
    [InlineData("https://example.com", " ", 1, 1000, 1, 1, 1)]
    [InlineData("https://example.com", "key", 0, 1000, 1, 1, 1)]
    [InlineData("https://example.com", "key", 1, 0, 1, 1, 1)]
    [InlineData("https://example.com", "key", 1, 200_000_000, 1, 1, 1)]
    [InlineData("https://example.com", "key", 1, 1000, 0, 1, 1)]
    [InlineData("https://example.com", "key", 1, 1000, 1, 0, 1)]
    [InlineData("https://example.com", "key", 1, 1000, 1, 1, 0)]
    public void UnusableOptions_DisableTheClient_WithoutThrowing(
        string? endpoint,
        string? apiKey,
        int batchSize,
        int flushMilliseconds,
        int maxQueueSize,
        long maxQueueBytes,
        int maxRequestBytes)
    {
        using var handler = new RecordingHandler(_time);

        using var client = new LogbookClient(
            new LogbookOptions
            {
                Endpoint = endpoint is null ? null : new Uri(endpoint, UriKind.RelativeOrAbsolute),
                ApiKey = apiKey,
                BatchSize = batchSize,
                FlushInterval = TimeSpan.FromMilliseconds(flushMilliseconds),
                MaxQueueSize = maxQueueSize,
                MaxQueueBytes = maxQueueBytes,
                MaxRequestBytes = maxRequestBytes
            },
            handler,
            _time);

        Assert.False(client.IsEnabled);

        client.Enqueue(new LogbookEvent(_time.GetUtcNow(), LogbookLevel.Error, "ignored"));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void NullOptionsAndNullEvents_AreIgnored()
    {
        using var disabled = new LogbookClient(null!);
        using var handler = new RecordingHandler(_time);
        using var enabled = CreateClient(handler);

        disabled.Enqueue(new LogbookEvent(_time.GetUtcNow(), LogbookLevel.Error, "ignored"));
        enabled.Enqueue((LogbookEvent)null!);

        Assert.False(disabled.IsEnabled);
        Assert.Equal(0, enabled.Buffer!.Count);
    }

    [Fact]
    public void AfterDispose_EnqueueAndDisposeDoNothing()
    {
        using var handler = new RecordingHandler(_time);
        var client = CreateClient(handler);

        client.Dispose();
        client.Dispose();
        client.Enqueue(new LogbookEvent(_time.GetUtcNow(), LogbookLevel.Error, "ignored"));

        Assert.Equal(0, client.Buffer!.Count);
    }

    [Fact]
    public async Task Dispose_EndsARequestInFlight()
    {
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new RecordingHandler(_time, async (_, cancellationToken) =>
        {
            using var registration = cancellationToken.Register(canceled.SetResult);
            await Task.Delay(Timeout.Infinite, cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        using var client = CreateClient(handler);

        Enqueue(client, "1", "2", "3");
        await handler.WaitForRequestAsync();
        client.Dispose();

        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }

    private void Enqueue(LogbookClient client, params string[] messages)
    {
        foreach (var message in messages)
        {
            client.Enqueue(new LogbookEvent(_time.GetUtcNow(), LogbookLevel.Information, message));
        }
    }

    private LogbookClient CreateClient(RecordingHandler handler, LogbookOptions? options = null)
    {
        return new LogbookClient(options ?? BaseOptions, handler, _time);
    }

    private Task AdvanceUntilAsync(Func<bool> condition) => _time.AdvanceUntilAsync(Interval, condition);
}
