// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Sdk;

/// <summary>
///     Sends events to a Logbook server: they wait in a bounded in-memory buffer and are sent in batches in the background.
/// </summary>
/// <remarks>
///     The client never throws into the host application, and only makes it wait when it is disposed, for at most
///     <see cref="LogbookOptions.ShutdownTimeout"/>. With options it cannot work with, e.g. without an
///     <see cref="LogbookOptions.Endpoint"/>, it stays disabled and ignores all events. Create one client and keep it for
///     the lifetime of the application. What it does can be followed through the <c>EventSource</c> named <c>Logbook-Sdk</c>.
///     Log Events, spans and metric points have a buffer each, with the same limits, because they go to different endpoints,
///     and so that requests of a busy application cannot push out the Log Events that explain what went wrong.
/// </remarks>
public sealed class LogbookClient : IDisposable
{
    private const string LogsPath = "/api/v1/logs";
    private const string SpansPath = "/api/v1/spans";
    private const string MetricsPath = "/api/v1/metrics";

    private readonly TelemetryBuffer<LogbookEvent>? _buffer;
    private readonly TelemetrySender<LogbookEvent>? _sender;
    private readonly LogEventLimits? _limits;
    private readonly TelemetryBuffer<LogbookSpan>? _spanBuffer;
    private readonly TelemetrySender<LogbookSpan>? _spanSender;
    private readonly TelemetryBuffer<LogbookMetric>? _metricBuffer;
    private readonly TelemetrySender<LogbookMetric>? _metricSender;
    private readonly HttpClient? _httpClient;
    private readonly TimeSpan _shutdownTimeout;

    private int _spanSenderStarted;
    private int _metricSenderStarted;
    private int _disposed;

    /// <summary>
    ///     Creates the client and starts sending in the background.
    /// </summary>
    /// <param name="options">The options. They are read once, so later changes have no effect.</param>
    public LogbookClient(LogbookOptions options)
        : this(options, handler: null, TimeProvider.System)
    {
    }

