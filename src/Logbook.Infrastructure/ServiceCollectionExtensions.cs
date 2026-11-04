// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Infrastructure.HealthChecks;
using Logbook.Infrastructure.Persistence;
using Logbook.Infrastructure.Persistence.Repositories;
using Logbook.Infrastructure.Security;

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

        serviceCollection.AddSingleton<IApiKeyGenerator, ApiKeyGenerator>();
        serviceCollection.AddSingleton<IApiKeyHasher, Sha256ApiKeyHasher>();

        serviceCollection.AddHealthChecks()
            .AddDbContextCheck<LogbookContext>("database", tags: [HealthCheckTags.Ready])
            .AddCheck<MigrationsHealthCheck>("migrations", tags: [HealthCheckTags.Ready]);

        return serviceCollection;
    }
}
