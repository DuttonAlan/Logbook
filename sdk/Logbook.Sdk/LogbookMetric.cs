// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Sdk;

/// <summary>
///     The measurements of one metric over a short interval, aggregated, to send to Logbook: how many there were, their sum,
///     and the smallest and largest. It never changes after it is created, so it can wait in the buffer safely.
/// </summary>
/// <remarks>
///     Usually created by <see cref="LogbookMetrics"/>, which aggregates single measurements. The server rejects a whole batch
///     for a single value it does not accept, so values are made acceptable here: text over a limit of the server is cut, an
///     empty name becomes <see cref="UnnamedName"/>, a count below 1 becomes 1, a value that is not a finite number becomes
///     0, minimum and maximum are swapped if they are the wrong way round, and only the first
///     <see cref="MaxDimensions"/> dimensions by name are kept.
/// </remarks>
public sealed class LogbookMetric : ITelemetryItem
{
    /// <summary>
    ///     The name of a metric that was created without one.
    /// </summary>
    public const string UnnamedName = "(unnamed)";

    /// <summary>
    ///     The most dimensions the server accepts for one metric point.
    /// </summary>
    public const int MaxDimensions = 10;

    // The limits of the server, which are fixed.
    private const int NameMaxLength = 200;
    private const int EnvironmentMaxLength = 100;
    private const int DimensionNameMaxLength = 100;
    private const int DimensionValueMaxLength = 500;

    // Roughly the object itself, the headers of its strings, and its place in the buffer.
    private const long MemoryOverheadBytes = 384;

    private static readonly IReadOnlyDictionary<string, string> NoDimensions = new Dictionary<string, string>();

    /// <summary>
    ///     Creates a metric point.
    /// </summary>
    /// <param name="timestamp">Point in time when the interval of the measurements started.</param>
    /// <param name="name">The name of the metric, e.g. <c>orders.placed</c>.</param>
    /// <param name="count">The number of measurements.</param>
    /// <param name="sum">The sum of the measurements.</param>
    /// <param name="min">The smallest measurement.</param>
    /// <param name="max">The largest measurement.</param>
    /// <param name="environment">The Environment, e.g. <c>Production</c>.</param>
    /// <param name="dimensions">The dimensions, e.g. <c>Region</c> = <c>EU</c>. A dimension without a name or a value is left out.</param>
    public LogbookMetric(
        DateTimeOffset timestamp,
        string? name,
        long count,
        double sum,
        double min,
        double max,
        string? environment = null,
        IEnumerable<KeyValuePair<string, string?>>? dimensions = null)
    {
        Timestamp = timestamp;
        Name = string.IsNullOrWhiteSpace(name) ? UnnamedName : LogEventGuard.Cut(name!.Trim(), NameMaxLength);
        Count = count < 1 ? 1 : count;
        Sum = Finite(sum);
        Min = Math.Min(Finite(min), Finite(max));
        Max = Math.Max(Finite(min), Finite(max));
        Environment = string.IsNullOrWhiteSpace(environment) ? null : LogEventGuard.Cut(environment!, EnvironmentMaxLength);
        Dimensions = CutDimensions(dimensions);
    }

    /// <summary>
    ///     Point in time when the interval of the measurements started.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    ///     The name of the metric.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     The number of measurements, at least 1.
    /// </summary>
    public long Count { get; }

    /// <summary>
    ///     The sum of the measurements.
    /// </summary>
    public double Sum { get; }

    /// <summary>
    ///     The smallest measurement.
    /// </summary>
    public double Min { get; }

    /// <summary>
    ///     The largest measurement.
    /// </summary>
    public double Max { get; }

    /// <summary>
    ///     The Environment.
    /// </summary>
    public string? Environment { get; }

    /// <summary>
    ///     The dimensions, ordered by name.
    /// </summary>
    public IReadOnlyDictionary<string, string> Dimensions { get; }

    /// <inheritdoc/>
    long ITelemetryItem.GetMemoryBytes()
    {
        var characters = (long)Name.Length + (Environment?.Length ?? 0);

        foreach (var dimension in Dimensions)
        {
            characters += dimension.Key.Length + dimension.Value.Length;
        }

        return (characters * sizeof(char)) + MemoryOverheadBytes;
    }

    private static double Finite(double value) => double.IsFinite(value) ? value : 0;

    private static IReadOnlyDictionary<string, string> CutDimensions(IEnumerable<KeyValuePair<string, string?>>? dimensions)
    {
        if (dimensions is null)
        {
            return NoDimensions;
        }

        // The server trims the names and rejects two that are the same, so they are compared as it would.
        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var dimension in dimensions)
        {
            if (string.IsNullOrWhiteSpace(dimension.Key) || string.IsNullOrWhiteSpace(dimension.Value))
            {
                continue;
            }

            var key = LogEventGuard.Cut(dimension.Key.Trim(), DimensionNameMaxLength);

            if (!sorted.ContainsKey(key))
            {
                sorted[key] = LogEventGuard.Cut(dimension.Value!.Trim(), DimensionValueMaxLength);
            }
        }

        if (sorted.Count == 0)
        {
            return NoDimensions;
        }

        return sorted.Take(MaxDimensions).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }
}
