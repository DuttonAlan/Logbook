// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Globalization;
using System.Net;
using System.Net.Http.Json;

using Logbook.Core.Abstractions;
using Logbook.Core.Application.Applications.ChangeRetention;
using Logbook.Core.Application.Applications.Create;
using Logbook.Core.Application.Applications.Get;
using Logbook.Core.Application.LogEvents.ApplyRetention;
using Logbook.Core.Domain.Entities;
using Logbook.Infrastructure.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks the retention per Application, with real partitions.
/// </summary>
/// <param name="factory">The Api under test.</param>
public sealed class RetentionTests(LogbookApiFactory factory) : IClassFixture<LogbookApiFactory>, IAsyncLifetime
{
    private const string Applications = "/api/v1/applications";

    private readonly HttpClient _admin = factory.CreateAdminClient();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static DateTime Now => DateTime.UtcNow;

    /// <summary>
    ///     What can be dropped depends on the longest retention of all Applications, so each test starts without any.
    /// </summary>
    /// <returns>A Task that completes once they are gone.</returns>
    public async ValueTask InitializeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LogbookContext>();

        await context.Applications.ExecuteDeleteAsync(Token);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _admin.Dispose();

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task PartitionPastEveryRetention_IsDropped()
    {
        var application = await CreateApplicationAsync();
        var old = Now.AddDays(-400);

        await StoreAsync(application, old, "old");
        await StoreAsync(application, Now.AddDays(-1), "recent");

        Assert.True(await PartitionExistsAsync(old));

        await ApplyRetentionAsync();

        Assert.False(await PartitionExistsAsync(old));
        Assert.Equal(["recent"], await MessagesAsync(application));
    }

    [Fact]
    public async Task Partition_IsKept_UntilItsWholeDayHasExpired_ForTheLongestRetention()
    {
        var application = await CreateApplicationAsync(retentionDays: 10);
        var expired = Now.AddDays(-12);
        var partlyExpired = Now.AddDays(-10);

        await StoreAsync(application, expired, "expired");
        await StoreAsync(application, partlyExpired.Date, "day of the limit");

        await ApplyRetentionAsync();

        Assert.False(await PartitionExistsAsync(expired));
        Assert.True(await PartitionExistsAsync(partlyExpired));
        Assert.Equal(["day of the limit"], await MessagesAsync(application));
    }

    [Fact]
    public async Task ShortRetention_DeletesRows_WhileALongerOneKeepsThePartition()
    {
        var shortRetention = await CreateApplicationAsync(retentionDays: 7);
        var longRetention = await CreateApplicationAsync(retentionDays: 100);
        var old = Now.AddDays(-40);

        await StoreAsync(shortRetention, old, "short old");
        await StoreAsync(shortRetention, Now.AddDays(-6), "short recent");
        await StoreAsync(longRetention, old, "long old");

        await ApplyRetentionAsync();

        Assert.True(await PartitionExistsAsync(old));
        Assert.Equal(["short recent"], await MessagesAsync(shortRetention));
        Assert.Equal(["long old"], await MessagesAsync(longRetention));
    }

    [Fact]
    public async Task RaisingTheRetentionDuringARun_KeepsThePartition()
    {
        var application = await CreateApplicationAsync(retentionDays: 2);
        var old = Now.AddDays(-5);
        await StoreAsync(application, old, "keep me");

        await RaiseRetentionDuringARunAsync(application, retentionDays: 100);

        Assert.True(await PartitionExistsAsync(old));
        Assert.Equal(["keep me"], await MessagesAsync(application));
    }

    [Fact]
    public async Task RaisingTheRetentionDuringARun_KeepsTheRows()
    {
        var application = await CreateApplicationAsync(retentionDays: 2);
        var longest = await CreateApplicationAsync(retentionDays: 200);
        await StoreAsync(application, Now.AddDays(-5), "keep me");
        await StoreAsync(longest, Now.AddDays(-5), "other");

        await RaiseRetentionDuringARunAsync(application, retentionDays: 100);

        Assert.Equal(["keep me"], await MessagesAsync(application));
    }

