// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Extensions.Logging;
using Logbook.Sdk;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Logging;

/// <summary>
///     Adds Logbook to the logging of an application.
/// </summary>
public static class LogbookLoggingBuilderExtensions
{
    /// <summary>
    ///     Sends everything logged through <see cref="ILogger"/> to Logbook, configured from the section <c>Logbook</c>.
    /// </summary>
    /// <remarks>
    ///     This never takes the application down: if the settings cannot be used, nothing is sent, and the
    ///     <c>EventSource</c> named <c>Logbook-Sdk</c> reports why. Which levels are sent can be set like for any other
    ///     provider, under the name <c>Logbook</c>, e.g. <c>Logging:Logbook:LogLevel:Default</c>.
    /// </remarks>
    /// <param name="builder">The logging builder.</param>
    /// <param name="configure">Changes the options after they were read from the section.</param>
    /// <returns>The logging builder.</returns>
    public static ILoggingBuilder AddLogbook(this ILoggingBuilder builder, Action<LogbookOptions>? configure = null)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.Services.AddOptions();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<LogbookOptions>, ConfigureFromSection>());

        if (configure is not null)
        {
            builder.Services.Configure(configure);
        }

        // In the container, the client is created before the provider, so it is disposed after it. That way, it sends what
        // was logged while everything else was shutting down.
        builder.Services.TryAddSingleton(CreateClient);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, LogbookLoggerProvider>(CreateProvider));

        return builder;
    }

    private static LogbookClient CreateClient(IServiceProvider services)
    {
        return new LogbookClient(ReadOptions(services)!);
    }

    private static LogbookLoggerProvider CreateProvider(IServiceProvider services)
    {
        var options = ReadOptions(services);
        var client = services.GetRequiredService<LogbookClient>();
        var environment = options?.Environment ?? services.GetService<IHostEnvironment>()?.EnvironmentName;

        return new LogbookLoggerProvider(
            client,
            environment,
            options?.MaxMessageLength ?? 0,
            options?.MaxEventBytes ?? 0,
            services.GetService<TimeProvider>() ?? TimeProvider.System);
    }

    // A value in the section that cannot be converted throws here, and with it the start of the application. Null makes
    // the client disabled, and report it.
    private static LogbookOptions? ReadOptions(IServiceProvider services)
    {
        try
        {
            return services.GetRequiredService<IOptions<LogbookOptions>>().Value;
        }
#pragma warning disable CA1031 // The SDK must never throw into the host application.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    private sealed class ConfigureFromSection(IServiceProvider services) : IConfigureOptions<LogbookOptions>
    {
        public void Configure(LogbookOptions options)
        {
            services.GetService<IConfiguration>()?.GetSection(LogbookOptions.SectionName).Bind(options);
        }
    }
}
