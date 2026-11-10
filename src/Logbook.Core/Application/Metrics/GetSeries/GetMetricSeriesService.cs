// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Text.Json;

using Logbook.Core.Abstractions;
using Logbook.Core.Application.Charts;
using Logbook.Core.Domain.Entities;
using Logbook.Core.Domain.Exceptions;

namespace Logbook.Core.Application.Metrics.GetSeries;

/// <summary>
///     The Service to read a metric over a time range, for a chart.
/// </summary>
/// <param name="metricRepository">An Instance of a IMetricRepository to read the metric points.</param>
/// <param name="timeProvider">The TimeProvider for the default time range.</param>
public sealed class GetMetricSeriesService(IMetricRepository metricRepository, TimeProvider timeProvider)
{
    /// <summary>
    ///     The maximum number of dimension filters.
    /// </summary>
    public const int MaxDimensionFilters = MetricPoint.MaxDimensions;

    /// <summary>
    ///     Combines the points of a metric into the buckets of a chart.
    /// </summary>
    /// <param name="name">The name of the metric, compared exactly.</param>
    /// <param name="from">The start of the range, or null for one day before <paramref name="to"/>.</param>
    /// <param name="to">The end of the range, or null for now.</param>
    /// <param name="applicationId">Only points of this Application.</param>
    /// <param name="environment">Only points of this Environment, compared exactly.</param>
    /// <param name="dimensions">Only points with all of these dimensions, each in the form <c>Name=Value</c>.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>Every bucket of the range, also those without measurements.</returns>
    /// <exception cref="DomainValidationException">Thrown when the name is empty, the time range is not valid, or a dimension filter does not have the form <c>Name=Value</c>.</exception>
    public async Task<GetMetricSeriesResponse> ExecuteAsync(
        string? name,
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? applicationId,
        string? environment,
        IReadOnlyList<string?>? dimensions,
        CancellationToken cancellationToken = default)
    {
        DomainValidationException.ThrowIfNullOrWhiteSpace(name);

        var range = ChartRange.Create(from, to, timeProvider.GetUtcNow().UtcDateTime);
        var normalizedName = LogEventJson.ReplaceNullCharacters(name.Trim());

        var found = await metricRepository.GetSeries(
            range,
            normalizedName,
            applicationId,
            NormalizeText(environment),
            ToDimensionsJson(dimensions),
            cancellationToken);

        var byBucket = found.ToDictionary(bucket => bucket.Timestamp);

        return new GetMetricSeriesResponse(
            normalizedName,
            range.From,
            range.To,
            (long)range.BucketWidth.TotalSeconds,
            [.. range.Buckets.Select(start => byBucket.GetValueOrDefault(start) ?? new MetricBucketResponse(start, 0, 0, null, null))]);
    }

    private static string? ToDimensionsJson(IReadOnlyList<string?>? dimensions)
    {
        if (dimensions is null || dimensions.Count == 0)
        {
            return null;
        }

        if (dimensions.Count > MaxDimensionFilters)
        {
            throw new DomainValidationException($"At most {MaxDimensionFilters} dimension filters are allowed.", nameof(dimensions));
        }

        var filter = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var dimension in dimensions)
        {
            // Null is what the model binding makes of an empty parameter. Names and values are stored trimmed.
            var separator = dimension?.IndexOf('=', StringComparison.Ordinal) ?? -1;
            var key = separator > 0 ? dimension![..separator].Trim() : string.Empty;

            if (key.Length == 0)
            {
                throw new DomainValidationException("A dimension filter must have the form 'Name=Value'.", "dimension");
            }

            filter[LogEventJson.ReplaceNullCharacters(key)] = LogEventJson.ReplaceNullCharacters(dimension![(separator + 1)..].Trim());
        }

        return JsonSerializer.Serialize(filter);
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : LogEventJson.ReplaceNullCharacters(value.Trim());
    }
}
