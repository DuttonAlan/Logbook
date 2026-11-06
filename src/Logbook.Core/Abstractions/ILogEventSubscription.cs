// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Application.LogEvents.Tail;

namespace Logbook.Core.Abstractions;

/// <summary>
///     The batches of written Log Events for one follower of the <see cref="ILogEventFeed"/>.
/// </summary>
public interface ILogEventSubscription : IDisposable
{
    /// <summary>
    ///     Waits for the next written batch.
    /// </summary>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="cancellationToken">CancellationToken to stop waiting.</param>
    /// <returns>The batch, or null if none was written in time.</returns>
    public ValueTask<WrittenLogEvents?> ReadAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
