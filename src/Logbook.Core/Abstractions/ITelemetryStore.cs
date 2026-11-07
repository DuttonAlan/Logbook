// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.Entities;

namespace Logbook.Core.Abstractions;

/// <summary>
///     Defines what the background writer needs to store records.
/// </summary>
/// <typeparam name="T">The type of the records.</typeparam>
public interface ITelemetryStore<in T>
    where T : ITelemetryRecord
{
    /// <summary>
    ///     Stores records: either all of them or none. Records of an Application that no longer exists are discarded.
    /// </summary>
    /// <param name="records">The records to store.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>A Task that completes once the records are stored.</returns>
    public Task AddRange(IReadOnlyCollection<T> records, CancellationToken cancellationToken = default);
}
