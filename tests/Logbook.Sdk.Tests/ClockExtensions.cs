// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Microsoft.Extensions.Time.Testing;

namespace Logbook.Sdk.Tests;

/// <summary>
///     Helps to move a fake clock while the sender runs on another thread.
/// </summary>
internal static class ClockExtensions
{
    /// <summary>
    ///     Moves the clock until the condition holds, since the sender may start waiting just after any given move. The
    ///     limit in real time is only there so that a bug ends in a failure instead of a test that hangs.
    /// </summary>
    /// <param name="time">The clock.</param>
    /// <param name="step">How far to move it at a time.</param>
    /// <param name="condition">The condition to wait for.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    public static async Task AdvanceUntilAsync(this FakeTimeProvider time, TimeSpan step, Func<bool> condition)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(30));

        while (!condition())
        {
            time.Advance(step);
            await Task.Delay(1, limit.Token);
        }
    }
}
