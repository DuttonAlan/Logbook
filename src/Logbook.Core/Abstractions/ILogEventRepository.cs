// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.Entities;

namespace Logbook.Core.Abstractions;

/// <summary>
///     Defines a contract for Log Event operations.
/// </summary>
public interface ILogEventRepository
{
    /// <summary>
    ///     Stores Log Events: either all of them or none.
    /// </summary>
    /// <param name="logEvents">The Log Events to store.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>A Task that completes once the Log Events are stored.</returns>
    public Task AddRange(IReadOnlyCollection<LogEvent> logEvents, CancellationToken cancellationToken = default);
}
