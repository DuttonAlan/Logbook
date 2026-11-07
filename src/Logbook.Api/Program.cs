// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

using Logbook.Api.Authentication;
using Logbook.Api.Authorization;
using Logbook.Api.ExceptionHandling;
using Logbook.Api.HealthChecks;
using Logbook.Api.LogEvents;
using Logbook.Api.OpenApi;
using Logbook.Api.RateLimiting;
using Logbook.Api.Security;
using Logbook.Core;
using Logbook.Core.Application.LogEvents;
using Logbook.Infrastructure;
using Logbook.Infrastructure.HealthChecks;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

// In a container, the server usually is process 1, which the abort after an unhandled exception does not end. It would
// keep running without doing anything, so that refusing to start would look like a server that hangs.
AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
{
    Console.Error.WriteLine($"Unhandled exception. {eventArgs.ExceptionObject}");
    Environment.Exit(1);
};

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(options =>
{
    // Include the XML comments of the Api and of Core, which contains the request and response types.
    foreach (var xmlFile in Directory.EnumerateFiles(AppContext.BaseDirectory, "Logbook.*.xml"))
    {
        options.IncludeXmlComments(xmlFile);
    }

    options.SupportNonNullableReferenceTypes();
    options.NonNullableReferenceTypesAsRequired();

    options.AddSecurityDefinition(AuthorizeOperationFilter.SecuritySchemeName, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Cookie,
        Name = SessionAuthentication.CookieName,
        Description = "The session cookie from signing in. Requests that change something also need the header `X-Requested-With`."
    });
    options.AddSecurityDefinition(AuthorizeOperationFilter.ApiKeySecuritySchemeName, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = ApiKeyAuthenticationHandler.HeaderName,
        Description = "The API Key of an Application."
    });
    options.OperationFilter<AuthorizeOperationFilter>();
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.AddControllers(options =>
        // Free-form JSON of a Log Event has nothing to validate, and walking through it would only cost time.
        options.ModelMetadataDetailsProviders.Add(new SuppressChildValidationMetadataProvider(typeof(JsonElement))))
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOptions<SignInOptions>()
    .Bind(builder.Configuration.GetSection(SignInOptions.SectionName))
    .ValidateOnStart();
builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SignInOptions>, SignInOptionsValidator>());

builder.Services.AddOptions<MonitoringKeyOptions>()
    .Bind(builder.Configuration.GetSection(MonitoringKeyOptions.SectionName))
    .ValidateOnStart();
builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MonitoringKeyOptions>, MonitoringKeyOptionsValidator>());

builder.Services.AddOptions<SecurityOptions>()
    .Bind(builder.Configuration.GetSection(SecurityOptions.SectionName))
    .ValidateOnStart();
builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SecurityOptions>, ProductionSettingsValidator>());

builder.Services.AddOptions<ReverseProxyOptions>()
    .Bind(builder.Configuration.GetSection(ReverseProxyOptions.SectionName))
    .ValidateOnStart();
builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ReverseProxyOptions>, ReverseProxyOptionsValidator>());
builder.Services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<ReverseProxyOptions>>((options, reverseProxy) =>
{
    // Not X-Forwarded-Host: the proxy passes on the host, which AllowedHosts checks.
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = reverseProxy.Value.ForwardLimit;

    // Without any of these, the default stays: only a proxy on the same host.
    if (reverseProxy.Value.KnownProxies.Count > 0 || reverseProxy.Value.KnownNetworks.Count > 0)
    {
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var proxy in reverseProxy.Value.KnownProxies)
        {
            options.KnownProxies.Add(IPAddress.Parse(proxy));
        }

        foreach (var network in reverseProxy.Value.KnownNetworks)
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }
    }
});

builder.Services.AddLogbookSession(builder.Configuration, builder.Environment)
    .AddScheme<AuthenticationSchemeOptions, MonitoringKeyAuthenticationHandler>(MonitoringKeyAuthenticationHandler.SchemeName, configureOptions: null)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, configureOptions: null);
builder.Services.AddAuthorization(options => options.AddLogbookPolicies());

builder.Services.AddOptions<LogIngestionOptions>()
    .Bind(builder.Configuration.GetSection(LogIngestionOptions.SectionName))
    .ValidateOnStart();
builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<LogIngestionOptions>, LogIngestionOptionsValidator>());
builder.Services.AddSingleton<LogRequestBodyFilter>();

builder.Services.AddOptions<RetentionOptions>()
    .Bind(builder.Configuration.GetSection(RetentionOptions.SectionName))
    .ValidateOnStart();
builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<RetentionOptions>, RetentionOptionsValidator>());

builder.Services.AddOptions<RateLimitOptions>()
    .Bind(builder.Configuration.GetSection(RateLimitOptions.SectionName))
    .ValidateOnStart();
builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<RateLimitOptions>, RateLimitOptionsValidator>());
builder.Services.AddRateLimiter(options => options.AddLogbookPolicies());
builder.Services.AddSingleton<InvalidApiKeyLimiter>();
builder.Services.AddSingleton<InvalidSignInLimiter>();
builder.Services.AddSingleton<RateLimitReporter>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// First, so that everything after it sees the address of the client and the protocol it used.
if (app.Services.GetRequiredService<IOptions<ReverseProxyOptions>>().Value.Enabled)
{
    app.UseForwardedHeaders();
}

app.UseSecurityHeaders();

app.UseExceptionHandler();

if (!app.Environment.IsDevelopment())
{
    // Only takes effect on HTTPS requests, and not for localhost.
    app.UseHsts();
}

// The web interface (src/Logbook.Web) is built into wwwroot when the container image is built.
app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        // Requests of a session that change something need this header.
        options.UseRequestInterceptor($"(request) => {{ request.headers['{AuthorizationPolicies.RequestedWithHeader}'] = 'swagger'; return request; }}");
    });
}

app.UseAuthentication();
app.UseAuthorization();

// After authorization, because the limits are per API Key, which the authorization policy authenticates.
app.UseRateLimiter();

app.MapControllers();

// Public, for load balancers and orchestrators: only the overall status (200 or 503).
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains(HealthCheckTags.Ready),
    ResponseWriter = HealthReportWriter.WriteStatusAsync
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthReportWriter.WriteStatusAsync
});

// For monitoring: all checks with their data, and the version.
app.MapHealthChecks("/health/details", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains(HealthCheckTags.Ready),
    ResponseWriter = HealthReportWriter.WriteDetailsAsync
}).RequireAuthorization(AuthorizationPolicies.HealthDetails);

// Unknown API routes are a 404, not the web interface.
app.Map("/api/{**path}", () => Results.NotFound());

// Every other route belongs to the web interface, which routes in the browser.
app.MapFallbackToFile("index.html");

await app.RunAsync();
