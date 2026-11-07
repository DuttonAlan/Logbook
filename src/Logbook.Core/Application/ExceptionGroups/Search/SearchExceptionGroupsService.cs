// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Application.LogEvents.Search;
using Logbook.Core.Domain.Exceptions;

namespace Logbook.Core.Application.ExceptionGroups.Search;

/// <summary>
///     The Service to find the groups of exceptions in the stored Log Events.
/// </summary>
/// <remarks>
///     Groups are computed from the Log Events rather than kept in a table of their own. So they cannot disagree with what is
///     stored: retention and deleting an Application need no extra work, and any time range can be asked for. Exceptions are
///     rare compared to other Log Events, and a small index holds only those.
/// </remarks>
/// <param name="logEventRepository">An Instance of a ILogEventRepository to read the groups.</param>
public sealed class SearchExceptionGroupsService(ILogEventRepository logEventRepository)
{
    /// <summary>
    ///     Gets the groups of the exceptions matching the filter.
    /// </summary>
    /// <param name="from">Only count occurrences at or after this point in time.</param>
    /// <param name="to">Only count occurrences before this point in time.</param>
    /// <param name="applicationId">Only groups of this Application.</param>
    /// <param name="environment">Only occurrences in this Environment, compared exactly.</param>
    /// <param name="sort">The order of the groups.</param>
    /// <param name="limit">The maximum number of groups.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The groups.</returns>
    /// <exception cref="DomainValidationException">Thrown when the time range is empty.</exception>
    public async Task<SearchExceptionGroupsResponse> ExecuteAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? applicationId,
        string? environment,
        ExceptionGroupSort sort,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (from >= to)
        {
            throw new DomainValidationException("'from' must be earlier than 'to'.", nameof(from));
        }

        var filter = new LogEventFilter(from, to, ApplicationId: applicationId, Environment: environment).Normalize();

        return new SearchExceptionGroupsResponse(await logEventRepository.GetExceptionGroups(filter, sort, limit, cancellationToken));
    }
}
