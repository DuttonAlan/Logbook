// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Sdk;

/// <summary>
///     Something that can wait in a <see cref="TelemetryBuffer{T}"/>.
/// </summary>
internal interface ITelemetryItem
{
    /// <summary>
    ///     Estimates the memory the item holds while it waits in the buffer.
    /// </summary>
    /// <returns>The estimated size in bytes.</returns>
    public long GetMemoryBytes();
}
