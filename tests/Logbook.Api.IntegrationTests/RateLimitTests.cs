// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;

using Logbook.Api.RateLimiting;
using Logbook.Core.Application.Applications.Create;
using Logbook.Core.Application.LogEvents.Search;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks the rate limit per API Key on sending Log Events.
/// </summary>
/// <param name="factory">The Api under test.</param>
public sealed class RateLimitTests(LogbookApiFactory factory) : IClassFixture<LogbookApiFactory>
{
    private const string Endpoint = "/api/v1/logs";
    private const string KeyHeader = "X-Logbook-Key";

    [Fact]
    public async Task OverTheLimit_IsTooManyRequests_WithRetryAfter_AndNothingIsAccepted()
    {
        await using var limited = WithLimit(2);
        var application = await CreateApplicationAsync();
        using var client = CreateSender(limited, application.ApiKey.Key);

        Assert.Equal(HttpStatusCode.Accepted, await SendAsync(client, "first"));
        Assert.Equal(HttpStatusCode.Accepted, await SendAsync(client, "second"));

        using var content = LogEventBodies.Json(Body("rejected"));
        using var response = await client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.InRange(response.Headers.RetryAfter!.Delta!.Value, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));

        // Both accepted Log Events arrive, and the rejected one was not on its way.
        var stored = await WaitForAsync(application.Id, 2);
        Assert.DoesNotContain(stored, item => item.Message == "rejected");
    }

    [Fact]
    public async Task OneApiKeyOverTheLimit_DoesNotAffectOtherApiKeys_EvenOfTheSameApplication()
    {
        await using var limited = WithLimit(1);
        var first = await CreateApplicationAsync();
        var second = await CreateApplicationAsync();
        var sameApplication = await IssueApiKeyAsync(first.Id);
        using var firstClient = CreateSender(limited, first.ApiKey.Key);
        using var secondClient = CreateSender(limited, second.ApiKey.Key);
        using var sameApplicationClient = CreateSender(limited, sameApplication);

        Assert.Equal(HttpStatusCode.Accepted, await SendAsync(firstClient, "a"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(firstClient, "b"));

        Assert.Equal(HttpStatusCode.Accepted, await SendAsync(secondClient, "c"));
        Assert.Equal(HttpStatusCode.Accepted, await SendAsync(sameApplicationClient, "d"));
    }

    [Fact]
    public async Task AfterABurst_TheBucketRefills_WithoutWaitingForAWholeMinute()
    {
        // 120 per minute refills 2 tokens per second.
        await using var limited = WithLimit(120);
        var application = await CreateApplicationAsync();
        using var client = CreateSender(limited, application.ApiKey.Key);

        HttpStatusCode status;

        do
        {
            status = await SendAsync(client, "burst");
        }
        while (status == HttpStatusCode.Accepted);

        Assert.Equal(HttpStatusCode.TooManyRequests, status);

        var started = TimeProvider.System.GetTimestamp();

        while (await SendAsync(client, "after") != HttpStatusCode.Accepted)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.True(TimeProvider.System.GetElapsedTime(started) < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task InvalidApiKeys_DoNotCountAgainstTheLimitOfAnApiKey()
    {
        await using var limited = WithLimit(1);
        using var client = CreateSender(limited, "not-a-valid-key");

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await SendAsync(client, "x"));
        }
    }

    [Fact]
    public async Task TooManyInvalidApiKeys_FromOneIpAddress_AreRejected_AlsoForValidApiKeys()
    {
        await using var limited = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:InvalidApiKeyPermitsPerMinute", "2"));
        var application = await CreateApplicationAsync();
        using var invalid = CreateSender(limited, "not-a-valid-key");
        using var valid = CreateSender(limited, application.ApiKey.Key);

        Assert.Equal(HttpStatusCode.Accepted, await SendAsync(valid, "before"));
        Assert.Equal(HttpStatusCode.Unauthorized, await SendAsync(invalid, "x"));
        Assert.Equal(HttpStatusCode.Unauthorized, await SendAsync(invalid, "x"));

        using var content = LogEventBodies.Json(Body("x"));
        using var rejected = await invalid.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        Assert.InRange(rejected.Headers.RetryAfter!.Delta!.Value, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));

        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(valid, "after"));
    }

    [Fact]
    public async Task ValidApiKeys_DoNotUseUpTheLimitForInvalidApiKeys()
    {
        await using var limited = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:InvalidApiKeyPermitsPerMinute", "1"));
        var application = await CreateApplicationAsync();
        using var valid = CreateSender(limited, application.ApiKey.Key);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Accepted, await SendAsync(valid, "fine"));
        }
    }

    [Fact]
    public async Task RequestsWithoutAnApiKey_AreUnauthorized_EvenOverTheLimit()
    {
        await using var limited = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:InvalidApiKeyPermitsPerMinute", "1"));
        using var invalid = CreateSender(limited, "not-a-valid-key");
        using var anonymous = limited.CreateClient();
        using var content = LogEventBodies.Json(Body("x"));

        Assert.Equal(HttpStatusCode.Unauthorized, await SendAsync(invalid, "x"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(invalid, "x"));

        using var response = await anonymous.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void InvalidApiKeyLimiter_StaysOverTheLimit_WhileTheBucketHoldsOnlyAFractionOfAToken()
    {
        var clock = new ManualTimeProvider();
        var limiter = new InvalidApiKeyLimiter(Options.Create(new RateLimitOptions { InvalidApiKeyPermitsPerMinute = 60 }), clock);

        for (var i = 0; i < 60; i++)
        {
            Assert.False(limiter.IsOverLimit("192.0.2.1", out _));
            limiter.RecordFailure("192.0.2.1");
        }

        Assert.True(limiter.IsOverLimit("192.0.2.1", out var retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(1), retryAfter);

        // Half a token: more than nothing, but not enough for another try.
        clock.Advance(TimeSpan.FromMilliseconds(500));
        Assert.True(limiter.IsOverLimit("192.0.2.1", out retryAfter));
        Assert.Equal(TimeSpan.FromMilliseconds(500), retryAfter);

        clock.Advance(TimeSpan.FromMilliseconds(500));
        Assert.False(limiter.IsOverLimit("192.0.2.1", out _));

        limiter.RecordFailure("192.0.2.1");
        Assert.True(limiter.IsOverLimit("192.0.2.1", out _));
    }

    [Fact]
    public void InvalidApiKeyLimiter_KeepsAddressesApart_AndCheckingUsesUpNothing()
    {
        var limiter = new InvalidApiKeyLimiter(Options.Create(new RateLimitOptions { InvalidApiKeyPermitsPerMinute = 2 }), new ManualTimeProvider());

        limiter.RecordFailure("192.0.2.1");

        for (var i = 0; i < 10; i++)
        {
            Assert.False(limiter.IsOverLimit("192.0.2.1", out _));
        }

        limiter.RecordFailure("192.0.2.1");

        Assert.True(limiter.IsOverLimit("192.0.2.1", out _));
        Assert.False(limiter.IsOverLimit("2001:db8::1", out _));
    }

    [Fact]
    public void Reporter_WarnsOncePerMinuteForEachSender_AndNeverWithAKey()
    {
        var clock = new ManualTimeProvider();
        var logger = new CountingLogger();
        var reporter = new RateLimitReporter(clock, logger);

        reporter.ApiKeyOverLimit("first");
        reporter.ApiKeyOverLimit("first");
        reporter.ApiKeyOverLimit("second");
        reporter.TooManyInvalidApiKeys("192.0.2.1");
        reporter.TooManyInvalidApiKeys("192.0.2.1");
        Assert.Equal(3, logger.Warnings);

        clock.Advance(TimeSpan.FromSeconds(59));
        reporter.ApiKeyOverLimit("first");
        Assert.Equal(3, logger.Warnings);

        clock.Advance(TimeSpan.FromSeconds(1));
        reporter.ApiKeyOverLimit("first");
        Assert.Equal(4, logger.Warnings);
    }

    [Fact]
    public async Task InvalidRequests_UseUpTheLimit_BeforeTheBodyIsRead()
    {
        await using var limited = WithLimit(1);
        var application = await CreateApplicationAsync();
        using var client = CreateSender(limited, application.ApiKey.Key);

        using (var invalid = new StringContent("not json", Encoding.UTF8, "application/json"))
        using (var first = await client.PostAsync(Endpoint, invalid, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        }

        // A body that would be 400 is not looked at any more.
        using var again = new StringContent("not json", Encoding.UTF8, "application/json");
        using var second = await client.PostAsync(Endpoint, again, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task Search_IsNotLimited()
    {
        await using var limited = WithLimit(1);
        using var admin = limited.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new("Bearer", LogbookApiFactory.AdminKey);

        for (var i = 0; i < 5; i++)
        {
            using var response = await admin.GetAsync(Endpoint, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("ApiKeyPermitsPerMinute", "0")]
    [InlineData("ApiKeyPermitsPerMinute", "-1")]
    [InlineData("ApiKeyPermitsPerMinute", "1000001")]
    [InlineData("InvalidApiKeyPermitsPerMinute", "0")]
    [InlineData("InvalidApiKeyPermitsPerMinute", "1000001")]
    public void Startup_WithInvalidLimit_Fails(string setting, string limit)
    {
        using var misconfigured = factory.WithWebHostBuilder(builder => builder.UseSetting($"RateLimiting:{setting}", limit));

        Assert.Throws<OptionsValidationException>(() => misconfigured.CreateClient());
    }

    [Fact]
    public void DefaultLimit_IsValid()
    {
        var options = new RateLimitOptions();

        Assert.True(new RateLimitOptionsValidator().Validate(null, options).Succeeded);
        Assert.Equal(6000, options.ApiKeyPermitsPerMinute);
    }

    private static string Body(string message)
    {
        return $$"""[{ "timestamp": "{{DateTimeOffset.UtcNow:O}}", "level": "Error", "message": "{{message}}" }]""";
    }

    private static HttpClient CreateSender(WebApplicationFactory<Program> application, string key)
    {
        var client = application.CreateClient();
        client.DefaultRequestHeaders.Add(KeyHeader, key);

        return client;
    }

    private static async Task<HttpStatusCode> SendAsync(HttpClient client, string message)
    {
        using var content = LogEventBodies.Json(Body(message));
        using var response = await client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    private WebApplicationFactory<Program> WithLimit(int permitsPerMinute)
    {
        return factory.WithWebHostBuilder(builder => builder.UseSetting(
            "RateLimiting:ApiKeyPermitsPerMinute",
            permitsPerMinute.ToString(CultureInfo.InvariantCulture)));
    }

    private async Task<CreateApplicationResponse> CreateApplicationAsync()
    {
        using var admin = factory.CreateAdminClient();
        using var response = await admin.PostAsJsonAsync(
            "/api/v1/applications",
            new CreateApplicationRequest($"Rate Limit {Guid.NewGuid():N}"),
            TestContext.Current.CancellationToken);

        return (await response.Content.ReadFromJsonAsync<CreateApplicationResponse>(TestContext.Current.CancellationToken))!;
    }

    private async Task<string> IssueApiKeyAsync(Guid applicationId)
    {
        using var admin = factory.CreateAdminClient();
        using var response = await admin.PostAsync($"/api/v1/applications/{applicationId}/api-keys", content: null, TestContext.Current.CancellationToken);
        var issued = await response.Content.ReadFromJsonAsync<Core.Application.Applications.IssuedApiKeyResponse>(TestContext.Current.CancellationToken);

        return issued!.Key;
    }

    private async Task<IReadOnlyList<LogEventResponse>> WaitForAsync(Guid applicationId, int count)
    {
        using var admin = factory.CreateAdminClient();
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        while (true)
        {
            var page = await admin.GetFromJsonAsync<SearchLogEventsResponse>($"{Endpoint}?applicationId={applicationId}", options, TestContext.Current.CancellationToken);

            if (page!.Items.Count >= count)
            {
                return page.Items;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class CountingLogger : ILogger<RateLimitReporter>
    {
        public int Warnings { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings++;
            }
        }
    }
}
