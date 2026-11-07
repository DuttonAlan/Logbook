// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Globalization;
using System.Net.Http.Headers;

namespace Logbook.Sdk;

/// <summary>
///     The background loop that sends the events in a <see cref="TelemetryBuffer{T}"/> in batches.
/// </summary>
/// <remarks>
///     Every attempt reads the oldest events again, so a batch that failed gets sent without the events that were dropped
///     in the meantime. Events leave the buffer once the server has accepted them, or has said that sending them again
///     would not help.
/// </remarks>
internal sealed class TelemetrySender<T> : IDisposable
    where T : ITelemetryItem
{
    private const string ApiKeyHeader = "X-Logbook-Key";

    private static readonly MediaTypeHeaderValue JsonContentType = new("application/json");

    private readonly TelemetryBuffer<T> _buffer;
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly Uri _uri;
    private readonly string _apiKey;
    private readonly int _batchSize;
    private readonly Func<IReadOnlyList<BufferedItem<T>>, DateTimeOffset, LogEventBatch> _writeBatch;
    private readonly TimeSpan _flushInterval;
    private readonly TimeSpan _shutdownTimeout;
    private readonly RetryBackoff _backoff = new();

    // Ends all waiting, so what is left gets sent. A request that is under way may still finish.
    private readonly CancellationTokenSource _stopping = new();

    // Ends a request that is under way, when the time for the shutdown has run out.
    private readonly CancellationTokenSource _aborting = new();

    private Task _loop = Task.CompletedTask;

    /// <summary>
    ///     Creates the sender.
    /// </summary>
    /// <param name="options">The options, which must be valid.</param>
    /// <param name="httpClient">The client to send with, which the caller disposes after the sender has ended.</param>
    /// <param name="path">The path of the endpoint on the server.</param>
    /// <param name="buffer">The buffer to send from.</param>
    /// <param name="writeBatch">Writes as many of the events as fit into one request.</param>
    /// <param name="timeProvider">The clock for the waiting times.</param>
    public TelemetrySender(
        LogbookOptions options,
        HttpClient httpClient,
        string path,
        TelemetryBuffer<T> buffer,
        Func<IReadOnlyList<BufferedItem<T>>, DateTimeOffset, LogEventBatch> writeBatch,
        TimeProvider timeProvider)
    {
        _buffer = buffer;
        _timeProvider = timeProvider;
        _httpClient = httpClient;
        _uri = new Uri(options.Endpoint!.GetLeftPart(UriPartial.Path).TrimEnd('/') + path);
        _apiKey = options.ApiKey!;
        _batchSize = options.BatchSize;
        _writeBatch = writeBatch;
        _flushInterval = options.FlushInterval;
        _shutdownTimeout = options.ShutdownTimeout;
    }

    /// <summary>
    ///     Ends when the loop has ended, and with it all use of the HttpClient.
    /// </summary>
    public Task Completion => _loop;

    /// <summary>
    ///     Starts the loop in the background.
    /// </summary>
    public void Start()
    {
        // Otherwise the loop would keep the Activity, scopes and other ambient state of whoever created the client, for
        // as long as the application runs.
        using (ExecutionContext.SuppressFlow())
        {
            _loop = Task.Run(RunAsync);
        }
    }

    /// <summary>
    ///     Sends what is left, for at most the shutdown timeout, and stops the loop. Events that could not be sent by then
    ///     are lost.
    /// </summary>
    public void Dispose()
    {
        // Not on this thread: canceling can run the loop right here, and a slow start of a request would then not be
        // limited by the timeout below.
        _ = Task.Run(_stopping.Cancel);

        if (!_loop.Wait(_shutdownTimeout))
        {
            _aborting.Cancel();
        }

        _ = _loop.ContinueWith(
            _ =>
            {
                _stopping.Dispose();
                _aborting.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    private async Task RunAsync()
    {
        SendingFlow.Mark();

        var stopping = _stopping.Token;

        while (!stopping.IsCancellationRequested)
        {
            TimeSpan? retryDelay;

            try
            {
                await WaitUntilBatchIsDueAsync(stopping).ConfigureAwait(false);

                retryDelay = await SendNextBatchAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // The client must never throw into the host application.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                // Whatever happened, the loop must go on and the host application must not notice. The events stay in
                // the buffer, so this is a failed attempt like any other.
                retryDelay = _backoff.NextDelay();
                LogbookEventSource.Log.RequestFailed(exception.GetType().Name, 0, ToMilliseconds(retryDelay.Value));
            }

            ReportFullBuffer();

            if (retryDelay is { } delay)
            {
                // Not observing the task: when stopping, it ends as canceled and the loop condition ends the loop.
                await Task.WhenAny(DelayAsync(delay, stopping)).ConfigureAwait(false);
            }
        }

        await SendWhatIsLeftAsync().ConfigureAwait(false);
    }

    // One failure ends this: with the server down, there is no point in using up the time of the shutdown.
    private async Task SendWhatIsLeftAsync()
    {
        try
        {
            while (_buffer.Count > 0 && await SendNextBatchAsync().ConfigureAwait(false) is null)
            {
            }
        }
#pragma warning disable CA1031 // The client must never throw into the host application.
        catch (Exception)
#pragma warning restore CA1031
        {
            // The time ran out, or something unexpected happened. Either way, what is left is reported below.
        }

        ReportFullBuffer();
        ReportDropped(LogbookEventSource.ReasonShutdown, _buffer.DropThrough(long.MaxValue));
    }

    private async Task WaitUntilBatchIsDueAsync(CancellationToken stopping)
    {
        while (!_buffer.IsBatchDue(_flushInterval, out var wait, out var signal))
        {
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(stopping);

            await Task.WhenAny(signal, DelayAsync(wait ?? Timeout.InfiniteTimeSpan, waiting.Token)).ConfigureAwait(false);

            stopping.ThrowIfCancellationRequested();

            // Ends the timer, in case the signal came first.
            waiting.Cancel();
        }
    }

    /// <summary>
    ///     Sends the oldest events.
    /// </summary>
    /// <returns>The time to wait before trying again if the attempt failed, or null if the loop can go on.</returns>
    private async Task<TimeSpan?> SendNextBatchAsync()
    {
        var events = _buffer.Peek(_batchSize);

        if (events.Count == 0)
        {
            return null;
        }

        var batch = _writeBatch(events, _timeProvider.GetUtcNow());

        if (batch.Count == 0)
        {
            _buffer.DropThrough(batch.LastSequence);
            ReportLeftOut(batch);

            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _uri) { Content = new ByteArrayContent(batch.Body) };
        request.Headers.TryAddWithoutValidation(ApiKeyHeader, _apiKey);
        request.Content.Headers.ContentType = JsonContentType;
        request.Content.Headers.ContentEncoding.Add("gzip");

        try
        {
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _aborting.Token)
                .ConfigureAwait(false);

            return HandleResponse(response, batch);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException && !_aborting.IsCancellationRequested)
        {
            // The server is down or did not answer in time.
            var delay = _backoff.NextDelay();
            LogbookEventSource.Log.RequestFailed(exception.GetType().Name, batch.Count, ToMilliseconds(delay));

            return delay;
        }
    }

    private TimeSpan? HandleResponse(HttpResponseMessage response, LogEventBatch batch)
    {
        var statusCode = (int)response.StatusCode;

        switch (ClassifyResponse(statusCode))
        {
            case ResponseKind.Delivered:
                _backoff.Reset();
                _buffer.RemoveThrough(batch.LastSequence, batch.ExpiredCount + batch.TooLargeCount);
                ReportLeftOut(batch);
                LogbookEventSource.Log.BatchSent(batch.Count, batch.Body.Length);

                if (batch.TruncatedCount > 0)
                {
                    LogbookEventSource.Log.EventsTruncated(batch.TruncatedCount);
                }

                return null;

            case ResponseKind.Rejected:
                // The same batch would fail again. This is not an outage, so the next batch can go right away.
                _backoff.Reset();
                _buffer.DropThrough(batch.LastSequence);
                ReportLeftOut(batch);
                LogbookEventSource.Log.BatchRejected(statusCode, batch.Count);

                return null;

            case ResponseKind.Refused:
                var longDelay = _backoff.NextRefusedDelay();
                LogbookEventSource.Log.RequestRefused(statusCode, batch.Count, ToMilliseconds(longDelay));

                return longDelay;

            default:
                var delay = _backoff.NextDelay(GetRetryAfter(response));
                LogbookEventSource.Log.RequestFailed(statusCode.ToString(CultureInfo.InvariantCulture), batch.Count, ToMilliseconds(delay));

                return delay;
        }
    }

    private static ResponseKind ClassifyResponse(int statusCode)
    {
        return statusCode switch
        {
            >= 200 and < 300 => ResponseKind.Delivered,

            // Bad request, content too large, unsupported media type, unprocessable content.
            400 or 413 or 415 or 422 => ResponseKind.Rejected,

            // Request timeout, too many requests, and the server or a proxy in front of it having a problem.
            408 or 429 or >= 500 => ResponseKind.Temporary,

            // A wrong or revoked API Key, a wrong address, a redirect. The events are not at fault and someone may fix this,
            // but not soon, so asking again every few seconds would only add load.
            _ => ResponseKind.Refused
        };
    }

    private TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;

        return retryAfter?.Delta ?? (retryAfter?.Date - _timeProvider.GetUtcNow());
    }

    private void ReportFullBuffer()
    {
        ReportDropped(LogbookEventSource.ReasonBufferFull, _buffer.TakeUnreportedFullCount());
    }

    private static void ReportLeftOut(LogEventBatch batch)
    {
        ReportDropped(LogbookEventSource.ReasonExpired, batch.ExpiredCount);
        ReportDropped(LogbookEventSource.ReasonTooLarge, batch.TooLargeCount);
    }

    private static void ReportDropped(string reason, int count)
    {
        if (count > 0)
        {
            LogbookEventSource.Log.EventsDropped(reason, count);
        }
    }

    private static int ToMilliseconds(TimeSpan delay) => (int)delay.TotalMilliseconds;

    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, _timeProvider, cancellationToken);

    private enum ResponseKind
    {
        Delivered,
        Rejected,
        Temporary,
        Refused
    }
}
