// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace Logbook.Infrastructure.Persistence.Repositories;

/// <inheritdoc/>
/// <param name="context">The DbContext to access the Log Events.</param>
public sealed class LogEventRepository(LogbookContext context) : ILogEventRepository
{
    /// <inheritdoc/>
    public async Task AddRange(IReadOnlyCollection<LogEvent> logEvents, CancellationToken cancellationToken = default)
    {
        // A row without a partition for its day cannot be inserted. The function only looks up partitions that exist.
        var days = logEvents.Select(logEvent => DateOnly.FromDateTime(logEvent.Timestamp)).Distinct().ToArray();

        await context.Database.ExecuteSqlAsync(
            $"SELECT telemetry.ensure_log_event_partitions({days})",
            cancellationToken);

        context.LogEvents.AddRange(logEvents);
        await context.SaveChangesAsync(cancellationToken);
    }
}
