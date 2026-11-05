// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net;
using System.Net.Http.Headers;

using Microsoft.Extensions.Time.Testing;

namespace Logbook.Sdk.Tests;

/// <summary>
///     Checks what the client does when things go wrong: the server is down, slow or returning errors, the buffer is full,
///     and the application shuts down in the middle of it.
/// </summary>
public sealed class LogbookClientFailureTests
{
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LongEnough = TimeSpan.FromSeconds(30);

    private readonly FakeTimeProvider _time = new();

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(HttpStatusCode.UnsupportedMediaType)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task ARejectedBatch_IsDropped_AndTheNextOneGoesWithoutWaiting(HttpStatusCode status)
    {
        using var handler = new RecordingHandler(_time, (number, _) => Respond(number == 0 ? status : HttpStatusCode.Accepted));
        using var client = CreateClient(handler);

        Enqueue(client, "bad 1", "bad 2", "bad 3", "good 1", "good 2", "good 3");

        // The clock does not move, so any waiting would end this test with a failure.
        await handler.WaitForRequestAsync();
        await handler.WaitForRequestAsync();

        Assert.Equal(["bad 1", "bad 2", "bad 3"], handler.Requests[0].Messages);
        Assert.Equal(["good 1", "good 2", "good 3"], handler.Requests[1].Messages);
        await WaitForAsync(() => client.Buffer!.Count == 0);
        Assert.Equal(3, client.Buffer!.DroppedCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task ATemporaryFailure_KeepsTheEvents_AndTheyAreSentAgain(HttpStatusCode status)
    {
        using var handler = new RecordingHandler(_time, (number, _) => Respond(number == 0 ? status : HttpStatusCode.Accepted));
        using var client = CreateClient(handler);

        Enqueue(client, "1", "2", "3");
        await handler.WaitForRequestAsync();
        await _time.AdvanceUntilAsync(Step, () => client.Buffer!.Count == 0);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Requests[0].Messages, handler.Requests[1].Messages);
        Assert.Equal(0, client.Buffer!.DroppedCount);
    }

    [Fact]
    public async Task ServerDown_TheAttemptsSpreadOut()
    {
        using var handler = new RecordingHandler(_time, (_, _) => throw new HttpRequestException("Connection refused"));
        using var client = CreateClient(handler);

        Enqueue(client, "1", "2", "3");
        await handler.WaitForRequestAsync();
        await _time.AdvanceUntilAsync(TimeSpan.FromMilliseconds(100), () => handler.Requests.Count >= 6);

        var gaps = handler.Requests.Zip(handler.Requests.Skip(1), (first, second) => (second.ArrivedAt - first.ArrivedAt).TotalSeconds).ToArray();

        // Half of each doubled value is the least the jitter can make of it.
        Assert.True(gaps[0] >= 0.5, $"Gap {gaps[0]}");
        Assert.True(gaps[1] >= 1, $"Gap {gaps[1]}");
        Assert.True(gaps[2] >= 2, $"Gap {gaps[2]}");
        Assert.True(gaps[3] >= 4, $"Gap {gaps[3]}");
        Assert.True(gaps[4] >= 8, $"Gap {gaps[4]}");
        Assert.Equal(3, client.Buffer!.Count);
    }

    [Fact]
    public async Task WhileBackingOff_AFullBatchDoesNotCauseAnAttempt()
    {
        using var handler = new RecordingHandler(_time, (number, _) => Respond(number == 0 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Accepted));
        using var client = CreateClient(handler);

        Enqueue(client, "1", "2", "3");
        await handler.WaitForRequestAsync();

        Enqueue(client, "4", "5", "6");

        // Only gives a wrong early attempt the chance to happen.
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Single(handler.Requests);

        await _time.AdvanceUntilAsync(Step, () => client.Buffer!.Count == 0);

        Assert.Equal(["1", "2", "3"], handler.Requests[1].Messages);
        Assert.Equal(["4", "5", "6"], handler.Requests[2].Messages);
    }

    [Fact]
    public async Task AfterASuccess_TheBackoffStartsOver()
    {
        // Failing, failing, failing, delivered, then failing again.
        using var handler = new RecordingHandler(_time, (number, _) => Respond(number is 3 or 5 ? HttpStatusCode.Accepted : HttpStatusCode.ServiceUnavailable));
        using var client = CreateClient(handler);

        Enqueue(client, "1", "2", "3");
        await handler.WaitForRequestAsync();
        await _time.AdvanceUntilAsync(TimeSpan.FromMilliseconds(100), () => client.Buffer!.Count == 0);

        Enqueue(client, "4", "5", "6");
        await _time.AdvanceUntilAsync(TimeSpan.FromMilliseconds(100), () => client.Buffer!.Count == 0);

        var requests = handler.Requests;

        // The third wait is at least 2 s. The first one after the success is at most 1 s plus how far the clock moved in
        // the short time it took the test to notice, so it stays clearly below the wait it would be without the reset (8 s).
        Assert.True((requests[3].ArrivedAt - requests[2].ArrivedAt).TotalSeconds >= 2);
        Assert.True((requests[5].ArrivedAt - requests[4].ArrivedAt).TotalSeconds < 4);
    }

    [Fact]
    public async Task RetryAfterInSeconds_IsRespected()
    {
        using var handler = new RecordingHandler(_time, (number, _) =>
        {
            var response = new HttpResponseMessage(number == 0 ? HttpStatusCode.TooManyRequests : HttpStatusCode.Accepted);

            if (number == 0)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            }

            return Task.FromResult(response);
        });
        using var client = CreateClient(handler);

        Enqueue(client, "1", "2", "3");
        await handler.WaitForRequestAsync();
        await _time.AdvanceUntilAsync(Step, () => handler.Requests.Count >= 2);

        Assert.True(handler.Requests[1].ArrivedAt - handler.Requests[0].ArrivedAt >= TimeSpan.FromSeconds(120));
    }

