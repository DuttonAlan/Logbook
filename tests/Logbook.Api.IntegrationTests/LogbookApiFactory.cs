// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net.Http.Headers;

using Logbook.Infrastructure.Persistence;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

using Testcontainers.PostgreSql;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Runs the Api against a PostgreSQL container.
/// </summary>
public sealed class LogbookApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    ///     The admin key the Api is configured with.
    /// </summary>
    public const string AdminKey = "integration-tests-admin-key-0123456789";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18-alpine").Build();

    /// <summary>
    ///     Creates a client that sends the admin key with every request.
    /// </summary>
    /// <returns>The client.</returns>
    public HttpClient CreateAdminClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        return client;
    }

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync();

        // The Api does not apply migrations itself.
        var options = new DbContextOptionsBuilder<LogbookContext>().UseNpgsql(_database.GetConnectionString()).Options;

        await using var context = new LogbookContext(options);
        await context.Database.MigrateAsync();
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Not Development, so the settings of appsettings.Development.json are not used.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Database", _database.GetConnectionString());
        builder.UseSetting("AdminApi:Key", AdminKey);

        // Tests wait for Log Events to be written, so they should not wait for a batch to fill up.
        builder.UseSetting("LogIngestion:WriteInterval", "00:00:00.020");
    }
}
