// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

// A small web application to try the Logbook SDK against a local Logbook server.
var builder = WebApplication.CreateBuilder(args);

// Reads the section "Logbook". Without an API Key, the sample runs but sends nothing. Set one for this run with the
// environment variable Logbook__ApiKey, so that it does not end up in a file.
builder.Logging.AddLogbook();

var app = builder.Build();

app.MapGet("/", (ILogger<Program> logger) =>
{
    using (logger.BeginScope(new Dictionary<string, object?> { ["Visitor"] = Guid.NewGuid() }))
    {
        logger.LogInformation("Hello from {Application} at {Time}", "SampleWebApp", DateTimeOffset.UtcNow);
    }

    return "SampleWebApp is running. Open /error to log an exception.";
});

app.MapGet("/error", () =>
{
    throw new InvalidOperationException("A sample failure for Logbook.");
});

await app.RunAsync();
