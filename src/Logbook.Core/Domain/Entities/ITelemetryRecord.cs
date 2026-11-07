// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Domain.Entities;

/// <summary>
///     Something an Application sends that waits in memory before it is written to the database.
/// </summary>
public interface ITelemetryRecord
{
    /// <summary>
    ///     Id of the record.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    ///     Id of the <see cref="Application"/> that sent the record.
    /// </summary>
    public Guid ApplicationId { get; }

    /// <summary>
    ///     Estimates the memory the record holds while it waits in the queue. Unlike the size on the wire, a string takes
    ///     two bytes for each character, so text mostly in ASCII takes about twice as much.
    /// </summary>
    /// <returns>The estimated size in bytes.</returns>
    public long GetMemoryBytes();
}
