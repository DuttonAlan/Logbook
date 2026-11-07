# Track requests and dependencies

This guide makes an ASP.NET Core application send what it does to Logbook, besides its logs: each request it receives and each call it makes. You then see the requests, the failures grouped by exception, and one timeline per transaction.

## Prerequisites

- An application that is [connected](connect-application.md) with an endpoint and an API Key.
- ASP.NET Core on .NET 10 or later.

## 1. Add the package

From a checkout of this repository next to your application:

```sh
dotnet add reference ../Logbook/sdk/Logbook.AspNetCore/Logbook.AspNetCore.csproj
```

## 2. Add the tracking

In `Program.cs`, right after the builder is created:

```csharp
builder.Services.AddLogbookTelemetry();
```

This includes `builder.Logging.AddLogbook()`, and uses the same settings in the section `Logbook`. Your other code needs no changes.

## 3. Check that it works

Run the application, call it, and open **Requests** in the web interface. Requests appear within a few seconds. Selecting one opens its **transaction**.

| Field | For a request | For an `HttpClient` call |
|---|---|---|
| Name | `GET /orders/{id}`, or `GET (no route)` if no route matched | `GET payments.example.com` |
| Status | The status of the response | The status of the response, if there was one |
| Failed if | The status is 500 or above, or an exception left the pipeline | The status is 400 or above, or the call threw |
| Details | The path, without the query string | The URL, without the query string |

A request that ends because the client went away is stored with the status `499`, and does not count as failed.

## Read the transactions

**Transactions** show one trace as a timeline: the request, the dependencies it called, and the Log Events written meanwhile, each under what caused it. W3C trace headers carry the trace on through `HttpClient`, so if the called application also sends to Logbook, its requests appear in the same transaction, whichever Application it is.

To get there from a Log Event, select it in the log explorer and choose **Open the transaction**.

## Find what fails most

**Exceptions** lists the exceptions in your Log Events, grouped by [type and stack](../reference/telemetry-api.md#what-makes-two-exceptions-the-same), with their count and first and last occurrence. Sort by *Most frequent first* to see what matters most, or look at the first occurrence to tell whether a failure is new. **Show all occurrences** opens the Log Events of a group.

This needs no tracking at all, as it works from what is logged with an exception. ASP.NET Core logs unhandled exceptions as errors by itself, so keep the level for `Microsoft.AspNetCore` at `Error` or below.

## Choose what is tracked

All of these are optional, in the section `Logbook:Telemetry` or in the delegate, which is applied afterwards. They are listed in [Configuration](../reference/configuration.md#tracking).

To leave out what is called often and says little, such as the probes of a load balancer:

```json
{
  "Logbook": {
    "Telemetry": {
      "ExcludedRequestPaths": [ "/health" ]
    }
  }
}
```

### Track SQL

SQL commands are not tracked by default, as one request can run many of them:

```csharp
builder.Services.AddLogbookTelemetry(options =>
{
    options.TrackSqlDependencies = true;
    options.CaptureSqlText = true;
});
```

The statements themselves are only sent with `CaptureSqlText`, because a statement without parameters can contain values.

This works with database drivers that publish an `ActivitySource`: **Npgsql** and **MySqlConnector** are built in, and `SqlActivitySources` adds the name of another one. It also works through Entity Framework Core on these drivers. **Microsoft.Data.SqlClient is not supported yet**, as it does not publish its commands this way, so nothing is tracked for SQL Server.

## If nothing arrives

Spans travel like Log Events, so [the same checks](connect-application.md#if-nothing-arrives) apply. Dependencies are tracked per process: if a process runs several hosts, each tracks the calls of all of them. See also [SDK behavior](../reference/sdk-behavior.md#requests-and-dependencies).