    [Fact]
    public async Task ApplicationWithoutRetention_UsesTheDefaultOfTheServer()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("Retention:DefaultDays", "5"));
        using var admin = host.CreateAdminClient();

        var withDefault = await CreateApplicationAsync();
        var withOwn = await CreateApplicationAsync(retentionDays: 20);

        await StoreAsync(withDefault, Now.AddDays(-10), "default");
        await StoreAsync(withOwn, Now.AddDays(-10), "own");

        await ApplyRetentionAsync(host);

        Assert.Empty(await MessagesAsync(withDefault));
        Assert.Equal(["own"], await MessagesAsync(withOwn));

        var application = await admin.GetFromJsonAsync<GetApplicationResponse>($"{Applications}/{withDefault}", Token);
        Assert.Null(application!.RetentionDays);
        Assert.Equal(5, application.EffectiveRetentionDays);
    }

    [Fact]
    public async Task EmptyDatabase_DropsPartitionsPastTheDefault()
    {
        var old = Now.AddDays(-300);
        var application = await CreateApplicationAsync();
        await StoreAsync(application, old, "old");

        await InitializeAsync();
        await ApplyRetentionAsync();

        Assert.False(await PartitionExistsAsync(old));
    }

    [Fact]
    public async Task LogEventForADroppedDay_RecreatesThePartition()
    {
        var application = await CreateApplicationAsync(retentionDays: 2);
        var old = Now.AddDays(-5);

        await StoreAsync(application, old, "first");
        await ApplyRetentionAsync();
        Assert.False(await PartitionExistsAsync(old));

        await StoreAsync(application, old, "late");

        Assert.Equal(["late"], await MessagesAsync(application));
    }

    [Fact]
    public async Task BackgroundService_RunsAtTheInterval()
    {
        var application = await CreateApplicationAsync();

        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("Retention:Interval", "00:00:00.100"));
        using var client = host.CreateClient();
        using var started = await client.GetAsync("/health/live", Token);

        // Stored after the start, so only a later run can remove it.
        await StoreAsync(application, Now.AddDays(-500), "old");

        for (var i = 0; i < 100 && (await MessagesAsync(application)).Count > 0; i++)
        {
            await Task.Delay(50, Token);
        }

        Assert.Empty(await MessagesAsync(application));
    }

    [Fact]
    public async Task Api_ChangesTheRetention_AndResetsIt()
    {
        var application = await CreateApplicationAsync();

        using var set = await _admin.PutAsJsonAsync($"{Applications}/{application}/retention", new ChangeRetentionRequest(14), Token);
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        var changed = await _admin.GetFromJsonAsync<GetApplicationResponse>($"{Applications}/{application}", Token);
        Assert.Equal(14, changed!.RetentionDays);
        Assert.Equal(14, changed.EffectiveRetentionDays);
        Assert.NotNull(changed.ModifiedAt);

        using var reset = await _admin.PutAsJsonAsync($"{Applications}/{application}/retention", new ChangeRetentionRequest(null), Token);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var back = await _admin.GetFromJsonAsync<GetApplicationResponse>($"{Applications}/{application}", Token);
        Assert.Null(back!.RetentionDays);
        Assert.Equal(30, back.EffectiveRetentionDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3651)]
    public async Task Api_RejectsARetentionOutOfRange(int days)
    {
        var application = await CreateApplicationAsync();

        using var response = await _admin.PutAsJsonAsync($"{Applications}/{application}/retention", new ChangeRetentionRequest(days), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Api_UnknownApplication_IsNotFound()
    {
        using var response = await _admin.PutAsJsonAsync($"{Applications}/{Guid.NewGuid()}/retention", new ChangeRetentionRequest(14), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Api_WithoutSession_IsUnauthorized()
    {
        var application = await CreateApplicationAsync();
        using var client = factory.CreateClient();

        using var response = await client.PutAsJsonAsync($"{Applications}/{application}/retention", new ChangeRetentionRequest(14), Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Retention:DefaultDays", "0")]
    [InlineData("Retention:DefaultDays", "3651")]
    [InlineData("Retention:Interval", "00:00:00")]
    [InlineData("Retention:Interval", "31.00:00:00")]
    public void Startup_WithInvalidSetting_Fails(string setting, string value)
    {
        using var misconfigured = factory.WithWebHostBuilder(builder => builder.UseSetting(setting, value));

        Assert.Throws<OptionsValidationException>(() => misconfigured.CreateClient());
    }

    private async Task<Guid> CreateApplicationAsync(int? retentionDays = null)
    {
        using var created = await _admin.PostAsJsonAsync(Applications, new CreateApplicationRequest($"Retention {Guid.NewGuid():N}"), Token);
        var application = await created.Content.ReadFromJsonAsync<CreateApplicationResponse>(Token);

        if (retentionDays is not null)
        {
            using var changed = await _admin.PutAsJsonAsync($"{Applications}/{application!.Id}/retention", new ChangeRetentionRequest(retentionDays), Token);
            changed.EnsureSuccessStatusCode();
        }

        return application!.Id;
    }

    /// <summary>
    ///     Directly through the repository, as the log API rejects Log Events older than a week.
    /// </summary>
    private async Task StoreAsync(Guid applicationId, DateTime timestamp, string message)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<ILogEventRepository>().AddRange(
            [LogEvent.Create(applicationId, timestamp, LogEventLevel.Information, message)],
            Token);
    }

    private Task ApplyRetentionAsync() => ApplyRetentionAsync(factory);

    private static async Task ApplyRetentionAsync(WebApplicationFactory<Program> host)
    {
        await using var scope = host.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<ApplyRetentionService>().ExecuteAsync(Token);
    }

    /// <summary>
    ///     Starts a run while the change of the retention is not committed yet, so the run has read the old value, or would
    ///     have, if it did not wait for the change. This is the moment a change can arrive during a long run.
    /// </summary>
    private async Task RaiseRetentionDuringARunAsync(Guid applicationId, int retentionDays)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LogbookContext>();

        await using var transaction = await context.Database.BeginTransactionAsync(Token);

        await context.Database.ExecuteSqlAsync(
            $"""UPDATE config.applications SET "RetentionDays" = {retentionDays} WHERE "Id" = {applicationId}""",
            Token);

        var run = ApplyRetentionAsync();

        await Task.Delay(500, Token);

        Assert.False(run.IsCompleted, "The run must wait for the change of the retention.");

        await transaction.CommitAsync(Token);
        await run;
    }

    private async Task<List<string>> MessagesAsync(Guid applicationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<LogbookContext>().LogEvents
            .Where(logEvent => logEvent.ApplicationId == applicationId)
            .OrderBy(logEvent => logEvent.Timestamp)
            .Select(logEvent => logEvent.Message)
            .ToListAsync(Token);
    }

    private async Task<bool> PartitionExistsAsync(DateTime day)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        var name = $"telemetry.log_events_{day.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}";

        var found = await scope.ServiceProvider.GetRequiredService<LogbookContext>().Database
            .SqlQuery<string?>($"""SELECT to_regclass({name})::text AS "Value" """)
            .ToListAsync(Token);

        return found[0] is not null;
    }
}
