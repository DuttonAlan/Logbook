// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Globalization;
using System.Net.Http.Headers;

namespace Logbook.Sdk;

/// <summary>
///     The background loop that sends the events in the <see cref="LogEventBuffer"/> in batches.
/// </summary>
/// <remarks>
///     Every attempt reads the oldest events again, so a batch that failed gets sent without the events that were dropped
///     in the meantime. Events leave the buffer once the server has accepted them, or has said that sending them again
///     would not help.
/// </remarks>
internal sealed class LogEventSender : IDisposable
{
    private const string ApiKeyHeader = "X-Logbook-Key";
    private const string LogsPath = "/api/v1/logs";

    private static readonly MediaTypeHeaderValue JsonContentType = new("application/json");

    private readonly LogEventBuffer _buffer;
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly Uri _logsUri;
    private readonly string _apiKey;
    private readonly int _batchSize;
    private readonly LogEventLimits _limits;
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
    /// <param name="limits">The limits for the events.</param>
    /// <param name="buffer">The buffer to send from.</param>
    /// <param name="handler">The handler for the requests, or null for the default.</param>
    /// <param name="timeProvider">The clock for the waiting times.</param>
    public LogEventSender(
        LogbookOptions options,
        LogEventLimits limits,
        LogEventBuffer buffer,
        HttpMessageHandler? handler,
        TimeProvider timeProvider)
    {
        _buffer = buffer;
        _timeProvider = timeProvider;

#pragma warning disable CA2000 // The HttpClient owns the handler and disposes it.
        _httpClient = new HttpClient(handler ?? CreateDefaultHandler()) { Timeout = options.RequestTimeout };
#pragma warning restore CA2000

        _logsUri = new Uri(options.Endpoint!.GetLeftPart(UriPartial.Path).TrimEnd('/') + LogsPath);
        _apiKey = options.ApiKey!;
        _batchSize = options.BatchSize;
        _limits = limits;
        _flushInterval = options.FlushInterval;
        _shutdownTimeout = options.ShutdownTimeout;
    }

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
                _httpClient.Dispose();
                _stopping.Dispose();
                _aborting.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    /// <summary>
    ///     Creates the handler used outside of tests.
    /// </summary>
    /// <returns>The handler.</returns>
    /// <remarks>
    ///     Redirects are not followed: following one turns the POST into a GET, whose <c>200</c> would look like delivered
    ///     events, and could send the API Key to another host.
    /// </remarks>
    internal static HttpMessageHandler CreateDefaultHandler()
    {
        if (SocketsHttpHandler.IsSupported)
        {
            // With a long-lived HttpClient, the lifetime lets a server that moved to another address be found again.
            return new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        }

        return new HttpClientHandler { AllowAutoRedirect = false };
    }

    private async Task RunAsync()
    {
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

        var batch = LogEventBatchWriter.Write(events, _limits, _timeProvider.GetUtcNow());

        if (batch.Count == 0)
        {
            _buffer.DropThrough(batch.LastSequence);
            ReportLeftOut(batch);

            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _logsUri) { Content = new ByteArrayContent(batch.Body) };
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
