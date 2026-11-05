// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net;
using System.Net.Http.Json;

using Logbook.Core.Abstractions;
using Logbook.Core.Application.Applications.Create;
using Logbook.Core.Domain.Entities;
using Logbook.Infrastructure.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks that deleting an Application also deletes its Log Events, including those still in the queue.
/// </summary>
/// <param name="factory">The Api under test.</param>
public sealed class DeleteApplicationLogEventsTests(LogbookApiFactory factory) : IClassFixture<LogbookApiFactory>
{
    private const string Logs = "/api/v1/logs";
    private const string Applications = "/api/v1/applications";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Delete_RemovesTheLogEvents_OfThatApplicationOnly()
    {
        var application = await CreateApplicationAsync();
        var other = await CreateApplicationAsync();
        await SendAsync(factory, application, 3);
        await SendAsync(factory, other, 2);
        await WaitForCountAsync(application.Id, 3);
        await WaitForCountAsync(other.Id, 2);

        await DeleteAsync(application.Id);

        Assert.Equal(0, await CountAsync(application.Id));
        Assert.Equal(2, await CountAsync(other.Id));
    }

    [Fact]
    public async Task Delete_RemovesMoreLogEventsThanOneStepDeletes()
    {
        var application = await CreateApplicationAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var now = DateTime.UtcNow;
            var events = Enumerable.Range(0, 25_000).Select(i => LogEvent.Create(application.Id, now, LogEventLevel.Information, $"m{i}")).ToList();

            await scope.ServiceProvider.GetRequiredService<ILogEventRepository>().AddRange(events, Token);
        }

        Assert.Equal(25_000, await CountAsync(application.Id));

        await DeleteAsync(application.Id);

        Assert.Equal(0, await CountAsync(application.Id));
    }

    [Fact]
    public async Task LogEvents_StillInTheQueue_DoNotOutliveTheirApplication()
    {
        var application = await CreateApplicationAsync();

        // The interval keeps the Log Events in the queue until the host stops, which is after the deletion.
        var waiting = factory.WithWebHostBuilder(builder => builder.UseSetting("LogIngestion:WriteInterval", "01:00:00"));
        await SendAsync(waiting, application, 3);

        await DeleteAsync(application.Id);
        await waiting.DisposeAsync();

        Assert.Equal(0, await CountAsync(application.Id));
    }

    [Fact]
    public async Task Writer_KeepsLogEventsOfExistingApplications_InABatchWithADeletedOne()
    {
        var application = await CreateApplicationAsync();
        var deleted = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var scope = factory.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<ILogEventRepository>().AddRange(
            [LogEvent.Create(application.Id, now, LogEventLevel.Error, "kept"), LogEvent.Create(deleted, now, LogEventLevel.Error, "discarded")],
            Token);

        Assert.Equal(1, await CountAsync(application.Id));
        Assert.Equal(0, await CountAsync(deleted));
    }

    [Fact]
    public async Task Delete_OfAnApplicationThatIsGone_RemovesLogEventsLeftBehind()
    {
        // Log Events left by an interrupted deletion, or from before deleting them was introduced.
        var gone = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<LogbookContext>();
            var now = DateTime.UtcNow;

            await context.Database.ExecuteSqlAsync($"SELECT telemetry.ensure_log_event_partitions({new[] { DateOnly.FromDateTime(now) }})", Token);
            context.LogEvents.Add(LogEvent.Create(gone, now, LogEventLevel.Error, "left behind"));
            await context.SaveChangesAsync(Token);
        }

        Assert.Equal(1, await CountAsync(gone));

        await DeleteAsync(gone);

        Assert.Equal(0, await CountAsync(gone));
    }

    private static async Task SendAsync(WebApplicationFactory<Program> host, CreateApplicationResponse application, int count)
    {
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Logbook-Key", application.ApiKey.Key);

        for (var i = 0; i < count; i++)
        {
            using var content = LogEventBodies.Json(LogEventBodies.Valid());
            using var response = await client.PostAsync(Logs, content, Token);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
    }

    private async Task<CreateApplicationResponse> CreateApplicationAsync()
    {
        using var admin = factory.CreateAdminClient();
        using var response = await admin.PostAsJsonAsync(Applications, new CreateApplicationRequest($"Delete {Guid.NewGuid():N}"), Token);

        return (await response.Content.ReadFromJsonAsync<CreateApplicationResponse>(Token))!;
    }

    private async Task DeleteAsync(Guid applicationId)
    {
        using var admin = factory.CreateAdminClient();
        using var response = await admin.DeleteAsync($"{Applications}/{applicationId}", Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<int> CountAsync(Guid applicationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LogbookContext>();

        return await context.LogEvents.CountAsync(logEvent => logEvent.ApplicationId == applicationId, Token);
    }

    private async Task WaitForCountAsync(Guid applicationId, int count)
    {
        while (await CountAsync(applicationId) < count)
        {
            await Task.Delay(25, Token);
        }
    }
}
