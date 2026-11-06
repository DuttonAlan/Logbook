// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Application.LogEvents.Search;
using Logbook.Core.Application.LogEvents.Tail;
using Logbook.Core.Domain.Entities;

namespace Logbook.Core.Abstractions;

/// <summary>
///     Defines a contract for Log Event operations.
/// </summary>
public interface ILogEventRepository
{
    /// <summary>
    ///     Stores Log Events: either all of them or none. Log Events of an Application that no longer exists are discarded.
    /// </summary>
    /// <param name="logEvents">The Log Events to store.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>A Task that completes once the Log Events are stored.</returns>
    public Task AddRange(IReadOnlyCollection<LogEvent> logEvents, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes all Log Events of an Application, in several steps. If it is interrupted, calling it again finishes it.
    /// </summary>
    /// <param name="applicationId">Id of the Application.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The number of deleted Log Events.</returns>
    public Task<long> DeleteByApplication(Guid applicationId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Searches Log Events, newest first.
    /// </summary>
    /// <param name="filter">The filters to apply.</param>
    /// <param name="after">The position to continue after, or null to start with the newest Log Event.</param>
    /// <param name="take">The maximum number of Log Events to return.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The untracked Log Events.</returns>
    public Task<IReadOnlyList<LogEvent>> Search(
        LogEventFilter filter,
        LogEventCursor? after,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets those Log Events of a written batch that match a filter, newest first.
    /// </summary>
    /// <param name="written">The written batch.</param>
    /// <param name="filter">The filters to apply.</param>
    /// <param name="take">The maximum number of Log Events to return.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The untracked Log Events.</returns>
    public Task<IReadOnlyList<LogEvent>> GetWritten(
        WrittenLogEvents written,
        LogEventFilter filter,
        int take,
        CancellationToken cancellationToken = default);
}
