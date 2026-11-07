// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.Entities;

namespace Logbook.Core.Abstractions;

/// <summary>
///     Holds received records in memory until they are written to the database in the background.
/// </summary>
/// <typeparam name="T">The type of the records.</typeparam>
public interface ITelemetryQueue<in T>
    where T : ITelemetryRecord
{
    /// <summary>
    ///     Adds a batch of records to the queue: either all of them or none, so a sender can safely retry a rejected batch
    ///     without creating duplicates.
    /// </summary>
    /// <param name="records">The records to add.</param>
    /// <returns><see langword="true"/> if the batch was added, or <see langword="false"/> if the queue is full.</returns>
    public bool TryEnqueue(IReadOnlyCollection<T> records);
}
