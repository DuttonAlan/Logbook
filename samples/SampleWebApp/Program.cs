// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

// A small web application to try the Logbook SDK against a local Logbook server.
var builder = WebApplication.CreateBuilder(args);

// Reads the section "Logbook". Without an API Key, the sample runs but sends nothing. Set one for this run with the
// environment variable Logbook__ApiKey, so that it does not end up in a file.
builder.Logging.AddLogbook();

// Also tracks the requests this application receives and the calls it makes.
builder.Services.AddLogbookTelemetry();
builder.Services.AddHttpClient();

var app = builder.Build();

app.MapGet("/", (ILogger<Program> logger) =>
{
    using (logger.BeginScope(new Dictionary<string, object?> { ["Visitor"] = Guid.NewGuid() }))
    {
        logger.LogInformation("Hello from {Application} at {Time}", "SampleWebApp", DateTimeOffset.UtcNow);
    }

    return "SampleWebApp is running. Open /error to log an exception, or /call to make a request to a dependency.";
});

app.MapGet("/error", () =>
{
    throw new InvalidOperationException("A sample failure for Logbook.");
});

// Calls this application itself, so one transaction shows two requests connected by a dependency.
app.MapGet("/call", async (HttpContext context, IHttpClientFactory clients, ILogger<Program> logger) =>
{
    using var client = clients.CreateClient();

    using var response = await client.GetAsync(new Uri($"{context.Request.Scheme}://{context.Request.Host}/"), context.RequestAborted);

    logger.LogInformation("The call answered with {StatusCode}", (int)response.StatusCode);

    return Results.Text($"The call answered with {(int)response.StatusCode}.");
});

await app.RunAsync();
