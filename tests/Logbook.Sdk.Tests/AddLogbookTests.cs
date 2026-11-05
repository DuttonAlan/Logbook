// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Diagnostics.Tracing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Logbook.Sdk.Tests;

/// <summary>
///     Checks the setup of the provider: configuration, Environment, and everything that must never stop an application from
///     starting. The packages must also leave out their own logs, as they would otherwise feed themselves.
/// </summary>
public sealed class AddLogbookTests
{
    [Fact]
    public void Options_AreReadFromTheSectionLogbook()
    {
        using var provider = BuildProvider(
            new Dictionary<string, string?>
            {
                ["Logbook:Endpoint"] = "https://logbook.example.com",
                ["Logbook:ApiKey"] = "from-section",
                ["Logbook:Environment"] = "FromSection",
                ["Logbook:BatchSize"] = "7",
                ["Logbook:FlushInterval"] = "00:00:09",
                ["Logbook:MaxQueueBytes"] = "1234",
                ["Logbook:ShutdownTimeout"] = "00:00:00"
            });

        var options = provider.GetRequiredService<IOptions<LogbookOptions>>().Value;

        Assert.Equal(new Uri("https://logbook.example.com"), options.Endpoint);
        Assert.Equal("from-section", options.ApiKey);
        Assert.Equal("FromSection", options.Environment);
        Assert.Equal(7, options.BatchSize);
        Assert.Equal(TimeSpan.FromSeconds(9), options.FlushInterval);
        Assert.Equal(1234, options.MaxQueueBytes);
        Assert.True(provider.GetRequiredService<LogbookClient>().IsEnabled);
    }

    [Fact]
    public void TheDelegate_OverridesTheSection()
    {
        using var provider = BuildProvider(
            new Dictionary<string, string?> { ["Logbook:ApiKey"] = "from-section", ["Logbook:BatchSize"] = "7" },
            options =>
            {
                options.ApiKey = "from-delegate";
                options.Endpoint = new Uri("https://logbook.example.com");
                options.ShutdownTimeout = TimeSpan.Zero;
            });

        var options = provider.GetRequiredService<IOptions<LogbookOptions>>().Value;

        Assert.Equal("from-delegate", options.ApiKey);
        Assert.Equal(7, options.BatchSize);
    }

    [Fact]
    public void TheEnvironment_ComesFromTheHost()
    {
        var sent = LoggerHarness.SendOne(
            logger => logger.LogInformation("Hello"),
            configureServices: services => services.AddSingleton<IHostEnvironment>(new TestEnvironment("Staging")));

        Assert.Equal("Staging", sent.GetProperty("environment").GetString());
    }

    [Fact]
    public void TheEnvironment_InTheOptions_OverridesTheHost()
    {
        var sent = LoggerHarness.SendOne(
            logger => logger.LogInformation("Hello"),
            options => options.Environment = "Override",
            services => services.AddSingleton<IHostEnvironment>(new TestEnvironment("Staging")));

        Assert.Equal("Override", sent.GetProperty("environment").GetString());
    }

    [Fact]
    public void WithoutAHost_TheEnvironmentIsLeftOut()
    {
        var sent = LoggerHarness.SendOne(logger => logger.LogInformation("Hello"));

        Assert.False(sent.TryGetProperty("environment", out _));
    }

    [Fact]
    public void WithoutAnyConfiguration_NothingIsSent_AndTheApplicationRuns()
    {
        using var provider = BuildProvider(null);

        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Test");

        Assert.False(provider.GetRequiredService<LogbookClient>().IsEnabled);
        Assert.False(ProviderLogger(provider, "Test").IsEnabled(LogLevel.Critical));
        logger.LogCritical("Goes nowhere");
    }

