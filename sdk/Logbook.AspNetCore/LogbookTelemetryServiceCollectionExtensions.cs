// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.AspNetCore;
using Logbook.Sdk;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Adds the tracking of requests and dependencies to an ASP.NET Core application.
/// </summary>
public static class LogbookTelemetryServiceCollectionExtensions
{
    /// <summary>
    ///     Sends the requests the application receives and the calls it makes to Logbook, along with everything logged through
    ///     <c>ILogger</c>. It uses the client from the section <c>Logbook</c>, and reads what to track from the section
    ///     <c>Logbook:Telemetry</c>.
    /// </summary>
    /// <remarks>
    ///     This never takes the application down: if the settings cannot be used, nothing is tracked or sent, and the
    ///     <c>EventSource</c> named <c>Logbook-Sdk</c> reports why. It includes <c>builder.Logging.AddLogbook()</c>, so
    ///     calling that as well is only needed to configure the client with a delegate.
    /// </remarks>
    /// <param name="services">The services of the application.</param>
    /// <param name="configure">Changes the options after they were read from the section.</param>
    /// <returns>The services, for chaining.</returns>
    public static IServiceCollection AddLogbookTelemetry(this IServiceCollection services, Action<LogbookTelemetryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLogging(logging => logging.AddLogbook());

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<LogbookTelemetryOptions>, ConfigureFromSection>());

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton(CreateRecorder);
        services.TryAddSingleton<DependencyTrackingService>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, RequestTrackingStartupFilter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DependencyTrackingService>(provider => provider.GetRequiredService<DependencyTrackingService>()));

        return services;
    }

    private static SpanRecorder CreateRecorder(IServiceProvider services)
    {
        var environment = Read<LogbookOptions>(services)?.Environment ?? services.GetService<IHostEnvironment>()?.EnvironmentName;

        return new SpanRecorder(
            services.GetRequiredService<LogbookClient>(),
            Read<LogbookTelemetryOptions>(services) ?? new LogbookTelemetryOptions(),
            environment,
            services.GetService<TimeProvider>() ?? TimeProvider.System);
    }

    // A value in the section that cannot be converted throws here, and with it the start of the application.
    private static T? Read<T>(IServiceProvider services)
        where T : class
    {
        try
        {
            return services.GetRequiredService<IOptions<T>>().Value;
        }
#pragma warning disable CA1031 // The SDK must never throw into the host application.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    private sealed class ConfigureFromSection(IServiceProvider services) : IConfigureOptions<LogbookTelemetryOptions>
    {
        public void Configure(LogbookTelemetryOptions options)
        {
            services.GetService<IConfiguration>()?.GetSection(LogbookTelemetryOptions.SectionName).Bind(options);
        }
    }
}
