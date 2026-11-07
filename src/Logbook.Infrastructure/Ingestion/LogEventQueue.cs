// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Application.LogEvents;
using Logbook.Core.Domain.Entities;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logbook.Infrastructure.Ingestion;

/// <summary>
///     The in-memory queue between the log endpoint and <see cref="LogEventWriterService"/>.
/// </summary>
/// <param name="options">The limit of the queue.</param>
/// <param name="logger">The Logger to record that the queue is full.</param>
public sealed class LogEventQueue(IOptions<LogIngestionOptions> options, ILogger<LogEventQueue> logger)
    : TelemetryQueue<LogEvent>(LogEventBatchWriter.KindName, options, logger), ILogEventQueue
{
}
