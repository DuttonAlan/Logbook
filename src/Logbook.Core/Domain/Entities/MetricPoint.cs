// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Text.Json;

using Logbook.Core.Domain.Exceptions;

namespace Logbook.Core.Domain.Entities;

/// <summary>
///     Represents the measurements of one custom metric of an <see cref="Application"/> over a short interval, aggregated by
///     the sender: how many there were, their sum, and the smallest and largest. A metric point never changes.
/// </summary>
/// <remarks>
///     Sending aggregates rather than each measurement keeps the volume independent of how often the application measures,
///     and count, sum, minimum and maximum can be combined again into any longer interval.
/// </remarks>
public sealed class MetricPoint : ITelemetryRecord
{
    /// <summary>
    ///     Maximum length of the Name.
    /// </summary>
    public const int NameMaxLength = 200;

    /// <summary>
    ///     Maximum number of dimensions of one metric point.
    /// </summary>
    public const int MaxDimensions = 10;

    /// <summary>
    ///     Maximum length of the name of a dimension.
    /// </summary>
    public const int DimensionNameMaxLength = 100;

    /// <summary>
    ///     Maximum length of the value of a dimension.
    /// </summary>
    public const int DimensionValueMaxLength = 500;

    // Roughly the object itself, the headers of its strings, and its place in the queue.
    private const long MemoryOverheadBytes = 384;

    /// <summary>
    ///     Used by EF Core to materialize a metric point from the database.
    /// </summary>
    private MetricPoint()
    {
    }

    /// <summary>
    ///     Id of the metric point.
    /// </summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>
    ///     Id of the <see cref="Application"/> that sent the metric point, taken from its API Key.
    /// </summary>
    public Guid ApplicationId { get; private set; }

    /// <summary>
    ///     Point in time (UTC) when the interval of the measurements started, set by the sender.
    /// </summary>
    public DateTime Timestamp { get; private set; }

    /// <summary>
    ///     The name of the metric, e.g. <c>orders.placed</c>.
    /// </summary>
    public string Name { get; private set; } = null!;

    /// <summary>
    ///     The Environment, e.g. <c>Production</c>. A filter label, not a security boundary.
    /// </summary>
    public string Environment { get; private set; } = null!;

    /// <summary>
    ///     The number of measurements, at least one.
    /// </summary>
    public long Count { get; private set; }

    /// <summary>
    ///     The sum of the measurements.
    /// </summary>
    public double Sum { get; private set; }

    /// <summary>
    ///     The smallest measurement.
    /// </summary>
    public double Min { get; private set; }

    /// <summary>
    ///     The largest measurement.
    /// </summary>
    public double Max { get; private set; }

    /// <summary>
    ///     The dimensions as a JSON object of strings, e.g. <c>{"Region":"EU"}</c>, or null if there are none.
    /// </summary>
    public string? Dimensions { get; private set; }

    /// <summary>
    ///     Creates a new metric point.
    /// </summary>
    /// <param name="applicationId">Id of the Application that sent the metric point.</param>
    /// <param name="timestamp">Point in time (UTC) when the interval of the measurements started.</param>
    /// <param name="name">The name of the metric.</param>
    /// <param name="count">The number of measurements.</param>
    /// <param name="sum">The sum of the measurements.</param>
    /// <param name="min">The smallest measurement.</param>
    /// <param name="max">The largest measurement.</param>
    /// <param name="environment">The Environment. If it is empty, <see cref="LogEvent.DefaultEnvironment"/> is stored.</param>
    /// <param name="dimensions">The dimensions. A dimension without a value is left out.</param>
    /// <returns>The created metric point.</returns>
    /// <exception cref="DomainValidationException">Thrown when a value is not valid. The message never contains received values.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="timestamp"/> is not in UTC.</exception>
    public static MetricPoint Create(
        Guid applicationId,
        DateTime timestamp,
        string name,
        long count,
        double sum,
        double min,
        double max,
        string? environment = null,
        IReadOnlyDictionary<string, string?>? dimensions = null)
    {
        UtcGuard.EnsureUtc(timestamp);

        if (count < 1)
        {
            throw new DomainValidationException("The count must be at least 1.", nameof(count));
        }

        if (!double.IsFinite(sum) || !double.IsFinite(min) || !double.IsFinite(max))
        {
            throw new DomainValidationException("The sum, the minimum and the maximum must be finite numbers.", nameof(sum));
        }

        if (min > max)
        {
            throw new DomainValidationException("The minimum must not be larger than the maximum.", nameof(min));
        }

        return new MetricPoint
        {
            ApplicationId = applicationId,
            Timestamp = timestamp,
            Name = NormalizeOptional(name, NameMaxLength, nameof(name))
                ?? throw new DomainValidationException("The value must not be empty.", nameof(name)),
            Environment = NormalizeOptional(environment, LogEvent.EnvironmentMaxLength, nameof(environment)) ?? LogEvent.DefaultEnvironment,
            Count = count,
            Sum = sum,
            Min = min,
            Max = max,
            Dimensions = NormalizeDimensions(dimensions)
        };
    }

    /// <inheritdoc/>
    public long GetMemoryBytes()
    {
        var characters = (long)Name.Length + Environment.Length + (Dimensions?.Length ?? 0);

        return (characters * sizeof(char)) + MemoryOverheadBytes;
    }

    private static string? NormalizeDimensions(IReadOnlyDictionary<string, string?>? dimensions)
    {
        if (dimensions is null || dimensions.Count == 0)
        {
            return null;
        }

        // Sorted, so that the same dimensions are always stored as the same JSON.
        var normalized = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in dimensions)
        {
            var normalizedKey = NormalizeOptional(key, DimensionNameMaxLength, "dimensions")
                ?? throw new DomainValidationException("The name of a dimension must not be empty.", nameof(dimensions));

            if (NormalizeOptional(value, DimensionValueMaxLength, "dimensions") is not { } normalizedValue)
            {
                continue;
            }

            if (!normalized.TryAdd(normalizedKey, normalizedValue))
            {
                throw new DomainValidationException("The names of the dimensions must be unique.", nameof(dimensions));
            }
        }

        if (normalized.Count > MaxDimensions)
        {
            throw new DomainValidationException($"A metric point must not have more than {MaxDimensions} dimensions.", nameof(dimensions));
        }

        return normalized.Count == 0 ? null : JsonSerializer.Serialize(normalized);
    }

    private static string? NormalizeOptional(string? value, int maxLength, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        if (trimmed.Length > maxLength)
        {
            throw new DomainValidationException($"The value must not be longer than {maxLength} characters.", paramName);
        }

        return LogEventJson.ReplaceNullCharacters(trimmed);
    }
}
