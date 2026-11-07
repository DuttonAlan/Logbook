// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Diagnostics;
using System.Text.Json;

using Microsoft.Extensions.Time.Testing;

namespace Logbook.Sdk.Tests;

/// <summary>
///     Checks that spans are made acceptable to the server, and travel next to Log Events without getting in their way.
/// </summary>
public sealed class LogbookSpanTests
{
    private static readonly LogbookOptions BaseOptions = new()
    {
        Endpoint = new Uri("https://logbook.example.com/base/"),
        ApiKey = "the-api-key",
        BatchSize = 2,
        FlushInterval = TimeSpan.FromHours(1),
        ShutdownTimeout = TimeSpan.FromSeconds(5)
    };

    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void ValuesTheServerRejects_AreMadeAcceptable()
    {
        var span = new LogbookSpan(
            _time.GetUtcNow(),
            LogbookSpanKind.Request,
            "  ",
            double.NaN,
            success: true,
            traceId: null,
            spanId: " ",
            data: new string('d', 10_000),
            target: new string('t', 1000));

        Assert.Equal(LogbookSpan.UnnamedName, span.Name);
        Assert.Equal(0, span.DurationMilliseconds);
        Assert.Equal(32, span.TraceId.Length);
        Assert.Equal(16, span.SpanId.Length);
        Assert.Equal(8192, span.Data!.Length);
        Assert.Equal(500, span.Target!.Length);
        Assert.Equal(0, new LogbookSpan(_time.GetUtcNow(), LogbookSpanKind.Request, "a", -5, true).DurationMilliseconds);
    }

    [Fact]
    public void Write_AllFields_InTheFormatOfTheApi()
    {
        var timestamp = _time.GetUtcNow();
        var span = new LogbookSpan(
            timestamp,
            LogbookSpanKind.Dependency,
            "GET payments.example.com",
            12.5,
            success: false,
            "4bf92f3577b34da6a3ce929d0e0e4736",
            "00f067aa0ba902b7",
            "53995c3f42cd8ad8",
            "Production",
            502,
            "HTTP",
            "payments.example.com",
            "https://payments.example.com/charge");

        var batch = SpanBatchWriter.Write([new BufferedItem<LogbookSpan>(span, 1, 100, 0)], new LogEventLimits(BaseOptions), timestamp);

        Assert.Equal(1, batch.Count);

        using var document = JsonDocument.Parse(Decompress(batch.Body));
        var element = Assert.Single(document.RootElement.EnumerateArray());

        Assert.Equal("Dependency", element.GetProperty("kind").GetString());
        Assert.Equal("GET payments.example.com", element.GetProperty("name").GetString());
        Assert.Equal(12.5, element.GetProperty("durationMs").GetDouble());
        Assert.False(element.GetProperty("success").GetBoolean());
        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", element.GetProperty("traceId").GetString());
        Assert.Equal("00f067aa0ba902b7", element.GetProperty("spanId").GetString());
        Assert.Equal("53995c3f42cd8ad8", element.GetProperty("parentSpanId").GetString());
        Assert.Equal("Production", element.GetProperty("environment").GetString());
        Assert.Equal(502, element.GetProperty("statusCode").GetInt32());
        Assert.Equal("HTTP", element.GetProperty("dependencyType").GetString());
        Assert.Equal("payments.example.com", element.GetProperty("target").GetString());
        Assert.Equal("https://payments.example.com/charge", element.GetProperty("data").GetString());
    }

    [Fact]
    public void Write_LeavesOutSpansOutsideOfTheTimeWindow()
    {
        var now = _time.GetUtcNow();
        var old = new LogbookSpan(now.AddDays(-8), LogbookSpanKind.Request, "old", 1, true);
        var current = new LogbookSpan(now, LogbookSpanKind.Request, "current", 1, true);

        var batch = SpanBatchWriter.Write(
            [new BufferedItem<LogbookSpan>(old, 1, 100, 0), new BufferedItem<LogbookSpan>(current, 2, 100, 0)],
            new LogEventLimits(BaseOptions),
            now);

        Assert.Equal(1, batch.Count);
        Assert.Equal(1, batch.ExpiredCount);
        Assert.Equal(2, batch.LastSequence);
    }

