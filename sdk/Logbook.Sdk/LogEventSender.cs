// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net.Http.Headers;

namespace Logbook.Sdk;

/// <summary>
///     The background loop that sends the events in the <see cref="LogEventBuffer"/> in batches.
/// </summary>
/// <remarks>
///     Every attempt reads the oldest events again, so a batch that failed gets sent without the events that were dropped
///     in the meantime. Events leave the buffer only after the server has accepted them.
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
    private readonly int _maxRequestBytes;
    private readonly TimeSpan _flushInterval;
    private readonly CancellationTokenSource _stopping = new();

    private Task _loop = Task.CompletedTask;

    /// <summary>
    ///     Creates the sender.
    /// </summary>
    /// <param name="options">The options, which must be valid.</param>
    /// <param name="buffer">The buffer to send from.</param>
    /// <param name="handler">The handler for the requests, or null for the default.</param>
    /// <param name="timeProvider">The clock for the waiting times.</param>
    public LogEventSender(
        LogbookOptions options,
        LogEventBuffer buffer,
        HttpMessageHandler? handler,
        TimeProvider timeProvider)
    {
        _buffer = buffer;
        _timeProvider = timeProvider;

#pragma warning disable CA2000 // The HttpClient owns the handler and disposes it.
        _httpClient = new HttpClient(handler ?? CreateDefaultHandler());
#pragma warning restore CA2000

        _logsUri = new Uri(options.Endpoint!.GetLeftPart(UriPartial.Path).TrimEnd('/') + LogsPath);
        _apiKey = options.ApiKey!;
        _batchSize = options.BatchSize;
        _maxRequestBytes = options.MaxRequestBytes;
        _flushInterval = options.FlushInterval;
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
    ///     Stops the loop without waiting for it. Events that were not sent yet are lost.
    /// </summary>
    public void Dispose()
    {
        _stopping.Cancel();

        _ = _loop.ContinueWith(
            _ =>
            {
                _httpClient.Dispose();
                _stopping.Dispose();
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
#if NETSTANDARD2_0
        return new HttpClientHandler { AllowAutoRedirect = false };
#else
        if (SocketsHttpHandler.IsSupported)
        {
            // With a long-lived HttpClient, the lifetime lets a server that moved to another address be found again.
            return new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        }

        return new HttpClientHandler { AllowAutoRedirect = false };
#endif
    }

    private async Task RunAsync()
    {
        var stopping = _stopping.Token;

        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await WaitUntilBatchIsDueAsync(stopping).ConfigureAwait(false);

                if (await TrySendNextBatchAsync(stopping).ConfigureAwait(false))
                {
                    continue;
                }
            }
#pragma warning disable CA1031 // The client must never throw into the host application.
            catch (Exception)
#pragma warning restore CA1031
            {
                // Whatever happened, the loop must go on and the host application must not notice. The events stay in
                // the buffer, so this is a failed attempt like any other.
            }

            // Not observing the task: when stopping, it ends as canceled and the loop condition ends the loop.
            await Task.WhenAny(DelayAsync(_flushInterval, stopping)).ConfigureAwait(false);
        }
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

    private async Task<bool> TrySendNextBatchAsync(CancellationToken stopping)
    {
        var events = _buffer.Peek(_batchSize);

        if (events.Count == 0)
        {
            return true;
        }

        var batch = LogEventBatchWriter.Write(events, _maxRequestBytes);

        if (batch.Count == 0)
        {
            // The event could never be sent, so waiting would only hold up the others.
            _buffer.DropThrough(batch.LastSequence);
            return true;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _logsUri) { Content = new ByteArrayContent(batch.Body) };
        request.Headers.TryAddWithoutValidation(ApiKeyHeader, _apiKey);
        request.Content.Headers.ContentType = JsonContentType;
        request.Content.Headers.ContentEncoding.Add("gzip");

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stopping)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        _buffer.RemoveThrough(batch.LastSequence);

        return true;
    }

    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
#if NETSTANDARD2_0
        return _timeProvider.Delay(delay, cancellationToken);
#else
        return Task.Delay(delay, _timeProvider, cancellationToken);
#endif
    }
}