    /// <summary>
    ///     Creates the client with the parts tests replace.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="handler">The handler for the requests, or null for the default.</param>
    /// <param name="timeProvider">The clock for the waiting times.</param>
    internal LogbookClient(LogbookOptions options, HttpMessageHandler? handler, TimeProvider timeProvider)
    {
        var invalidSetting = options is null ? nameof(options) : options.FindInvalidSetting();

        if (invalidSetting is not null)
        {
            LogbookEventSource.Log.ClientDisabled(invalidSetting);
            return;
        }

        try
        {
            var limits = new LogEventLimits(options!);

#pragma warning disable CA2000 // The HttpClient owns the handler and disposes it.
            var httpClient = new HttpClient(handler ?? CreateDefaultHandler()) { Timeout = options!.RequestTimeout };
#pragma warning restore CA2000

            var buffer = CreateBuffer<LogbookEvent>(options, timeProvider);
            var spanBuffer = CreateBuffer<LogbookSpan>(options, timeProvider);
            var metricBuffer = CreateBuffer<LogbookMetric>(options, timeProvider);

            var sender = new TelemetrySender<LogbookEvent>(
                options,
                httpClient,
                LogsPath,
                buffer,
                (events, now) => LogEventBatchWriter.Write(events, limits, now),
                timeProvider);

            // Started with the first span, as most applications without request tracking never send one.
            var spanSender = new TelemetrySender<LogbookSpan>(
                options,
                httpClient,
                SpansPath,
                spanBuffer,
                (spans, now) => SpanBatchWriter.Write(spans, limits, now),
                timeProvider);

            // Started with the first metric point, like the spans.
            var metricSender = new TelemetrySender<LogbookMetric>(
                options,
                httpClient,
                MetricsPath,
                metricBuffer,
                (metrics, now) => MetricBatchWriter.Write(metrics, limits, now),
                timeProvider);

            sender.Start();

            _limits = limits;
            _buffer = buffer;
            _sender = sender;
            _spanBuffer = spanBuffer;
            _spanSender = spanSender;
            _metricBuffer = metricBuffer;
            _metricSender = metricSender;
            _httpClient = httpClient;
            _shutdownTimeout = options.ShutdownTimeout;
        }
#pragma warning disable CA1031 // The client must never throw into the host application.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // E.g. a platform without support for the default handler. Logging is not worth failing the host for.
            LogbookEventSource.Log.ClientDisabled(exception.GetType().Name);
        }
    }

    /// <summary>
    ///     Whether the client has usable options and sends events. If not, the <c>EventSource</c> has reported why.
    /// </summary>
    public bool IsEnabled => _buffer is not null;

    /// <summary>
    ///     The buffer of the client, or null if it is disabled.
    /// </summary>
    internal TelemetryBuffer<LogbookEvent>? Buffer => _buffer;

    /// <summary>
    ///     The buffer of the spans, or null if the client is disabled.
    /// </summary>
    internal TelemetryBuffer<LogbookSpan>? SpanBuffer => _spanBuffer;

    /// <summary>
    ///     The buffer of the metric points, or null if the client is disabled.
    /// </summary>
    internal TelemetryBuffer<LogbookMetric>? MetricBuffer => _metricBuffer;

    /// <summary>
    ///     Adds an event to the buffer and returns immediately, dropping the oldest events if the buffer is full.
    /// </summary>
    /// <param name="logEvent">The event. Null is ignored.</param>
    public void Enqueue(LogbookEvent logEvent)
    {
        if (logEvent is null || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            if (_buffer is not null)
            {
                // Only the cheap part of the guard runs here. It also keeps a huge message from pushing out other events.
                _buffer.Add(LogEventGuard.CutLongMessages(logEvent, _limits!));
            }
        }
#pragma warning disable CA1031 // The client must never throw into the host application.
        catch (Exception)
#pragma warning restore CA1031
        {
            // E.g. out of memory while the buffer grows. Losing one event is better than failing the caller.
        }
    }

    /// <summary>
    ///     Adds a span to its buffer and returns immediately, dropping the oldest spans if the buffer is full.
    /// </summary>
    /// <param name="span">The span. Null is ignored.</param>
    public void Enqueue(LogbookSpan span)
    {
        if (span is null || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            if (_spanBuffer is not null)
            {
                _spanBuffer.Add(span);

                if (Interlocked.Exchange(ref _spanSenderStarted, 1) == 0)
                {
                    _spanSender!.Start();
                }
            }
        }
#pragma warning disable CA1031 // The client must never throw into the host application.
        catch (Exception)
#pragma warning restore CA1031
        {
            // E.g. out of memory while the buffer grows. Losing one span is better than failing the caller.
        }
    }

    /// <summary>
    ///     Adds an aggregated metric point to its buffer and returns immediately, dropping the oldest metric points if the
    ///     buffer is full. To record single measurements, use <see cref="LogbookMetrics"/>, which aggregates them.
    /// </summary>
    /// <param name="metric">The metric point. Null is ignored.</param>
    public void Enqueue(LogbookMetric metric)
    {
        if (metric is null || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            if (_metricBuffer is not null)
            {
                _metricBuffer.Add(metric);

                if (Interlocked.Exchange(ref _metricSenderStarted, 1) == 0)
                {
                    _metricSender!.Start();
                }
            }
        }
#pragma warning disable CA1031 // The client must never throw into the host application.
        catch (Exception)
#pragma warning restore CA1031
        {
            // E.g. out of memory while the buffer grows. Losing one metric point is better than failing the caller.
        }
    }

    /// <summary>
    ///     Sends the remaining events and stops sending in the background. This waits for at most
    ///     <see cref="LogbookOptions.ShutdownTimeout"/>. Events that could not be sent by then are lost.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (_sender is null)
            {
                return;
            }

            // At the same time, as the host application must wait for the timeout once, not once for each.
            var stoppingOthers = Task.WhenAll(Task.Run(_spanSender!.Dispose), Task.Run(_metricSender!.Dispose));

            _sender.Dispose();
            stoppingOthers.Wait(_shutdownTimeout);

            _ = Task.WhenAll(_sender.Completion, _spanSender.Completion, _metricSender.Completion).ContinueWith(
                _ => _httpClient!.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }
#pragma warning disable CA1031 // The client must never throw into the host application.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Disposing must not fail the shutdown of the host application.
        }
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

    private static TelemetryBuffer<T> CreateBuffer<T>(LogbookOptions options, TimeProvider timeProvider)
        where T : ITelemetryItem
    {
        return new TelemetryBuffer<T>(
            options.MaxQueueSize,
            options.MaxQueueBytes,
            options.BatchSize,
            options.MaxRequestBytes,
            timeProvider);
    }
}
