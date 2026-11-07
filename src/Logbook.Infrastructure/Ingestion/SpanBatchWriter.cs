// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Domain.Entities;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Logbook.Infrastructure.Ingestion;

/// <summary>
///     Writes a batch of spans to the database.
/// </summary>
/// <param name="scopeFactory">Creates a scope with its own DbContext per attempt.</param>
/// <param name="timeProvider">The clock for the delay between attempts.</param>
/// <param name="logger">The Logger to record failures.</param>
public sealed class SpanBatchWriter(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<SpanBatchWriter> logger)
    : TelemetryBatchWriter<TraceSpan, ISpanRepository>(KindName, scopeFactory, timeProvider, logger)
{
    /// <summary>
    ///     What spans are called in the logs.
    /// </summary>
    internal const string KindName = "spans";
}
