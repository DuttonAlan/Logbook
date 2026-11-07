// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.Entities;

namespace Logbook.Core.Abstractions;

/// <summary>
///     Holds received Log Events in memory until they are written to the database in the background.
/// </summary>
public interface ILogEventQueue : ITelemetryQueue<LogEvent>
{
}
