// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Infrastructure.HealthChecks;
using Logbook.Infrastructure.Ingestion;
using Logbook.Infrastructure.Persistence;
using Logbook.Infrastructure.Persistence.Repositories;
using Logbook.Infrastructure.Retention;
using Logbook.Infrastructure.Security;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Logbook.Infrastructure;

/// <summary>
///     Defines Extension Methods for the IServiceCollection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the Infrastructure to the Service Collection.
    /// </summary>
    /// <param name="serviceCollection">The Service Collection to add the services to.</param>
    /// <param name="configuration">The configuration with the connection string <c>Database</c>.</param>
    /// <returns>The Service Collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the connection string is not configured.</exception>
    public static IServiceCollection AddInfrastructure(this IServiceCollection serviceCollection, IConfiguration configuration)
    {
        serviceCollection.TryAddSingleton(TimeProvider.System);

        var connectionString = configuration.GetConnectionString("Database");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "The connection string 'Database' is not configured. Set 'ConnectionStrings:Database', "
                + "e.g. via the environment variable 'ConnectionStrings__Database'.");
        }

        serviceCollection.AddDbContext<LogbookContext>(options => options.UseNpgsql(connectionString));

        serviceCollection.AddTransient<IApplicationRepository, ApplicationRepository>();
        serviceCollection.AddTransient<ILogEventRepository, LogEventRepository>();
        serviceCollection.AddTransient<ISpanRepository, SpanRepository>();
        serviceCollection.AddTransient<IMetricRepository, MetricRepository>();
        serviceCollection.AddTransient<ISavedSearchRepository, SavedSearchRepository>();

        serviceCollection.AddSingleton<LogEventQueue>();
        serviceCollection.AddSingleton<ILogEventQueue>(services => services.GetRequiredService<LogEventQueue>());
        serviceCollection.AddSingleton<LogEventBatchWriter>();
        serviceCollection.AddSingleton<LogEventFeed>();
        serviceCollection.AddSingleton<ILogEventFeed>(services => services.GetRequiredService<LogEventFeed>());
        serviceCollection.AddHostedService<LogEventWriterService>();

        serviceCollection.AddSingleton<SpanQueue>();
        serviceCollection.AddSingleton<ISpanQueue>(services => services.GetRequiredService<SpanQueue>());
        serviceCollection.AddSingleton<SpanBatchWriter>();
        serviceCollection.AddHostedService<SpanWriterService>();

        serviceCollection.AddSingleton<MetricQueue>();
        serviceCollection.AddSingleton<IMetricQueue>(services => services.GetRequiredService<MetricQueue>());
        serviceCollection.AddSingleton<MetricBatchWriter>();
        serviceCollection.AddHostedService<MetricWriterService>();

        serviceCollection.AddHostedService<LogEventRetentionService>();

        // In the database, so that sign-in sessions survive a restart and work on every server. The fixed name keeps the
        // keys usable when the content root changes, e.g. with a new image.
        serviceCollection.AddDataProtection()
            .SetApplicationName("Logbook")
            .PersistKeysToDbContext<LogbookContext>();

        serviceCollection.AddSingleton<IApiKeyGenerator, ApiKeyGenerator>();
        serviceCollection.AddSingleton<IApiKeyHasher, Sha256ApiKeyHasher>();

        serviceCollection.AddHealthChecks()
            .AddDbContextCheck<LogbookContext>("database", tags: [HealthCheckTags.Ready])
            .AddCheck<MigrationsHealthCheck>("migrations", tags: [HealthCheckTags.Ready]);

        return serviceCollection;
    }
}
