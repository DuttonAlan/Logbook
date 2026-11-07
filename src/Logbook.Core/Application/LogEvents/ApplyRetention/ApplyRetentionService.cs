// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logbook.Core.Application.LogEvents.ApplyRetention;

/// <summary>
///     The Service to remove the Log Events that are past the retention of their Application.
/// </summary>
/// <remarks>
///     A partition holds one day of all Applications, so it can only be dropped once it has expired for all of them. That
///     is the cheap path, and it serves the Application with the longest retention, whose Log Events are therefore kept
///     until their whole day has expired. Applications with a shorter retention have their Log Events deleted row by row.
/// </remarks>
/// <param name="applicationRepository">An Instance of a IApplicationRepository to read the retention of the Applications.</param>
/// <param name="logEventRepository">An Instance of a ILogEventRepository to remove the Log Events.</param>
/// <param name="options">The default retention.</param>
/// <param name="timeProvider">The TimeProvider to decide what has expired.</param>
/// <param name="logger">The Logger to record what was removed.</param>
public sealed class ApplyRetentionService(
    IApplicationRepository applicationRepository,
    ILogEventRepository logEventRepository,
    IOptions<RetentionOptions> options,
    TimeProvider timeProvider,
    ILogger<ApplyRetentionService> logger)
{
    /// <summary>
    ///     Removes all expired Log Events. If it is interrupted, the next run finishes it.
    /// </summary>
    /// <param name="cancellationToken">CancellationToken to cancel the run.</param>
    /// <returns>A Task that completes once the expired Log Events are removed.</returns>
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var defaultDays = options.Value.DefaultDays;

        var applications = await applicationRepository.GetApplications(cancellationToken);

        var longestDays = applications.Count > 0
            ? applications.Max(application => application.RetentionDays ?? defaultDays)
            : defaultDays;

        // First, as it is the cheap path, and may leave fewer rows to delete below.
        while (await logEventRepository.DropOldestExpiredPartition(defaultDays, now, cancellationToken) is { } day)
        {
            logger.PartitionDropped(day);
        }

        // The list only decides who needs this, so being out of date costs at most a wait for the next run. What gets
        // removed depends on the current retention, which the repository reads together with each removal.
        foreach (var application in applications)
        {
            var days = application.RetentionDays ?? defaultDays;

            if (days >= longestDays)
            {
                continue;
            }

            var deleted = await logEventRepository.DeleteExpired(application.Id, defaultDays, now, cancellationToken);

            if (deleted > 0)
            {
                logger.ExpiredLogEventsDeleted(application.Id, deleted);
            }
        }
    }
}
