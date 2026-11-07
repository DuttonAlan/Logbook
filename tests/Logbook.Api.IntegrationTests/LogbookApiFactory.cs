// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

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
    ///     The name of the local administrator.
    /// </summary>
    public const string AdminUserName = "admin";

    /// <summary>
    ///     The password of the local administrator the Api is configured with.
    /// </summary>
    public const string AdminPassword = "integration-tests-password-0123456789";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18-alpine").Build();

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
        builder.UseSetting("SignIn:Administrator:Password", AdminPassword);

        // Tests wait for Log Events to be written, so they should not wait for a batch to fill up.
        builder.UseSetting("LogIngestion:WriteInterval", "00:00:00.020");
    }
}
