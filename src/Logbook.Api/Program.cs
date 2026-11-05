// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Text.Json;
using System.Text.Json.Serialization;

using Logbook.Api.Authentication;
using Logbook.Api.Authorization;
using Logbook.Api.ExceptionHandling;
using Logbook.Api.HealthChecks;
using Logbook.Api.LogEvents;
using Logbook.Api.OpenApi;
using Logbook.Api.RateLimiting;
using Logbook.Core;
using Logbook.Core.Application.LogEvents;
using Logbook.Infrastructure;
using Logbook.Infrastructure.HealthChecks;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

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
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        Description = "The admin key from the setting `AdminApi:Key`."
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

builder.Services.AddOptions<AdminApiOptions>()
    .Bind(builder.Configuration.GetSection(AdminApiOptions.SectionName))
    .ValidateOnStart();
builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AdminApiOptions>, AdminApiOptionsValidator>());

// The admin API is protected by a single configured key until sign-in exists (milestone M4).
builder.Services.AddAuthentication(AdminKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, AdminKeyAuthenticationHandler>(AdminKeyAuthenticationHandler.SchemeName, configureOptions: null)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, configureOptions: null);
builder.Services.AddAuthorization(options => options.AddLogbookPolicies());

builder.Services.AddOptions<LogIngestionOptions>()
    .Bind(builder.Configuration.GetSection(LogIngestionOptions.SectionName))
    .ValidateOnStart();
builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<LogIngestionOptions>, LogIngestionOptionsValidator>());
builder.Services.AddSingleton<LogRequestBodyFilter>();

builder.Services.AddOptions<RateLimitOptions>()
    .Bind(builder.Configuration.GetSection(RateLimitOptions.SectionName))
    .ValidateOnStart();
builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<RateLimitOptions>, RateLimitOptionsValidator>());
builder.Services.AddRateLimiter(options => options.AddLogbookPolicies());

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();

// The web interface (src/Logbook.Web) is built into wwwroot when the container image is built.
app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.UseSwagger();
    app.UseSwaggerUI();
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

// Unknown API routes are a 404, not the web interface.
app.Map("/api/{**path}", () => Results.NotFound());

// Every other route belongs to the web interface, which routes in the browser.
app.MapFallbackToFile("index.html");

await app.RunAsync();