    [Fact]
    public async Task RetryAfterAsADate_IsRespected()
    {
        using var handler = new RecordingHandler(_time, (number, _) =>
        {
            var response = new HttpResponseMessage(number == 0 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Accepted);

            if (number == 0)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(_time.GetUtcNow().AddSeconds(90));
            }

            return Task.FromResult(response);
        });
        using var client = CreateClient(handler);

        Enqueue(client, "1", "2", "3");
        await handler.WaitForRequestAsync();
        await _time.AdvanceUntilAsync(Step, () => handler.Requests.Count >= 2);

        Assert.True(handler.Requests[1].ArrivedAt - handler.Requests[0].ArrivedAt >= TimeSpan.FromSeconds(90));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MovedPermanently)]
    public async Task ARefusal_KeepsTheEvents_WithABackoffOfAboutAMinute(HttpStatusCode status)
    {
        using var handler = new RecordingHandler(_time, (number, _) => Respond(number < 2 ? status : HttpStatusCode.Accepted));
        using var client = CreateClient(handler);

        Enqueue(client, "1", "2", "3");
        await handler.WaitForRequestAsync();
        await _time.AdvanceUntilAsync(Step, () => client.Buffer!.Count == 0);

        var requests = handler.Requests;
        Assert.Equal(3, requests.Count);
        Assert.True(requests[1].ArrivedAt - requests[0].ArrivedAt >= TimeSpan.FromSeconds(54));
        Assert.True(requests[2].ArrivedAt - requests[1].ArrivedAt >= TimeSpan.FromSeconds(54));
        Assert.Equal(["1", "2", "3"], requests[2].Messages);
        Assert.Equal(0, client.Buffer!.DroppedCount);
    }

    [Fact]
    public async Task ASlowServer_RunsIntoTheRequestTimeout_AndTheEventsAreSentAgain()
    {
        var timedOut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new RecordingHandler(_time, async (number, cancellationToken) =>
        {
            if (number == 0)
            {
                // Runs in real time, because the HttpClient has its own timer. It ends only through the timeout.
                using var registration = cancellationToken.Register(timedOut.SetResult);
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        using var client = CreateClient(handler, options => options.RequestTimeout = TimeSpan.FromMilliseconds(50));

        Enqueue(client, "1", "2", "3");
        await timedOut.Task.WaitAsync(LongEnough, TestContext.Current.CancellationToken);
        await _time.AdvanceUntilAsync(Step, () => client.Buffer!.Count == 0);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(["1", "2", "3"], handler.Requests[1].Messages);
    }

    [Fact]
    public async Task AnExpiredEvent_IsLeftOut_AndDoesNotCostTheBatch()
    {
        using var handler = new RecordingHandler(_time);
        using var client = CreateClient(handler);

        client.Enqueue(new LogbookEvent(_time.GetUtcNow().AddDays(-8), LogbookLevel.Information, "old"));
        Enqueue(client, "1", "2");
        await handler.WaitForRequestAsync();
        await WaitForAsync(() => client.Buffer!.Count == 0);

        Assert.Equal(["1", "2"], handler.Requests[0].Messages);
        Assert.Equal(1, client.Buffer!.DroppedCount);
    }

    [Fact]
    public async Task EventsThatExpireDuringAnOutage_AreDropped_AndNewerOnesAreDelivered()
    {
        using var handler = new RecordingHandler(_time, (number, _) => Respond(number == 0 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Accepted));
        using var client = CreateClient(handler);

        Enqueue(client, "old 1", "old 2", "old 3");
        await handler.WaitForRequestAsync();

        // A week passes before the next attempt.
        _time.Advance(TimeSpan.FromDays(7));
        Enqueue(client, "new");

        await _time.AdvanceUntilAsync(TimeSpan.FromSeconds(10), () => handler.Requests.Count == 2 && client.Buffer!.Count == 0);

        Assert.Equal(["new"], handler.Requests[^1].Messages);
        Assert.Equal(3, client.Buffer!.DroppedCount);
    }

    [Fact]
    public async Task ABufferFullOfExpiredEvents_IsNotSentAtAll()
    {
        using var handler = new RecordingHandler(_time);
        using var client = CreateClient(handler);

        client.Enqueue(new LogbookEvent(_time.GetUtcNow().AddDays(-8), LogbookLevel.Information, "1"));
        client.Enqueue(new LogbookEvent(_time.GetUtcNow().AddDays(-8), LogbookLevel.Information, "2"));
        client.Enqueue(new LogbookEvent(_time.GetUtcNow().AddDays(-8), LogbookLevel.Information, "3"));

        await WaitForAsync(() => client.Buffer!.Count == 0);

        Assert.Empty(handler.Requests);
        Assert.Equal(3, client.Buffer!.DroppedCount);
    }

    [Fact]
    public async Task AMessageMuchLargerThanTheBuffer_IsCutWhenAdded_SoItFits()
    {
        using var handler = new RecordingHandler(_time);
        using var client = CreateClient(handler, options => options.MaxQueueBytes = 1024 * 1024);

        Enqueue(client, new string('x', 10_000_000), "2", "3");
        await handler.WaitForRequestAsync();

        Assert.Equal(65_536, handler.Requests[0].Messages[0].Length);
        Assert.Contains(LogEventGuard.TruncatedProperty, handler.Requests[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispose_SendsWhatIsLeft_InBatches_WithoutTheClockMoving()
    {
        using var handler = new RecordingHandler(_time);
        using var client = CreateClient(handler, options => options.ShutdownTimeout = LongEnough);

        // The third batch is partial, so it would normally wait for the flush interval.
        Enqueue(client, "1", "2", "3", "4", "5", "6", "7");
        client.Dispose();

        Assert.Equal(["1", "2", "3", "4", "5", "6", "7"], handler.Requests.SelectMany(request => request.Messages));
        Assert.Equal(0, client.Buffer!.Count);
        Assert.Equal(0, client.Buffer.DroppedCount);
    }

    [Fact]
    public async Task Dispose_DuringABackoff_DoesNotWaitForIt_AndGivesUpAfterOneMoreFailure()
    {
        using var handler = new RecordingHandler(_time, (_, _) => Respond(HttpStatusCode.ServiceUnavailable));
        using var client = CreateClient(handler, options => options.ShutdownTimeout = LongEnough);

        Enqueue(client, "1", "2", "3");
        await handler.WaitForRequestAsync();

        // The clock does not move, so this returns only if it does not wait for the backoff.
        client.Dispose();

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(0, client.Buffer!.Count);
        Assert.Equal(3, client.Buffer.DroppedCount);
    }

    [Fact]
    public async Task Dispose_LetsARequestUnderWayFinish()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = false;
        using var handler = new RecordingHandler(_time, async (_, cancellationToken) =>
        {
            await gate.Task;
            canceled = cancellationToken.IsCancellationRequested;

            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        using var client = CreateClient(handler, options => options.ShutdownTimeout = LongEnough);

        Enqueue(client, "1", "2", "3");
        await handler.WaitForRequestAsync();

        var disposing = Task.Run(client.Dispose, TestContext.Current.CancellationToken);
        gate.SetResult();
        await disposing.WaitAsync(LongEnough, TestContext.Current.CancellationToken);

        Assert.False(canceled);
        Assert.Single(handler.Requests);
        Assert.Equal(0, client.Buffer!.DroppedCount);
    }

    [Fact]
    public async Task Dispose_WithAServerThatDoesNotAnswer_EndsAfterTheShutdownTimeout_AndCountsTheLoss()
    {
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new RecordingHandler(_time, async (_, cancellationToken) =>
        {
            using var registration = cancellationToken.Register(canceled.SetResult);
            await Task.Delay(Timeout.Infinite, cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        using var client = CreateClient(handler, options => options.ShutdownTimeout = TimeSpan.FromMilliseconds(50));

        Enqueue(client, "1", "2", "3");
        await handler.WaitForRequestAsync();

        // The request timeout is 30 seconds, so returning at all proves the shutdown timeout works. Test frameworks hang
        // on a bug here rather than fail, so it runs on the side.
        var disposing = Task.Run(client.Dispose, TestContext.Current.CancellationToken);
        await disposing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await canceled.Task.WaitAsync(LongEnough, TestContext.Current.CancellationToken);

        await WaitForAsync(() => client.Buffer!.DroppedCount == 3);
    }

    [Fact]
    public async Task Dispose_WithAShutdownTimeoutOfZero_DoesNotWait()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new RecordingHandler(_time, async (_, cancellationToken) =>
        {
            await gate.Task.WaitAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        using var client = CreateClient(handler, options => options.ShutdownTimeout = TimeSpan.Zero);

        Enqueue(client, "1", "2", "3");
        await handler.WaitForRequestAsync();

        client.Dispose();

        Assert.False(gate.Task.IsCompleted);
        gate.TrySetCanceled(TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> Respond(HttpStatusCode status) => Task.FromResult(new HttpResponseMessage(status));

    // For what the sender does on its own after a request. The limit is only there so that a bug ends in a failure.
    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limit.CancelAfter(LongEnough);

        while (!condition())
        {
            await Task.Delay(1, limit.Token);
        }
    }

    private void Enqueue(LogbookClient client, params string[] messages)
    {
        foreach (var message in messages)
        {
            client.Enqueue(new LogbookEvent(_time.GetUtcNow(), LogbookLevel.Information, message));
        }
    }

    private LogbookClient CreateClient(RecordingHandler handler, Action<LogbookOptions>? configure = null)
    {
        var options = new LogbookOptions
        {
            Endpoint = new Uri("https://logbook.example.com"),
            ApiKey = "the-api-key",
            BatchSize = 3,
            FlushInterval = TimeSpan.FromSeconds(2),
            ShutdownTimeout = TimeSpan.FromMilliseconds(50)
        };

        configure?.Invoke(options);

        return new LogbookClient(options, handler, _time);
    }
}