    [Theory]
    [InlineData("Logbook:FlushInterval", "not a time")]
    [InlineData("Logbook:BatchSize", "many")]
    public void AValueThatCannotBeRead_DisablesTheClient_InsteadOfStoppingTheStart(string key, string value)
    {
        using var listener = new DisabledListener();
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Logbook:Endpoint"] = "https://logbook.example.com",
            ["Logbook:ApiKey"] = "key",
            [key] = value
        });

        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Test");
        logger.LogCritical("Goes nowhere");

        Assert.False(provider.GetRequiredService<LogbookClient>().IsEnabled);
        Assert.True(listener.WasDisabled);
    }

    [Fact]
    public void ADelegateThatThrows_DisablesTheClient_InsteadOfStoppingTheStart()
    {
        using var provider = BuildProvider(null, _ => throw new InvalidOperationException("Configuration failed"));

        provider.GetRequiredService<ILoggerFactory>().CreateLogger("Test").LogCritical("Goes nowhere");

        Assert.False(provider.GetRequiredService<LogbookClient>().IsEnabled);
    }

    [Fact]
    public void CallingItTwice_AddsOneProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.AddLogbook(options => options.ShutdownTimeout = TimeSpan.Zero);
            logging.AddLogbook();
        });

        using var provider = services.BuildServiceProvider();

        Assert.Single(provider.GetServices<ILoggerProvider>());
    }

    [Fact]
    public void Levels_CanBeSetForTheProviderByItsName()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Logging:Logbook:LogLevel:Default"] = "Warning" })
            .Build();

        var events = LoggerHarness.Send(
            factory =>
            {
                var logger = factory.CreateLogger("Test");
                logger.LogInformation("Filtered");
                logger.LogWarning("Sent");
            },
            configureServices: services => services.AddLogging(logging => logging.AddConfiguration(configuration.GetSection("Logging"))));

        Assert.Equal("Sent", Assert.Single(events).GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("Logbook.Sdk")]
    [InlineData("Logbook.Sdk.LogEventSender")]
    [InlineData("Logbook.Extensions.Logging.LogbookLogger")]
    [InlineData("Logbook.AspNetCore.RequestTracking")]
    [InlineData("System.Net.Http.HttpClient")]
    [InlineData("System.Net.Http.HttpClient.Default.LogicalHandler")]
    [InlineData("System.Net.Http.HttpClient.Logbook.ClientHandler")]
    public void TheCategoriesOfTheFeedbackLoop_AreNeverSent(string category)
    {
        var events = LoggerHarness.Send(factory =>
        {
            var logger = factory.CreateLogger(category);

            Assert.False(FindProviderLogger(factory, category));
            logger.LogCritical("Must not be sent");
        });

        Assert.Empty(events);
    }

    [Theory]
    [InlineData("Logbook.SdkExtras")]
    [InlineData("Logbook.Shop")]
    [InlineData("System.Net.Http.HttpClientFactoryExtras")]
    [InlineData("MyApp.System.Net.Http.HttpClient")]
    public void OtherCategories_WithSimilarNames_AreSent(string category)
    {
        var events = LoggerHarness.Send(factory => factory.CreateLogger(category).LogInformation("Sent"));

        Assert.Equal(category, Assert.Single(events).GetProperty("category").GetString());
    }

    [Fact]
    public void LogLevelNone_IsNotEnabled()
    {
        LoggerHarness.Send(factory => Assert.False(factory.CreateLogger("Test").IsEnabled(LogLevel.None)));
    }

    [Fact]
    public void DisposingTheContainer_SendsWhatWasLoggedWhileShuttingDown_AndALoggerThatOutlivesItDoesNotThrow()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var handler = new RecordingHandler(time);
        var options = new LogbookOptions { Endpoint = new Uri("https://logbook.example.com"), ApiKey = "key", ShutdownTimeout = TimeSpan.FromSeconds(30) };

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton(_ => new LogbookClient(options, handler, time));
        services.AddLogging(logging => logging.AddLogbook());
        services.AddSingleton<LogsWhenDisposed>();

        ILogger logger;

        using (var provider = services.BuildServiceProvider())
        {
            logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Test");
            provider.GetRequiredService<LogsWhenDisposed>();
        }

        Assert.Equal(["Shutting down"], handler.Requests.SelectMany(request => request.Messages));

        logger.LogInformation("After the container is gone");
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?>? settings, Action<LogbookOptions>? configure = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(logging => logging.AddLogbook(configure));

        return services.BuildServiceProvider();
    }

    private static ILogger ProviderLogger(IServiceProvider provider, string category)
    {
        return provider.GetRequiredService<ILoggerProvider>().CreateLogger(category);
    }

    // The factory combines all providers, and the only one here is the provider under test.
    private static bool FindProviderLogger(ILoggerFactory factory, string category) => factory.CreateLogger(category).IsEnabled(LogLevel.Critical);

    private sealed class LogsWhenDisposed(ILoggerFactory factory) : IDisposable
    {
        // Not ILogger<T>: the category would start with the namespace of this test, which the provider excludes.
        private readonly ILogger _logger = factory.CreateLogger("Test");

        public void Dispose() => _logger.LogInformation("Shutting down");
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Test";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class DisabledListener : EventListener
    {
        public bool WasDisabled { get; private set; }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "Logbook-Sdk")
            {
                EnableEvents(eventSource, EventLevel.Error);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventName == "ClientDisabled")
            {
                WasDisabled = true;
            }
        }
    }
}
