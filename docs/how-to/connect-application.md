# Connect an application

This guide sends the logs of a .NET application to Logbook. Existing code that logs through `ILogger` needs no changes: you add one package, one line and two settings.

> The packages are not on NuGet yet, as they will be [published with version 1.0](../roadmap.md). Until then, reference the project from a checkout of this repository, as shown below.

## Prerequisites

- A running Logbook, e.g. from [Getting started](../tutorials/getting-started.md).
- An API Key of an Application. To create one, see [Manage Applications and API Keys](manage-applications.md). Give each application its own Application, so that you can tell their logs apart and revoke one key without touching the others.
- An application on .NET 10 or later. The examples here use a new one:

```sh
dotnet new web -o MyApp
cd MyApp
```

## 1. Add the package

From a checkout of this repository next to your application:

```sh
dotnet add reference ../Logbook/sdk/Logbook.Extensions.Logging/Logbook.Extensions.Logging.csproj
```

## 2. Add Logbook to the logging

In `Program.cs`, right after the builder is created:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddLogbook();
```

Everything else logged through `ILogger` now goes to Logbook as well, including what ASP.NET Core logs itself. To have something to look at, replace the default endpoint with one that logs:

```csharp
app.MapGet("/", (ILogger<Program> logger) =>
{
    logger.LogInformation("Order {OrderId} was placed", 42);

    return "Hello";
});
```

## 3. Configure the address and the API Key

Add the address of the server to `appsettings.json`:

```json
{
  "Logbook": {
    "Endpoint": "http://localhost:5080"
  }
}
```

The API Key is a secret, so keep it out of your files. Set it in the environment instead, here for the current shell:

```sh
export Logbook__ApiKey=…
```

Without an endpoint and an API Key, the application runs normally and sends nothing. All other settings have defaults, and are listed in [Configuration](../reference/configuration.md#sdk).

## 4. Check that it works

Run the application and call it:

```sh
dotnet run --urls http://localhost:5199
curl http://localhost:5199/
```

Log Events are sent at the latest after 2 seconds. Find this one in the [web interface](explore-logs.md), or [sign in with a script](configure-sign-in.md#scripts) and search for it:

```sh
curl --cookie cookies.txt -G "$LOGBOOK_URL/api/v1/logs" \
  --data-urlencode "text=placed"
```

The response contains the rendered message with its template, the category, the Environment from `ASPNETCORE_ENVIRONMENT`, the `traceId` and `spanId` of the request, and `OrderId` as a property. [SDK behavior](../reference/sdk-behavior.md#from-ilogger-to-an-event) lists how each field is filled.

## Choose what is sent

Which levels are sent is set like for any logging provider. For Logbook alone, use the name `Logbook`:

```json
{
  "Logging": {
    "Logbook": {
      "LogLevel": {
        "Default": "Information",
        "Microsoft.AspNetCore": "Warning"
      }
    }
  }
}
```

The Environment comes from the host. To set a different one, set `Logbook:Environment`, or use the delegate, which is applied after the section `Logbook`:

```csharp
builder.Logging.AddLogbook(options => options.Environment = "Staging");
```

## Without a web host

In a console application without a host, create the logger factory yourself. Dispose it before the program ends, because that is what sends the remaining Log Events:

```csharp
using var factory = LoggerFactory.Create(logging => logging.AddLogbook(options =>
{
    options.Endpoint = new Uri("http://localhost:5080");
    options.ApiKey = Environment.GetEnvironmentVariable("Logbook__ApiKey");
}));

factory.CreateLogger("Import").LogInformation("Imported {Count} rows", 120);
```

A host does this for you when it stops, waiting for at most `ShutdownTimeout`, 5 seconds by default. Give the application enough time to stop, see [Shutdown](../reference/sdk-behavior.md#shutdown).

## If nothing arrives

The SDK never throws into your application, so a problem does not show up in it. It reports through an `EventSource` named `Logbook-Sdk` instead. To see this, use [`dotnet-trace`](https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-trace) while the application runs, and open the file it writes:

```sh
dotnet tool install --global dotnet-trace
dotnet-trace collect --providers Logbook-Sdk --name MyApp
```

| Event | Likely cause |
|---|---|
| `ClientDisabled` with `Endpoint` or `ApiKey` | The setting is missing or not valid. `options` means a value in the section `Logbook` could not be read. |
| `RequestRefused` with `401` | The API Key is wrong, revoked or expired. Log Events wait in the buffer, and are tried again about once a minute. |
| `RequestRefused` with `404` or a redirect | The address is wrong, e.g. `http` where the server redirects to `https`. |
| `RequestFailed` with `HttpRequestException` | The server cannot be reached. |
| `EventsDropped` | See the reason in [Diagnostics](../reference/sdk-behavior.md#diagnostics). |

If there are no such events either, check the log level, and that the category of your logger does not start with `Logbook.Sdk`, `Logbook.Extensions.Logging`, `Logbook.AspNetCore` or `System.Net.Http.HttpClient`, which are [never sent](../reference/sdk-behavior.md#from-ilogger-to-an-event).
