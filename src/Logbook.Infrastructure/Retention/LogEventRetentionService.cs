// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Application.LogEvents;
using Logbook.Core.Application.LogEvents.ApplyRetention;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Npgsql;

namespace Logbook.Infrastructure.Retention;

/// <summary>
///     Removes expired Log Events when the server starts, and then at the interval of the setting
///     <see cref="RetentionOptions.Interval"/>.
/// </summary>
/// <remarks>
///     With several servers, each one runs it. That is harmless: dropping a partition takes one at a time, and what one
///     server has removed the other no longer finds. A failure is left for the next run. Only its type and SQL state are
///     logged, because the context of a PostgreSQL error can contain a row with the content of a Log Event.
/// </remarks>
/// <param name="scopeFactory">Creates a scope with its own DbContext per run.</param>
/// <param name="options">The interval.</param>
/// <param name="timeProvider">The clock for the interval.</param>
/// <param name="logger">The Logger to record failures.</param>
public sealed class LogEventRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<RetentionOptions> options,
    TimeProvider timeProvider,
    ILogger<LogEventRetentionService> logger) : BackgroundService
{
    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.Interval;

        using var timer = new PeriodicTimer(interval, timeProvider);

        try
        {
            do
            {
                await RunAsync(interval, stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping. The next start finishes the run.
        }
    }

    private async Task RunAsync(TimeSpan interval, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            await scope.ServiceProvider.GetRequiredService<ApplyRetentionService>().ExecuteAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.RunFailed(exception.GetType().Name, (exception as PostgresException)?.SqlState, interval);
        }
    }
}
