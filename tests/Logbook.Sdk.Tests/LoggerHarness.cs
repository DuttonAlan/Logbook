// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Logbook.Sdk.Tests;

/// <summary>
///     Runs calls to <see cref="ILogger"/> through the real logging pipeline, the provider and the client, and returns what
///     the server would receive. Disposing the container sends everything, so no test has to wait for anything.
/// </summary>
internal static class LoggerHarness
{
    /// <summary>
    ///     Logs and returns the events that were sent.
    /// </summary>
    /// <param name="log">The calls to make.</param>
    /// <param name="configureOptions">Changes the options.</param>
    /// <param name="configureServices">Adds to the services.</param>
    /// <returns>The events.</returns>
    public static JsonElement[] Send(
        Action<ILoggerFactory> log,
        Action<LogbookOptions>? configureOptions = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var handler = new RecordingHandler(time);

        var options = new LogbookOptions
        {
            Endpoint = new Uri("https://logbook.example.com"),
            ApiKey = "key",
            ShutdownTimeout = TimeSpan.FromSeconds(30)
        };
        configureOptions?.Invoke(options);

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton(_ => new LogbookClient(options, handler, time));
        configureServices?.Invoke(services);
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddLogbook(configureOptions);
        });

        using (var provider = services.BuildServiceProvider())
        {
            log(provider.GetRequiredService<ILoggerFactory>());
        }

        return [.. handler.Requests.SelectMany(request =>
        {
            using var document = JsonDocument.Parse(request.Body);

            return document.RootElement.EnumerateArray().Select(element => element.Clone()).ToArray();
        })];
    }

    /// <summary>
    ///     Logs with a logger of the category <c>Test</c> and returns the only event that was sent.
    /// </summary>
    /// <param name="log">The call to make.</param>
    /// <param name="configureOptions">Changes the options.</param>
    /// <param name="configureServices">Adds to the services.</param>
    /// <returns>The event.</returns>
    public static JsonElement SendOne(
        Action<ILogger> log,
        Action<LogbookOptions>? configureOptions = null,
        Action<IServiceCollection>? configureServices = null)
    {
        return Assert.Single(Send(factory => log(factory.CreateLogger("Test")), configureOptions, configureServices));
    }
}
