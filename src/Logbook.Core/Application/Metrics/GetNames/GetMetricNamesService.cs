// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Application.Charts;
using Logbook.Core.Domain.Entities;
using Logbook.Core.Domain.Exceptions;

namespace Logbook.Core.Application.Metrics.GetNames;

/// <summary>
///     The Service to find out which metrics the Applications send.
/// </summary>
/// <param name="metricRepository">An Instance of a IMetricRepository to read the names.</param>
/// <param name="timeProvider">The TimeProvider for the default time range.</param>
public sealed class GetMetricNamesService(IMetricRepository metricRepository, TimeProvider timeProvider)
{
    /// <summary>
    ///     The maximum number of names in the response.
    /// </summary>
    public const int MaxNames = 1000;

    /// <summary>
    ///     Gets the names of the metrics that have points in a time range.
    /// </summary>
    /// <param name="from">Only points at or after this point in time, or null for 7 days before <paramref name="to"/>.</param>
    /// <param name="to">Only points before this point in time, or null for now.</param>
    /// <param name="applicationId">Only points of this Application.</param>
    /// <param name="environment">Only points of this Environment, compared exactly.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The names, ordered by name.</returns>
    /// <exception cref="DomainValidationException">Thrown when the time range is empty or too long.</exception>
    public async Task<GetMetricNamesResponse> ExecuteAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? applicationId,
        string? environment,
        CancellationToken cancellationToken = default)
    {
        var end = to?.UtcDateTime ?? timeProvider.GetUtcNow().UtcDateTime;
        var start = from?.UtcDateTime ?? end.AddDays(-7);

        if (start >= end)
        {
            throw new DomainValidationException("'from' must be earlier than 'to'.", nameof(from));
        }

        if (end - start > ChartRange.MaxLength)
        {
            throw new DomainValidationException($"The time range must not be longer than {ChartRange.MaxLength.TotalDays} days.", nameof(from));
        }

        var names = await metricRepository.GetNames(start, end, applicationId, NormalizeText(environment), MaxNames, cancellationToken);

        return new GetMetricNamesResponse(names);
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : LogEventJson.ReplaceNullCharacters(value.Trim());
    }
}
