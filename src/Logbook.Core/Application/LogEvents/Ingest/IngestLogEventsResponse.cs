// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.LogEvents.Ingest;

/// <summary>
///     Represents the response to a request to receive Log Events.
/// </summary>
/// <param name="Accepted" example="100">Number of Log Events that were accepted.</param>
public sealed record IngestLogEventsResponse(int Accepted);
