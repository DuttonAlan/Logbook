// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;

namespace Logbook.Sdk;

/// <summary>
///     Records custom metrics and sends them to Logbook, aggregated: for each metric and set of dimensions, the count, sum,
///     minimum and maximum of the measurements of one <see cref="LogbookOptions.MetricInterval"/> become one
///     <see cref="LogbookMetric"/>.
/// </summary>
/// <remarks>
///     Measurements come from <see cref="Record"/>, or from the instruments of the <c>System.Diagnostics.Metrics</c> meters
///     named in <see cref="LogbookOptions.Meters"/>: counters, up-down counters, histograms, gauges, and the observable
///     ones, which are read once per interval. Recording never throws and never blocks for longer than adding two numbers.
///     The volume sent does not depend on how often the application measures, but on how many different metrics and
///     dimensions it uses: at most <see cref="MaxSeriesPerInterval"/> per interval are kept, and measurements beyond that are
///     dropped and reported through the <c>EventSource</c> named <c>Logbook-Sdk</c>. Dispose it before the client, which
///     dependency injection does, so that the last interval is sent too. Disposing waits for at most
///     <see cref="LogbookOptions.ShutdownTimeout"/>.
/// </remarks>
public sealed class LogbookMetrics : IDisposable
{
    /// <summary>
    ///     The most different combinations of metric and dimensions kept in one interval.
    /// </summary>
    public const int MaxSeriesPerInterval = 1000;

    private readonly LogbookClient? _client;
    private readonly string? _environment;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _shutdownTimeout;
    private readonly ITimer? _timer;
    private readonly MeterListener? _listener;
    private readonly string[] _meters = [];
    private readonly Lock _flushLock = new();

    private Interval _current;
    private int _disposed;

    /// <summary>
    ///     Creates the metrics and starts sending them at every <see cref="LogbookOptions.MetricInterval"/>.
    /// </summary>
    /// <param name="client">The client to send with. If it is disabled, nothing is recorded.</param>
    /// <param name="options">The options, of which the interval, the meters and the shutdown timeout are used.</param>
    /// <param name="environment">The Environment of the metric points, e.g. <c>Production</c>.</param>
    public LogbookMetrics(LogbookClient client, LogbookOptions options, string? environment = null)
        : this(client, options, environment, TimeProvider.System)
    {
    }

    /// <summary>
    ///     Creates the metrics with the clock tests replace.
    /// </summary>
    /// <param name="client">The client to send with.</param>
    /// <param name="options">The options.</param>
    /// <param name="environment">The Environment of the metric points.</param>
    /// <param name="timeProvider">The clock for the interval and the timestamps.</param>
    internal LogbookMetrics(LogbookClient client, LogbookOptions options, string? environment, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _environment = environment;
        _current = new Interval(_timeProvider.GetUtcNow());

        if (client is null || !client.IsEnabled || options is null || options.FindInvalidSetting() is not null)
        {
            return;
        }

        try
        {
            _shutdownTimeout = options.ShutdownTimeout;

            // Otherwise the timer would keep the Activity, scopes and other ambient state of whoever created this.
            using (ExecutionContext.SuppressFlow())
            {
                _timer = _timeProvider.CreateTimer(_ => Flush(), null, options.MetricInterval, options.MetricInterval);
            }

            _meters = [.. options.Meters.Where(meter => !string.IsNullOrWhiteSpace(meter)).Select(meter => meter.Trim())];

            if (_meters.Length > 0)
            {
                _listener = CreateListener();
                _listener.Start();
            }

            _client = client;
        }
#pragma warning disable CA1031 // The SDK must never throw into the host application.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _timer?.Dispose();
            _listener?.Dispose();
            LogbookEventSource.Log.ClientDisabled(exception.GetType().Name);
        }
    }

    /// <summary>
    ///     Records one measurement of a metric, e.g. <c>Record("orders.placed", 1, ("Region", "EU"))</c>.
    /// </summary>
    /// <param name="name">The name of the metric, with few distinct values. Put what varies into the dimensions.</param>
    /// <param name="value">The measurement. A value that is not a finite number is ignored.</param>
    /// <param name="dimensions">The dimensions, e.g. <c>("Region", "EU")</c>. A dimension without a value is left out.</param>
    public void Record(string name, double value, params ReadOnlySpan<(string Name, string? Value)> dimensions)
    {
        if (_client is null || !double.IsFinite(value) || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            var pairs = new KeyValuePair<string, string?>[dimensions.Length];

            for (var index = 0; index < dimensions.Length; index++)
            {
                pairs[index] = new KeyValuePair<string, string?>(dimensions[index].Name, dimensions[index].Value);
            }

            RecordCore(name, value, pairs);
        }
#pragma warning disable CA1031 // The SDK must never throw into the host application.
        catch (Exception)
#pragma warning restore CA1031
        {
            // E.g. out of memory. Losing one measurement is better than failing the caller.
        }
    }

    /// <summary>
    ///     Stops recording, and hands the measurements of the current interval to the client. Waits for at most
    ///     <see cref="LogbookOptions.ShutdownTimeout"/> for a flush that is under way, e.g. one held up by an observable
    ///     instrument of the application, and gives up the last interval after that.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _timer?.Dispose();
            _listener?.Dispose();

            if (_client is null || !_flushLock.TryEnter(_shutdownTimeout))
            {
                return;
            }

            try
            {
                // Without the observable instruments: the listener is gone, and their callbacks are code of the application
                // that could take any time.
                FlushCore(readObservables: false);
            }
            finally
            {
                _flushLock.Exit();
            }
        }
