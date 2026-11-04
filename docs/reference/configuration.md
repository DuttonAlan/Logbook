# Configuration

Logbook is configured like any ASP.NET Core application: through `appsettings.json`, environment variables or command-line arguments. In environment variables, `__` (two underscores) separates the levels, e.g. `ConnectionStrings__Database`.

## Server

| Setting | Default | Description |
|---|---|---|
| `ConnectionStrings:Database` | – (required) | Npgsql connection string of the PostgreSQL database, e.g. `Host=db;Database=logbook;Username=logbook;Password=…`. Logbook refuses to start without it. |
| `AdminApi:Key` | – (required) | The key that gives full access to the [admin API](admin-api.md). At least 32 characters without whitespace, e.g. from `openssl rand -base64 32`. Logbook refuses to start without it. Replaced by sign-in in milestone M4. |
| `AllowedHosts` | `*` | Host names the server answers to, separated by `;`. Set it to your domain in production. |
| `ASPNETCORE_HTTP_PORTS` | `8080` in the container image | Port for plain HTTP, for a TLS-terminating reverse proxy. |
| `ASPNETCORE_ENVIRONMENT` | `Production` | `Development` enables Swagger UI at `/swagger` and the settings of `appsettings.Development.json`. Never use it outside your computer. |

## Migrator

The image `logbook-migrator` applies the database migrations and exits. It needs only `ConnectionStrings:Database`, with a user that may change the schema. It does not need `AdminApi:Key`.

## SDK

The SDK is configured through `LogbookOptions`, or the section `Logbook` in `appsettings.json` once the `ILogger` provider exists (milestone M2).

| Setting | Default | Description |
|---|---|---|
| `Endpoint` | – | Base address of the Logbook server, e.g. `https://logbook.example.com`. |
| `ApiKey` | – | API Key of the Application that sends the Log Events. |
| `BatchSize` | `100` | Maximum number of events sent in one request. |
| `FlushInterval` | `00:00:02` | Maximum time an event waits in the buffer before it is sent. |
| `MaxQueueSize` | `10000` | Maximum number of events kept in memory. When the buffer is full, the oldest events are dropped. |
