// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Application.LogEvents.Search;

namespace Logbook.Core.Application.LogEvents.Tail;

/// <summary>
///     Represents one message of a live tail.
/// </summary>
/// <param name="Kind">The kind of the message.</param>
/// <param name="LogEvent">The Log Event, if the message is of the kind <see cref="LogEventTailMessageKind.LogEvent"/>.</param>
public sealed record LogEventTailMessage(LogEventTailMessageKind Kind, LogEventResponse? LogEvent = null);