    [Fact]
    public async Task Spans_GoToTheirOwnEndpoint_AndLogEventsToTheirs()
    {
        using var handler = new RecordingHandler(_time);
        using var client = new LogbookClient(BaseOptions, handler, _time);

        client.Enqueue(new LogbookSpan(_time.GetUtcNow(), LogbookSpanKind.Request, "1", 1, true));
        client.Enqueue(new LogbookSpan(_time.GetUtcNow(), LogbookSpanKind.Request, "2", 1, true));
        await handler.WaitForRequestAsync();

        client.Enqueue(new LogbookEvent(_time.GetUtcNow(), LogbookLevel.Information, "1"));
        client.Enqueue(new LogbookEvent(_time.GetUtcNow(), LogbookLevel.Information, "2"));
        await handler.WaitForRequestAsync();

        var paths = handler.Requests.Select(request => request.Request.RequestUri!.AbsolutePath).ToList();

        Assert.Equal(["/base/api/v1/logs", "/base/api/v1/spans"], paths.Order());
        Assert.All(handler.Requests, request => Assert.True(request.Request.Headers.Contains("X-Logbook-Key")));
    }

    [Fact]
    public void FullSpanBuffer_DropsSpans_NotLogEvents()
    {
        var options = new LogbookOptions
        {
            Endpoint = BaseOptions.Endpoint,
            ApiKey = BaseOptions.ApiKey,
            MaxQueueSize = 2,
            BatchSize = 100,
            FlushInterval = TimeSpan.FromHours(1),
            ShutdownTimeout = TimeSpan.Zero
        };

        using var handler = new RecordingHandler(_time);
        using var client = new LogbookClient(options, handler, _time);

        client.Enqueue(new LogbookEvent(_time.GetUtcNow(), LogbookLevel.Error, "kept"));

        foreach (var index in Enumerable.Range(0, 5))
        {
            client.Enqueue(new LogbookSpan(_time.GetUtcNow(), LogbookSpanKind.Request, $"{index}", 1, true));
        }

        Assert.Equal(1, client.Buffer!.Count);
        Assert.Equal(0, client.Buffer.DroppedCount);
        Assert.Equal(3, client.SpanBuffer!.DroppedCount);
    }

    [Fact]
    public void Dispose_SendsTheSpansThatAreLeft()
    {
        using var handler = new RecordingHandler(_time);

        var client = new LogbookClient(BaseOptions, handler, _time);

        client.Enqueue(new LogbookSpan(_time.GetUtcNow(), LogbookSpanKind.Request, "left", 1, true));
        client.Dispose();

        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/spans", request.Request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispose_WithBothServersSlow_WaitsForTheTimeoutOnce()
    {
        var timeout = TimeSpan.FromMilliseconds(500);

        var options = new LogbookOptions
        {
            Endpoint = BaseOptions.Endpoint,
            ApiKey = BaseOptions.ApiKey,
            BatchSize = 1,
            ShutdownTimeout = timeout
        };

        using var handler = new RecordingHandler(
            TimeProvider.System,
            async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);

                throw new InvalidOperationException("Never reached.");
            });

        var client = new LogbookClient(options, handler, TimeProvider.System);

        client.Enqueue(new LogbookEvent(DateTimeOffset.UtcNow, LogbookLevel.Information, "log"));
        client.Enqueue(new LogbookSpan(DateTimeOffset.UtcNow, LogbookSpanKind.Request, "span", 1, true));

        await handler.WaitForRequestAsync();
        await handler.WaitForRequestAsync();

        var watch = Stopwatch.StartNew();
        client.Dispose();
        watch.Stop();

        // Well below twice the timeout, while leaving room for a busy machine.
        Assert.True(watch.Elapsed < timeout * 1.9, $"Disposing took {watch.Elapsed}.");
    }

    [Fact]
    public async Task TheSendLoop_IsMarkedAsSending_AndTheApplicationIsNot()
    {
        var wasMarked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var handler = new RecordingHandler(
            _time,
            (_, _) =>
            {
                wasMarked.TrySetResult(SendingFlow.IsCurrent);

                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Accepted));
            });

        using var client = new LogbookClient(BaseOptions, handler, _time);

        client.Enqueue(new LogbookSpan(_time.GetUtcNow(), LogbookSpanKind.Request, "1", 1, true));
        client.Enqueue(new LogbookSpan(_time.GetUtcNow(), LogbookSpanKind.Request, "2", 1, true));

        Assert.True(await wasMarked.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        Assert.False(SendingFlow.IsCurrent);
    }

    [Fact]
    public void DisabledClient_IgnoresSpans()
    {
        using var client = new LogbookClient(new LogbookOptions());

        client.Enqueue(new LogbookSpan(DateTimeOffset.UtcNow, LogbookSpanKind.Request, "ignored", 1, true));
        client.Enqueue((LogbookSpan)null!);

        Assert.False(client.IsEnabled);
        Assert.Null(client.SpanBuffer);
    }

    private static string Decompress(byte[] body)
    {
        using var input = new MemoryStream(body);
        using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);

        return reader.ReadToEnd();
    }
}
