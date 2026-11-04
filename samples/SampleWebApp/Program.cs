// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

// A small web application to try the Logbook SDK against a local Logbook server.
var builder = WebApplication.CreateBuilder(args);

// Milestone M2: builder.Logging.AddLogbook(options => { options.Endpoint = new Uri("http://localhost:5080"); options.ApiKey = "…"; });

var app = builder.Build();

app.MapGet("/", (ILogger<Program> logger) =>
{
    logger.LogInformation("Hello from {Application} at {Time}", "SampleWebApp", DateTimeOffset.UtcNow);

    return "SampleWebApp is running. Open /error to log an exception.";
});

app.MapGet("/error", () =>
{
    throw new InvalidOperationException("A sample failure for Logbook.");
});

await app.RunAsync();
