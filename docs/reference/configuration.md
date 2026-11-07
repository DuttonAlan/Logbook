# Configuration

Logbook is configured like any ASP.NET Core application: through `appsettings.json`, environment variables or command-line arguments. In environment variables, `__` (two underscores) separates the levels, e.g. `ConnectionStrings__Database`.

## Server

| Setting | Default | Description |
|---|---|---|
| `ConnectionStrings:Database` | – (required) | Npgsql connection string of the PostgreSQL database, e.g. `Host=db;Database=logbook;Username=logbook;Password=…`. Logbook refuses to start without it. |
| `LogIngestion:MaxRequestBytes` | `5242880` (5 MiB) | Maximum size of a request to the [log API](log-api.md), before and after decompression. Must be greater than 0. |
| `LogIngestion:MaxEventBytes` | `262144` (256 KiB) | Maximum size of the message, message template, exception and properties of one Log Event together. Must be greater than 0 and at most `MaxRequestBytes`. |
| `LogIngestion:MaxEventAge` | `7.00:00:00` | How far in the past the timestamp of a Log Event may be. Greater than 0 and at most 3650 days. Each day in the accepted window can become a table in the database. |
| `LogIngestion:MaxFutureOffset` | `1.00:00:00` | How far in the future the timestamp of a Log Event may be, to tolerate wrong clocks. From 0 to 3650 days. |
| `LogIngestion:MaxQueuedBytes` | `268435456` (256 MiB) | Maximum memory for Log Events that are accepted but not written yet. Above it, the log API returns `503`. An empty queue takes any batch, so usage can exceed this by one batch. Spans have a queue of their own with the same limit, so the most this can take is twice the value. Must be greater than 0. |
| `LogIngestion:WriteBatchSize` | `1000` | Maximum number of Log Events written to the database at once. Must be greater than 0. |
| `LogIngestion:WriteInterval` | `00:00:01` | How long a Log Event waits for others to be written with it, if there are fewer than `WriteBatchSize`. Must be greater than 0. |
| `RateLimiting:ApiKeyPermitsPerMinute` | `6000` | Requests per minute each API Key can send to the [log API](log-api.md#rate-limit), per server. From 1 to 1,000,000. |
| `RateLimiting:InvalidApiKeyPermitsPerMinute` | `600` | Requests per minute with an API Key that is not valid, for each IP address, per server. Above it, all requests with an API Key from that address get `429`. From 1 to 1,000,000. |
| `RateLimiting:InvalidSignInPermitsPerMinute` | `10` | Failed sign-ins per minute, for each IP address, per server. Above it, all sign-ins from that address get `429`. From 1 to 1,000,000. |
| `AllowedHosts` | `*` | Host names the server answers to, separated by `;`. Must be set to your host names in `Production`, see the [production checks](#production-checks). Also applies to health probes. |
| `ASPNETCORE_HTTP_PORTS` | `8080` in the container image | Port for plain HTTP, for a TLS-terminating reverse proxy. |
| `ASPNETCORE_ENVIRONMENT` | `Production` | `Development` enables Swagger UI at `/swagger` and the settings of `appsettings.Development.json`. Never use it outside your computer. |
| `HealthDetails:MonitoringKey` | – | Gives monitoring tools access to [`/health/details`](health-checks.md#details) and nothing else, as `Authorization: Bearer …`. Optional. At least 32 characters without whitespace, e.g. from `openssl rand -base64 32`. |

## Sign-in

At least one way to sign in is required: Logbook refuses to start if nobody could. See [Configure sign-in](../how-to/configure-sign-in.md).

| Setting | Default | Description |
|---|---|---|
| `SignIn:SessionLifetime` | `12:00:00` | How long a session lasts from the moment of signing in. Activity does not extend it. Greater than 0 and at most 30 days. |
| `SignIn:Administrator:UserName` | `admin` | Name of the local administrator. |
| `SignIn:Administrator:Password` | – | Password of the local administrator, at least 12 characters, not starting or ending with whitespace. Without it, the local administrator cannot sign in. Changing it ends the sessions of the local administrator. |
| `SignIn:OpenIdConnect:Authority` | – | Address of the OpenID Connect provider, with HTTPS. Without it, this sign-in is off, and the other settings of this section must be empty. |
| `SignIn:OpenIdConnect:ClientId` | – | Id of Logbook at the provider. Required with `Authority`. |
| `SignIn:OpenIdConnect:ClientSecret` | – | Secret of Logbook at the provider. Leave it out for a public client: the sign-in always uses PKCE. |
| `SignIn:OpenIdConnect:DisplayName` | `OpenID Connect` | Name of the provider on the sign-in page. |
| `SignIn:OpenIdConnect:AdditionalScopes` | – | List of scopes to request in addition to `openid` and `profile`. |
| `SignIn:OpenIdConnect:RequiredClaimType` | – | Type of the claim a user needs to get access, e.g. `groups`. Required with `Authority`. |
| `SignIn:OpenIdConnect:RequiredClaimValue` | – | Value of that claim, e.g. `logbook-admins`, compared exactly. Required with `Authority`. |

## Retention

See [Change the retention](../how-to/manage-applications.md#change-the-retention).

| Setting | Default | Description |
|---|---|---|
| `Retention:DefaultDays` | `30` | Days to keep the Log Events and spans of an Application without a retention of its own. From 1 to 3650. |
| `Retention:Interval` | `01:00:00` | Time between two retention runs. The first run starts with the server. Greater than 0 and at most 30 days. |

A Log Event or span past its retention is gone after the next run, with one exception: those of the Application with the longest retention are removed with whole days, so they can stay up to a day longer.

## Reverse proxy

See [Configure a reverse proxy](../how-to/reverse-proxy.md).

| Setting | Default | Description |
|---|---|---|
| `ReverseProxy:Enabled` | `false` | Read the address of the client and the protocol from `X-Forwarded-For` and `X-Forwarded-Proto`. The original `Host` must be passed on as it is. |
| `ReverseProxy:KnownProxies` | – | List of IP addresses of the proxies to trust. |
| `ReverseProxy:KnownNetworks` | – | List of networks of the proxies to trust, in CIDR notation, e.g. `172.18.0.0/16`. Without these two lists, only a proxy on the same host is trusted. |
| `ReverseProxy:ForwardLimit` | `1` | Number of proxies in a row. At least 1. |

## Production checks

In the environment `Production`, which is the default, Logbook refuses to start and logs the reason if:

- `AllowedHosts` is empty or contains `*`.
- `AllowedHosts` names anything other than `localhost`, `127.0.0.1` or `[::1]`, while `ReverseProxy:Enabled` is off and Logbook does not listen only on HTTPS: passwords and logs would travel in the clear. It counts what `Kestrel:Endpoints` sets, otherwise `ASPNETCORE_URLS`, otherwise `ASPNETCORE_HTTP_PORTS` and `ASPNETCORE_HTTPS_PORTS`. Plain HTTP on `localhost` or `127.0.0.1` is allowed next to HTTPS. The container image sets `ASPNETCORE_HTTP_PORTS=8080`, so with `ASPNETCORE_HTTPS_PORTS` also set that to empty. Where plain HTTP is acceptable, e.g. on a separate network, set `Security:AllowInsecureHttp` to `true`.
- `SignIn:Administrator:Password` has the value from `appsettings.Development.json`.
- The password in `ConnectionStrings:Database` is `12345`, `password` or `postgres`.
- `ReverseProxy:KnownNetworks` contains a network that covers every address, such as `0.0.0.0/0`, as any client could then claim to be anyone.

## Migrator

The image `logbook-migrator` applies the database migrations and exits. It needs only `ConnectionStrings:Database`, with a user that may change the schema.

## SDK

The SDK is configured through `LogbookOptions`. With `builder.Logging.AddLogbook(…)`, they are read from the section `Logbook` in the configuration of the application, for example `appsettings.json` or the environment variable `Logbook__ApiKey`, and then changed by the delegate, if you pass one. The options are read once when the client is created. If `Endpoint` or `ApiKey` is missing or a value cannot be used, the client stays disabled and ignores all events instead of failing the application, and [reports](sdk-behavior.md#diagnostics) the setting.

| Setting | Default | Description |
|---|---|---|
| `Endpoint` | – | Base address of the Logbook server, e.g. `https://logbook.example.com`. An absolute `http` or `https` address, which can include a path. |
| `ApiKey` | – | API Key of the Application that sends the Log Events. |
| `Environment` | name from `IHostEnvironment` | The Environment of the events the `ILogger` provider creates. |
| `BatchSize` | `100` | Maximum number of events sent in one request. |
| `FlushInterval` | `00:00:02` | Maximum time an event waits in the buffer before it is sent. At most one day. |
| `MaxQueueSize` | `10000` | Maximum number of events kept in memory. When the buffer is full, the oldest events are dropped. |
| `MaxQueueBytes` | `67108864` (64 MiB) | Maximum memory of the events in the buffer, counting text at two bytes per character. When the buffer is full, the oldest events are dropped. |
| `MaxRequestBytes` | `5242880` (5 MiB) | Maximum size of the JSON of one request before compression. Must not be larger than the setting `LogIngestion:MaxRequestBytes` of the server. |
| `MaxEventBytes` | `262144` (256 KiB) | Maximum size of `message`, `messageTemplate`, `exception` and `properties` of one event together, in UTF-8. A larger event is [cut](sdk-behavior.md#events-over-a-limit). Must not be larger than the setting `LogIngestion:MaxEventBytes` of the server. |
| `MaxMessageLength` | `65536` | Maximum number of characters of `message` and of `messageTemplate`. Longer text is cut. Must not be larger than the limit of the server, 65,536 characters. |
| `MaxEventAge` | `7.00:00:00` | Maximum age of an event when it is sent. An older event is dropped. Must not be longer than the setting `LogIngestion:MaxEventAge` of the server. |
| `MaxFutureOffset` | `1.00:00:00` | How far in the future the timestamp of an event may be. An event beyond that is dropped. Must not be longer than the setting `LogIngestion:MaxFutureOffset` of the server. |
| `RequestTimeout` | `00:00:30` | Maximum time to wait for the server to answer one request. At most one day. |
| `ShutdownTimeout` | `00:00:05` | Maximum time disposing the client waits for the remaining events to be sent. `00:00:00` does not wait. At most one day. |

Which levels are sent is set like for any other logging provider, under the name `Logbook`, for example `Logging:Logbook:LogLevel:Default`.

How the client uses these settings when something goes wrong is described in [SDK behavior](sdk-behavior.md).

### Tracking

With `builder.Services.AddLogbookTelemetry(…)` from `Logbook.AspNetCore`, these are read from the section `Logbook:Telemetry`, and then changed by the delegate, if you pass one. See [Track requests and dependencies](../how-to/track-requests.md).

| Setting | Default | Description |
|---|---|---|
| `TrackRequests` | `true` | Track the requests the application receives. |
| `TrackHttpDependencies` | `true` | Track the requests the application makes with `HttpClient`. |
| `TrackSqlDependencies` | `false` | Track the commands the application sends to a database, with a driver that publishes an `ActivitySource`. |
| `CaptureSqlText` | `false` | Send the text of SQL statements, which can contain values if a statement is not parameterized. |
| `SqlActivitySources` | – | Names of further `ActivitySource`s of database drivers. `Npgsql` and `MySqlConnector` are always included. |
| `ExcludedRequestPaths` | – | Paths of requests not to track, each including what is below it, e.g. `/health`. |
