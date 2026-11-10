// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Diagnostics.Metrics;
using System.IO.Compression;
using System.Text.Json;

using Microsoft.Extensions.Time.Testing;

namespace Logbook.Sdk.Tests;

/// <summary>
///     Checks that measurements are aggregated per metric and dimensions, sent once per interval, made acceptable to the
///     server, and never hurt the application.
/// </summary>
public sealed class LogbookMetricsTests : IDisposable
{
    private static readonly LogbookOptions BaseOptions = new()
    {
        Endpoint = new Uri("https://logbook.example.com/"),
        ApiKey = "the-api-key",
        BatchSize = 5000,
        FlushInterval = TimeSpan.FromHours(1),
        ShutdownTimeout = TimeSpan.Zero,
        MetricInterval = TimeSpan.FromMinutes(1)
    };

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero));
    private readonly RecordingHandler _handler;
    private readonly LogbookClient _client;

    public LogbookMetricsTests()
    {
        _handler = new RecordingHandler(_time);
        _client = new LogbookClient(BaseOptions, _handler, _time);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _client.Dispose();
        _handler.Dispose();
    }

    [Fact]
    public void Record_AggregatesPerNameAndDimensions_InAnyOrder()
    {
        using var metrics = new LogbookMetrics(_client, BaseOptions, "Production", _time);
        var start = _time.GetUtcNow();

        metrics.Record("orders.placed", 2, ("Region", "EU"), ("Shop", "A"));
        metrics.Record("orders.placed", 5, ("Shop", "A"), ("Region", "EU"));
        metrics.Record("orders.placed", -1, ("Region", "EU"), ("Shop", "A"));
        metrics.Record("orders.placed", 10, ("Region", "US"));
        metrics.Record("orders.failed", 1);

        metrics.Flush();

        var points = Points();
        Assert.Equal(3, points.Count);

        var europe = Assert.Single(points, point => point.Dimensions.GetValueOrDefault("Region") == "EU");
        Assert.Equal("orders.placed", europe.Name);
        Assert.Equal(3, europe.Count);
        Assert.Equal(6, europe.Sum);
        Assert.Equal(-1, europe.Min);
        Assert.Equal(5, europe.Max);
        Assert.Equal("A", europe.Dimensions["Shop"]);
        Assert.Equal("Production", europe.Environment);
        Assert.Equal(start, europe.Timestamp);

        var failed = Assert.Single(points, point => point.Name == "orders.failed");
        Assert.Empty(failed.Dimensions);
        Assert.Equal(1, failed.Count);
    }

    [Fact]
    public void Record_IgnoresWhatCannotBeAMeasurement()
    {
        using var metrics = new LogbookMetrics(_client, BaseOptions, null, _time);

        metrics.Record("a", double.NaN);
        metrics.Record("a", double.PositiveInfinity);
        metrics.Record(" ", 1);
        metrics.Record(null!, 1);
        metrics.Record("b", 1, ("Empty", " "), (" ", "value"), ("Null", null));

        metrics.Flush();

        var point = Assert.Single(Points());
        Assert.Equal("b", point.Name);
        Assert.Empty(point.Dimensions);
    }

    [Fact]
    public void EachInterval_IsSentOnItsOwn_WithItsStartAsTheTimestamp()
    {
        using var metrics = new LogbookMetrics(_client, BaseOptions, null, _time);
        var first = _time.GetUtcNow();

        metrics.Record("requests", 1);
        _time.Advance(BaseOptions.MetricInterval);

        var second = _time.GetUtcNow();

        metrics.Record("requests", 1);
        metrics.Record("requests", 1);
        _time.Advance(BaseOptions.MetricInterval);

        var points = Points();
        Assert.Equal([(first, 1L), (second, 2L)], points.Select(point => (point.Timestamp, point.Count)));
    }

    [Fact]
    public void AnIntervalWithoutMeasurements_SendsNothing()
    {
        using var metrics = new LogbookMetrics(_client, BaseOptions, null, _time);

        _time.Advance(BaseOptions.MetricInterval * 3);

        Assert.Equal(0, _client.MetricBuffer!.Count);
    }

    [Fact]
    public void TooManySeries_KeepsTheFirst_AndDropsTheRest()
    {
        using var metrics = new LogbookMetrics(_client, BaseOptions, null, _time);

        for (var index = 0; index < LogbookMetrics.MaxSeriesPerInterval + 50; index++)
        {
            metrics.Record("users", 1, ("UserId", index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        // A series that already exists still counts.
        metrics.Record("users", 1, ("UserId", "0"));

        metrics.Flush();

        var points = Points();
        Assert.Equal(LogbookMetrics.MaxSeriesPerInterval, points.Count);
        Assert.Equal(2, Assert.Single(points, point => point.Dimensions["UserId"] == "0").Count);
    }

    [Fact]
    public async Task ConcurrentNewSeries_NeverGoOverTheLimit()
    {
        using var metrics = new LogbookMetrics(_client, BaseOptions, null, _time);

        const int Threads = 8;
        var perThread = LogbookMetrics.MaxSeriesPerInterval / 2;
        using var start = new Barrier(Threads);

        await Task.WhenAll(Enumerable.Range(0, Threads).Select(thread => Task.Run(() =>
        {
            start.SignalAndWait(TestContext.Current.CancellationToken);

            for (var index = 0; index < perThread; index++)
            {
                metrics.Record("users", 1, ("Key", $"{thread}-{index}"));
            }
        })));

        metrics.Flush();

        Assert.Equal(LogbookMetrics.MaxSeriesPerInterval, Points().Count);
    }

    [Fact]
    public void DimensionsThatOnlyLookAlike_AreDifferentSeries_AndOnesTheServerTreatsAsTheSame_AreOne()
    {
        using var metrics = new LogbookMetrics(_client, BaseOptions, null, _time);

        // Characters that could separate the parts of a key are text like any other.
        metrics.Record("m", 1, ("a", "b\u001fc\u001ed"));
        metrics.Record("m", 1, ("a", "b"), ("c", "d"));

        // The server trims, and replaces U+0000, before it compares names.
        metrics.Record("n", 1, (" Region ", "EU"), ("Region", "US"));
        metrics.Record("n", 1, ("Region", " EU "));
        metrics.Record("o", 1, ("a\0b", "1"), ("a�b", "2"));

        metrics.Flush();

        var points = Points();
        Assert.Equal(2, points.Count(point => point.Name == "m"));

        var region = Assert.Single(points, point => point.Name == "n");
        Assert.Equal(2, region.Count);
        Assert.Equal("EU", Assert.Single(region.Dimensions).Value);

        Assert.Equal("a�b", Assert.Single(Assert.Single(points, point => point.Name == "o").Dimensions).Key);
    }

    [Fact]
    public void LongNamesAndValues_AreCut_BeforeTheyAreKept()
    {
        using var metrics = new LogbookMetrics(_client, BaseOptions, null, _time);

        metrics.Record(new string('n', 300), 1, ("Shop", new string('v', 600)));
        metrics.Record(new string('n', 250), 1, ("Shop", new string('v', 550)));

        metrics.Flush();

        var point = Assert.Single(Points());
        Assert.Equal(2, point.Count);
        Assert.Equal(200, point.Name.Length);
        Assert.Equal(500, point.Dimensions["Shop"].Length);
    }

    [Fact]
    public async Task Dispose_WhileAnObservableInstrumentHangs_WaitsOnlyForTheShutdownTimeout()
    {
        var meterName = $"Logbook.Tests.{Guid.NewGuid():N}";
        var options = new LogbookOptions
        {
            Endpoint = BaseOptions.Endpoint,
            ApiKey = BaseOptions.ApiKey,
            ShutdownTimeout = TimeSpan.FromMilliseconds(200)
        };
        options.Meters.Add(meterName);

        using var meter = new Meter(meterName);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        meter.CreateObservableGauge("stuck", () =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(30));

            return 1;
        });

        var metrics = new LogbookMetrics(_client, options, null, _time);

        try
        {
            var flushing = Task.Run(metrics.Flush, TestContext.Current.CancellationToken);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

            var watch = System.Diagnostics.Stopwatch.StartNew();
            metrics.Dispose();

            Assert.InRange(watch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(5));

            release.Set();
            await flushing;
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task ConcurrentRecording_LosesNothing_AcrossFlushes()
    {
        using var metrics = new LogbookMetrics(_client, BaseOptions, null, _time);

        const int Threads = 8;
        const int PerThread = 20_000;

        var recording = Enumerable.Range(0, Threads)
            .Select(_ => Task.Run(() =>
            {
                for (var index = 0; index < PerThread; index++)
                {
                    metrics.Record("work", 1);
                }
            }))
            .ToArray();

        while (!recording.All(task => task.IsCompleted))
        {
            metrics.Flush();
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        await Task.WhenAll(recording);
        metrics.Flush();

        Assert.Equal(Threads * PerThread, Points().Sum(point => point.Count));
    }

    [Fact]
    public void Meters_AreCollected_WhenTheirNameIsConfigured()
    {
        var prefix = $"Logbook.Tests.{Guid.NewGuid():N}";
        var options = new LogbookOptions
        {
            Endpoint = BaseOptions.Endpoint,
            ApiKey = BaseOptions.ApiKey,
            BatchSize = BaseOptions.BatchSize,
            FlushInterval = BaseOptions.FlushInterval,
            ShutdownTimeout = BaseOptions.ShutdownTimeout
        };
        options.Meters.Add($"{prefix}.*");

        using var collected = new Meter($"{prefix}.Orders");
        using var ignored = new Meter($"Other.{Guid.NewGuid():N}");

        var counter = collected.CreateCounter<int>("orders.placed");
        var histogram = collected.CreateHistogram<double>("orders.duration");
        collected.CreateObservableGauge("orders.queue", () => 7);
        var notCollected = ignored.CreateCounter<long>("ignored");

        using var metrics = new LogbookMetrics(_client, options, null, _time);

        counter.Add(1, new KeyValuePair<string, object?>("Region", "EU"));
        counter.Add(2, new KeyValuePair<string, object?>("Region", "EU"));
        histogram.Record(12.5);
        histogram.Record(30, new KeyValuePair<string, object?>("Status", 200));
        notCollected.Add(1);

        metrics.Flush();

        var points = Points();

        var placed = Assert.Single(points, point => point.Name == "orders.placed");
        Assert.Equal((2L, 3d), (placed.Count, placed.Sum));
        Assert.Equal("EU", placed.Dimensions["Region"]);

        Assert.Equal("200", Assert.Single(points, point => point.Name == "orders.duration" && point.Dimensions.Count == 1).Dimensions["Status"]);
        Assert.Equal(7, Assert.Single(points, point => point.Name == "orders.queue").Max);
        Assert.DoesNotContain(points, point => point.Name == "ignored");
    }

    [Fact]
    public void Dispose_HandsTheLastIntervalToTheClient_AndStopsRecording()
    {
        var metrics = new LogbookMetrics(_client, BaseOptions, null, _time);

        metrics.Record("last", 1);
        metrics.Dispose();
        metrics.Record("after", 1);
        metrics.Dispose();

        Assert.Equal(["last"], Points().Select(point => point.Name));
    }

    [Fact]
    public void WithADisabledClient_NothingIsRecorded_AndNothingThrows()
    {
        using var client = new LogbookClient(new LogbookOptions());
        using var metrics = new LogbookMetrics(client, BaseOptions, null, _time);

        metrics.Record("a", 1);
        metrics.Flush();

        Assert.Null(client.MetricBuffer);
    }

    [Fact]
    public void Metric_ValuesTheServerRejects_AreMadeAcceptable()
    {
        KeyValuePair<string, string?>[] dimensions =
        [
            new(new string('k', 150), new string('v', 600)),
            new("Empty", null),
            new(" ", "no name")
        ];

        var metric = new LogbookMetric(_time.GetUtcNow(), " ", 0, double.NaN, 9, 3, new string('e', 150), dimensions);

        Assert.Equal(LogbookMetric.UnnamedName, metric.Name);
        Assert.Equal(1, metric.Count);
        Assert.Equal(0, metric.Sum);
        Assert.Equal((3d, 9d), (metric.Min, metric.Max));
        Assert.Equal(100, metric.Environment!.Length);
        var dimension = Assert.Single(metric.Dimensions);
        Assert.Equal(new string('k', 100), dimension.Key);
        Assert.Equal(500, dimension.Value.Length);
        Assert.Equal(200, new LogbookMetric(_time.GetUtcNow(), new string('n', 300), 1, 1, 1, 1).Name.Length);

        // The first ones by name are kept, as the server accepts no more.
        var many = Enumerable.Range(0, 15).Select(index => new KeyValuePair<string, string?>($"d{14 - index:00}", "v"));
        var kept = new LogbookMetric(_time.GetUtcNow(), "a", 1, 1, 1, 1, dimensions: many).Dimensions.Keys;
        Assert.Equal(Enumerable.Range(0, LogbookMetric.MaxDimensions).Select(index => $"d{index:00}"), kept);
    }

    [Fact]
    public void Write_AllFields_InTheFormatOfTheApi()
    {
        var timestamp = _time.GetUtcNow();
        var metric = new LogbookMetric(timestamp, "orders.placed", 3, 6.5, 1, 4, "Production", [new KeyValuePair<string, string?>("Region", "EU")]);

        var batch = MetricBatchWriter.Write([new BufferedItem<LogbookMetric>(metric, 1, 100, 0)], new LogEventLimits(BaseOptions), timestamp);

        using var document = JsonDocument.Parse(Decompress(batch.Body));
        var element = Assert.Single(document.RootElement.EnumerateArray());

        Assert.Equal(timestamp, element.GetProperty("timestamp").GetDateTimeOffset());
        Assert.Equal("orders.placed", element.GetProperty("name").GetString());
        Assert.Equal(3, element.GetProperty("count").GetInt64());
        Assert.Equal(6.5, element.GetProperty("sum").GetDouble());
        Assert.Equal(1, element.GetProperty("min").GetDouble());
        Assert.Equal(4, element.GetProperty("max").GetDouble());
        Assert.Equal("Production", element.GetProperty("environment").GetString());
        Assert.Equal("EU", element.GetProperty("dimensions").GetProperty("Region").GetString());

        var withoutDimensions = MetricBatchWriter.Write(
            [new BufferedItem<LogbookMetric>(new LogbookMetric(timestamp, "a", 1, 1, 1, 1), 1, 100, 0)],
            new LogEventLimits(BaseOptions),
            timestamp);

        using var other = JsonDocument.Parse(Decompress(withoutDimensions.Body));
        Assert.False(other.RootElement[0].TryGetProperty("dimensions", out _));
    }

    [Fact]
    public void Dispose_SendsTheMetricPointsThatAreLeft()
    {
        using var handler = new RecordingHandler(_time);

        var options = new LogbookOptions
        {
            Endpoint = BaseOptions.Endpoint,
            ApiKey = BaseOptions.ApiKey,
            FlushInterval = TimeSpan.FromHours(1),
            ShutdownTimeout = TimeSpan.FromSeconds(5)
        };

        var client = new LogbookClient(options, handler, _time);

        client.Enqueue(new LogbookMetric(_time.GetUtcNow(), "left", 1, 1, 1, 1));
        client.Dispose();

        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/api/v1/metrics", request.Request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(500)]
    public void AMetricIntervalUnderASecond_DisablesTheClient(int milliseconds)
    {
        var options = new LogbookOptions
        {
            Endpoint = BaseOptions.Endpoint,
            ApiKey = BaseOptions.ApiKey,
            MetricInterval = TimeSpan.FromMilliseconds(milliseconds)
        };

        Assert.Equal(nameof(LogbookOptions.MetricInterval), options.FindInvalidSetting());
    }

    private static string Decompress(byte[] body)
    {
        using var input = new MemoryStream(body);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);

        return reader.ReadToEnd();
    }

    private List<LogbookMetric> Points() => [.. _client.MetricBuffer!.Peek(int.MaxValue).Select(item => item.Item)];
}
