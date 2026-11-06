// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Abstractions;

/// <summary>
///     Tells who follows the Log Events which of them were just stored by this server.
/// </summary>
public interface ILogEventFeed
{
    /// <summary>
    ///     Starts following. Batches written from now on can be read from the subscription.
    /// </summary>
    /// <returns>The subscription, which must be disposed to stop following.</returns>
    public ILogEventSubscription Subscribe();
}
