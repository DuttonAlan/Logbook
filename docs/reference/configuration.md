# Configuration

Logbook is configured like any ASP.NET Core application: through `appsettings.json`, environment variables or command-line arguments. In environment variables, `__` (two underscores) separates the levels, e.g. `ConnectionStrings__Database`.

## Server

| Setting | Default | Description |
|---|---|---|
| `ConnectionStrings:Database` | – (required) | Npgsql connection string of the PostgreSQL database, e.g. `Host=db;Database=logbook;Username=logbook;Password=…`. Logbook refuses to start without it. |
| `AdminApi:Key` | – (required) | The key that gives full access to the [admin API](admin-api.md). At least 32 characters without whitespace, e.g. from `openssl rand -base64 32`. Logbook refuses to start without it. Replaced by sign-in in milestone M4. |
| `LogIngestion:MaxRequestBytes` | `5242880` (5 MiB) | Maximum size of a request to the [log API](log-api.md), before and after decompression. Must be greater than 0. |
| `LogIngestion:MaxEventBytes` | `262144` (256 KiB) | Maximum size of the message, message template, exception and properties of one Log Event together. Must be greater than 0 and at most `MaxRequestBytes`. |
| `LogIngestion:MaxEventAge` | `7.00:00:00` | How far in the past the timestamp of a Log Event may be. Greater than 0 and at most 3650 days. Each day in the accepted window can become a table in the database. |
| `LogIngestion:MaxFutureOffset` | `1.00:00:00` | How far in the future the timestamp of a Log Event may be, to tolerate wrong clocks. From 0 to 3650 days. |
| `LogIngestion:MaxQueuedBytes` | `268435456` (256 MiB) | Maximum memory for Log Events that are accepted but not written yet. Above it, the log API returns `503`. An empty queue takes any batch, so usage can exceed this by one batch. Must be greater than 0. |
| `LogIngestion:WriteBatchSize` | `1000` | Maximum number of Log Events written to the database at once. Must be greater than 0. |
| `LogIngestion:WriteInterval` | `00:00:01` | How long a Log Event waits for others to be written with it, if there are fewer than `WriteBatchSize`. Must be greater than 0. |
| `RateLimiting:ApiKeyPermitsPerMinute` | `6000` | Requests per minute each API Key can send to the [log API](log-api.md#rate-limit), per server. From 1 to 1,000,000. |
| `RateLimiting:InvalidApiKeyPermitsPerMinute` | `600` | Requests per minute with an API Key that is not valid, for each IP address, per server. Above it, all requests with an API Key from that address get `429`. From 1 to 1,000,000. |
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
