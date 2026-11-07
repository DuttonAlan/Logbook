// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.Spans.Ingest;

/// <summary>
///     Represents the result of receiving spans.
/// </summary>
/// <param name="Accepted" example="100">The number of accepted spans, which are queued for writing.</param>
public sealed record IngestSpansResponse(int Accepted);
