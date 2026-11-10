// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.Metrics.Ingest;

/// <summary>
///     Represents the result of receiving metric points.
/// </summary>
/// <param name="Accepted" example="20">The number of accepted metric points, which are queued for writing.</param>
public sealed record IngestMetricsResponse(int Accepted);