#pragma warning disable CA1031 // The SDK must never throw into the host application.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Disposing must not fail the shutdown of the host application.
        }
    }

    /// <summary>
    ///     Ends the current interval and hands its metric points to the client. Called by the timer.
    /// </summary>
    internal void Flush()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            // The timer can fire again while a slow flush still runs.
            lock (_flushLock)
            {
                FlushCore(readObservables: true);
            }
        }
#pragma warning disable CA1031 // The timer must not end the process.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>
    ///     Builds a key that two different series can never share: each part is written with its length in front, so no
    ///     character inside a name or value can be mistaken for the boundary between two of them.
    /// </summary>
    private static string BuildKey(string name, List<KeyValuePair<string, string>> dimensions)
    {
        var key = new StringBuilder();

        Append(key, name);

        foreach (var dimension in dimensions)
        {
            Append(key, dimension.Key);
            Append(key, dimension.Value);
        }

        return key.ToString();

        static void Append(StringBuilder key, string part) => key.Append(part.Length).Append(':').Append(part);
    }

    private void FlushCore(bool readObservables)
    {
        if (readObservables)
        {
            try
            {
                // Observable instruments report their values now, into the interval that is about to end.
                _listener?.RecordObservableInstruments();
            }
#pragma warning disable CA1031 // A failing callback of the application must not stop the others from being sent.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }

        var ended = Interlocked.Exchange(ref _current, new Interval(_timeProvider.GetUtcNow()));

        // A measurement that started before the exchange finishes in the ended interval. Any later one sees the new
        // interval, so after this wait nothing changes the ended one anymore.
        var wait = default(SpinWait);

        while (Volatile.Read(ref ended.Writers) != 0)
        {
            wait.SpinOnce();
        }

        foreach (var series in ended.Series.Values)
        {
            _client!.Enqueue(new LogbookMetric(ended.Start, series.Name, series.Count, series.Sum, series.Min, series.Max, _environment, series.Dimensions));
        }

        if (ended.Dropped > 0)
        {
            LogbookEventSource.Log.EventsDropped($"more than {MaxSeriesPerInterval} combinations of metric and dimensions in one interval", ended.Dropped);
        }
    }

    // Names and dimensions are made what the server accepts before they become a series, so that what is kept in memory
    // is bounded, and two inputs the server would treat as the same are one series.
    private void RecordCore(string? name, double value, IEnumerable<KeyValuePair<string, string?>> dimensions)
    {
        if (LogbookMetric.NormalizeName(name) is not { } normalizedName)
        {
            return;
        }

        var normalizedDimensions = LogbookMetric.NormalizeDimensions(dimensions);
        var key = BuildKey(normalizedName, normalizedDimensions);

        while (true)
        {
            var interval = Volatile.Read(ref _current);

            Interlocked.Increment(ref interval.Writers);

            try
            {
                // The flush has started in the meantime: this measurement belongs to the next interval.
                if (!ReferenceEquals(Volatile.Read(ref _current), interval))
                {
                    continue;
                }

                if (!interval.Series.TryGetValue(key, out var series))
                {
                    series = interval.AddSeries(key, normalizedName, normalizedDimensions);

                    if (series is null)
                    {
                        Interlocked.Increment(ref interval.Dropped);
                        return;
                    }
                }

                series.Add(value);

                return;
            }
            finally
            {
                Interlocked.Decrement(ref interval.Writers);
            }
        }
    }

    private MeterListener CreateListener()
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (IsCollected(instrument.Meter.Name))
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };

        listener.SetMeasurementEventCallback<byte>((instrument, value, tags, _) => RecordMeasurement(instrument, value, tags));
        listener.SetMeasurementEventCallback<short>((instrument, value, tags, _) => RecordMeasurement(instrument, value, tags));
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => RecordMeasurement(instrument, value, tags));
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => RecordMeasurement(instrument, value, tags));
        listener.SetMeasurementEventCallback<float>((instrument, value, tags, _) => RecordMeasurement(instrument, value, tags));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => RecordMeasurement(instrument, value, tags));
        listener.SetMeasurementEventCallback<decimal>((instrument, value, tags, _) => RecordMeasurement(instrument, (double)value, tags));

        return listener;
    }

    private bool IsCollected(string meterName)
    {
        foreach (var meter in _meters)
        {
            var matches = meter.EndsWith('*')
                ? meterName.StartsWith(meter[..^1], StringComparison.OrdinalIgnoreCase)
                : meterName.Equals(meter, StringComparison.OrdinalIgnoreCase);

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private void RecordMeasurement(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (!double.IsFinite(value) || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            var pairs = new KeyValuePair<string, string?>[tags.Length];

            for (var index = 0; index < tags.Length; index++)
            {
                pairs[index] = new KeyValuePair<string, string?>(tags[index].Key, Convert.ToString(tags[index].Value, CultureInfo.InvariantCulture));
            }

            RecordCore(instrument.Name, value, pairs);
        }
#pragma warning disable CA1031 // The SDK must never throw into the host application.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Losing one measurement is better than failing the code that measured.
        }
    }

    private sealed class Interval(DateTimeOffset start)
    {
        public int Writers;

        public int Dropped;

        private int _seriesCount;

        public DateTimeOffset Start { get; } = start;

        public ConcurrentDictionary<string, Series> Series { get; } = new(StringComparer.Ordinal);

        /// <summary>
        ///     Adds a series, if the limit allows. The slot is reserved before the series is added, so that threads adding
        ///     different series at the same time cannot together go over the limit.
        /// </summary>
        /// <returns>The series, also if another thread added it first, or null if the limit is reached.</returns>
        public Series? AddSeries(string key, string name, List<KeyValuePair<string, string>> dimensions)
        {
            if (Interlocked.Increment(ref _seriesCount) > MaxSeriesPerInterval)
            {
                Interlocked.Decrement(ref _seriesCount);

                return Series.TryGetValue(key, out var existing) ? existing : null;
            }

            var created = new Series(name, [.. dimensions.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))]);

            if (Series.TryAdd(key, created))
            {
                return created;
            }

            // Another thread added the same series first, and holds its own slot for it.
            Interlocked.Decrement(ref _seriesCount);

            return Series[key];
        }
    }

    private sealed class Series(string name, KeyValuePair<string, string?>[] dimensions)
    {
        private readonly Lock _lock = new();

        private long _count;
        private double _sum;
        private double _min = double.PositiveInfinity;
        private double _max = double.NegativeInfinity;

        public string Name { get; } = name;

        public KeyValuePair<string, string?>[] Dimensions { get; } = dimensions;

        public long Count => _count;

        public double Sum => _sum;

        public double Min => _min;

        public double Max => _max;

        public void Add(double value)
        {
            lock (_lock)
            {
                _count++;
                _sum += value;
                _min = Math.Min(_min, value);
                _max = Math.Max(_max, value);
            }
        }
    }
}
