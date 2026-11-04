// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Text.Json.Serialization;

using Logbook.Api.ExceptionHandling;
using Logbook.Api.HealthChecks;
using Logbook.Core;
using Logbook.Infrastructure;
using Logbook.Infrastructure.HealthChecks;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;

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
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

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
