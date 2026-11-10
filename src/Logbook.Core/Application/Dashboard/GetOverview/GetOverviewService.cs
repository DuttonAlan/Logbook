// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Application.Charts;
using Logbook.Core.Domain.Entities;
using Logbook.Core.Domain.Exceptions;

namespace Logbook.Core.Application.Dashboard.GetOverview;

/// <summary>
///     The Service to get the overview of a time range for the dashboard.
/// </summary>
/// <remarks>
///     Everything is counted from the stored Log Events and spans, so the numbers cannot disagree with what can be searched.
///     The most frequent exceptions come from <c>SearchExceptionGroupsService</c>, sorted by count.
/// </remarks>
/// <param name="logEventRepository">An Instance of a ILogEventRepository to count the Log Events.</param>
/// <param name="spanRepository">An Instance of a ISpanRepository to count the requests.</param>
/// <param name="timeProvider">The TimeProvider for the default time range.</param>
public sealed class GetOverviewService(
    ILogEventRepository logEventRepository,
    ISpanRepository spanRepository,
    TimeProvider timeProvider)
{
    /// <summary>
    ///     The maximum number of slowest requests in the response.
    /// </summary>
    public const int MaxSlowestRequests = 50;

    /// <summary>
    ///     Gets the overview of a time range.
    /// </summary>
    /// <param name="from">The start of the range, or null for one day before <paramref name="to"/>.</param>
    /// <param name="to">The end of the range, or null for now.</param>
    /// <param name="applicationId">Only data of this Application.</param>
    /// <param name="environment">Only data of this Environment, compared exactly.</param>
    /// <param name="slowestRequests">The number of slowest requests to return.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The overview, with every bucket of the range.</returns>
    /// <exception cref="DomainValidationException">Thrown when the time range is empty or too long.</exception>
    public async Task<GetOverviewResponse> ExecuteAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? applicationId,
        string? environment,
        int slowestRequests,
        CancellationToken cancellationToken = default)
    {
        var range = ChartRange.Create(from, to, timeProvider.GetUtcNow().UtcDateTime);
        var normalizedEnvironment = string.IsNullOrWhiteSpace(environment) ? null : LogEventJson.ReplaceNullCharacters(environment.Trim());

        // One after the other, as they share the DbContext of the request.
        var logEvents = await logEventRepository.CountByBucket(range, applicationId, normalizedEnvironment, cancellationToken);
        var requests = await spanRepository.CountRequestsByBucket(range, applicationId, normalizedEnvironment, cancellationToken);
        var slowest = await spanRepository.GetSlowestRequests(range, applicationId, normalizedEnvironment, slowestRequests, cancellationToken);

        var logEventsByBucket = logEvents.ToDictionary(bucket => bucket.Timestamp);
        var requestsByBucket = requests.ToDictionary(bucket => bucket.Timestamp);

        return new GetOverviewResponse(
            range.From,
            range.To,
            (long)range.BucketWidth.TotalSeconds,
            [.. range.Buckets.Select(start => logEventsByBucket.GetValueOrDefault(start) ?? new LogEventBucketResponse(start, 0, 0, 0))],
            [.. range.Buckets.Select(start => requestsByBucket.GetValueOrDefault(start) ?? new RequestBucketResponse(start, 0, 0))],
            requests.Sum(bucket => bucket.Count),
            requests.Sum(bucket => bucket.Failed),
            slowest);
    }
}
