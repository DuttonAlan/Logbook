# Changelog

All notable changes to Logbook are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and Logbook uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Before 1.0, minor versions may contain breaking changes; they are listed under *Changed* or *Removed*.

## [Unreleased]

### Added

- Project foundations: server (`Logbook.Api`, `Logbook.Core`, `Logbook.Infrastructure`) on .NET 10 and PostgreSQL, web interface with React, SDK packages `Logbook.Sdk`, `Logbook.Extensions.Logging` and `Logbook.AspNetCore` for .NET 10 and later.
- Health endpoints `/health` and `/health/live`.
- Container images for the server and the database migrations, and a Docker Compose setup for local evaluation.
- Documentation following Diátaxis, and the roadmap.
- Applications and API Keys: admin API under `/api/v1/applications` to create, list, rename and delete Applications and to issue and revoke API Keys. API Keys are shown once when issued and stored only as SHA-256 hashes.
- Log API: `POST /api/v1/logs` receives batches of Log Events as JSON, optionally compressed with gzip, authenticated with the API Key of an Application in the header `X-Logbook-Key`. Log Events are stored in the schema `telemetry`, partitioned by day. A Log Event without an Environment is stored as `Unknown`.
- Settings `LogIngestion:MaxRequestBytes`, `LogIngestion:MaxEventBytes`, `LogIngestion:MaxEventAge` and `LogIngestion:MaxFutureOffset` for the limits of the log API.
- Ingestion queue: accepted Log Events are written to the database in batches by a background service, and what is left is written on shutdown. `POST /api/v1/logs` returns `503` with `Retry-After` when the queue is full. Settings `LogIngestion:MaxQueuedBytes`, `LogIngestion:WriteBatchSize` and `LogIngestion:WriteInterval`.
- Search: `GET /api/v1/logs` finds Log Events by time range, level, Application, Environment and full-text search on the message, newest first with cursor paging. It requires a session.
- Rate limit per API Key on `POST /api/v1/logs`: `429` with `Retry-After` above `RateLimiting:ApiKeyPermitsPerMinute`, by default 6000 requests per minute. Requests with an invalid API Key are limited per IP address by `RateLimiting:InvalidApiKeyPermitsPerMinute`, by default 600 per minute. API Keys and IP addresses over a limit are logged as warnings, at most once per minute.
- SDK client: `LogbookClient` in `Logbook.Sdk` buffers Log Events in memory and sends them in the background, compressed with gzip, in batches by number, size and time. When the buffer is full, the oldest events are dropped. New options `MaxQueueBytes` and `MaxRequestBytes`.
- SDK failure handling: exponential backoff with `Retry-After` after `408`, `429`, `5xx` and connection failures, a backoff of about a minute after `401` and other refusals, and no retry after `400`, `413`, `415` and `422`. See [SDK behavior](docs/reference/sdk-behavior.md).
- SDK size guards: events over a limit of the server have their long text cut and get the property `logbook.truncated`, and are dropped only if still too large. Events outside of the accepted time window are dropped. New options `MaxEventBytes`, `MaxMessageLength`, `MaxEventAge`, `MaxFutureOffset` and `RequestTimeout`.
- SDK shutdown: disposing the client sends what is left, for at most the new option `ShutdownTimeout`.
- SDK diagnostics through an `EventSource` named `Logbook-Sdk`.
- `ILogger` provider: `builder.Logging.AddLogbook(…)` in `Logbook.Extensions.Logging` reads the section `Logbook` and sends message templates, scopes, `EventId`, inner and aggregate exceptions, and `traceId` and `spanId` from `Activity.Current`. The values of one event are limited as a whole, so that a call to the logger stays cheap. The Environment comes from `IHostEnvironment`, unless the new option `Environment` overrides it. Its own categories and `System.Net.Http.HttpClient` are excluded, so sending cannot create more to send. See [SDK behavior](docs/reference/sdk-behavior.md).
- How-to guide: [connect an application](docs/how-to/connect-application.md).
- Web interface: the log explorer lists Log Events with infinite scrolling and level colors, filters by time range, level, Application, Environment, text and properties, and shows the details of a Log Event with its exception, stack trace and properties. It also manages Applications and API Keys. See [Search and follow logs](docs/how-to/explore-logs.md).
- Live tail: `GET /api/v1/logs/stream` sends new Log Events as server-sent events, with the same filters as the search. The web interface follows them, and can pause and catch up.
- Search filters `traceId` and `property` on `GET /api/v1/logs`, and "All Log Events of this trace" in the web interface. The new index on the trace requires running the migrations, which can take a while on a large table.

- Sign-in to the web interface and the admin API, replacing the setting `AdminApi:Key`, which no longer exists: a local administrator from `SignIn:Administrator:Password`, an OpenID Connect provider with a required claim, or both. Logbook refuses to start if nobody could sign in. Sessions are cookies that last 12 hours (`SignIn:SessionLifetime`). Requests that change something also need the header `X-Requested-With`. Failed sign-ins are limited per IP address by `RateLimiting:InvalidSignInPermitsPerMinute`. Docker Compose reads the password from `LOGBOOK_ADMIN_PASSWORD` in `.env`, instead of `LOGBOOK_ADMIN_KEY`. Scripts now [sign in](docs/how-to/configure-sign-in.md#scripts) instead of sending the key. The new table of the keys of the sessions requires running the migrations, and a database user with limited rights needs `SELECT` and `INSERT` on `config.data_protection_keys`.
- `GET /health/details` with all checks and the version, for signed-in users and for monitoring tools with the new setting `HealthDetails:MonitoringKey`. See [Health checks](docs/reference/health-checks.md#details).
- Retention: a background service removes Log Events past the retention of their Application, dropping whole partitions where possible. Settings `Retention:DefaultDays` and `Retention:Interval`, `PUT /api/v1/applications/{id}/retention`, and a field for it in the web interface. **After upgrading, Log Events older than 30 days are removed**, unless you set a longer retention first. Dropping partitions requires running the migrations.
- Security headers on every response, including a content security policy, and HSTS over HTTPS. The header `Server` is no longer sent.
- Settings `ReverseProxy:*` for running behind a reverse proxy, so that rate limits and logs see the address of the client, and cookies are marked `Secure`.
- In the environment `Production`, Logbook refuses to start with [insecure settings](docs/reference/configuration.md#production-checks), e.g. `AllowedHosts` set to `*`, or serving other hosts over plain HTTP. A deployment that does this needs a change.
- How-to guides: [deploy to production](docs/how-to/deploy-production.md), [configure a reverse proxy](docs/how-to/reverse-proxy.md), [configure sign-in](docs/how-to/configure-sign-in.md), [upgrade](docs/how-to/upgrade.md), [back up and restore](docs/how-to/back-up-and-restore.md).
- The release workflow runs the tests first, and publishes `Logbook.Sdk`, `Logbook.Extensions.Logging` and `Logbook.AspNetCore` to NuGet, with the version from the tag. The server reports that version as well.
- Request and dependency tracking: `builder.Services.AddLogbookTelemetry()` in `Logbook.AspNetCore` sends each request with its route, status and duration, each `HttpClient` call and, optionally, SQL commands of drivers with an `ActivitySource`, such as Npgsql and MySqlConnector. Microsoft.Data.SqlClient is not supported yet. See [Track requests and dependencies](docs/how-to/track-requests.md).
- Spans: `POST /api/v1/spans` receives requests and dependencies, and `GET /api/v1/spans` searches them, stored in the new partitioned table `telemetry.spans`. Retention and deleting an Application cover them. This requires running the migrations, and a database user with limited rights needs `SELECT`, `INSERT` and `DELETE` on `telemetry.spans`. See [Telemetry API](docs/reference/telemetry-api.md).
- Transactions: `GET /api/v1/traces/{traceId}` returns the spans and Log Events of a trace, and the web interface shows them on a timeline.
- Groups of exceptions: `GET /api/v1/exceptions` groups the exceptions in Log Events by type and stack fingerprint, with counts and first and last occurrence, and the web interface lists them. `GET /api/v1/logs` and its stream have the new filter `exceptionFingerprint`, and Log Events the field of that name. Log Events stored earlier have no fingerprint and are not grouped. The new indexes can take a while to create on a large table.

### Changed

- `DELETE /api/v1/applications/{id}` also deletes the Log Events of the Application. A database user with limited rights now needs `DELETE` on `telemetry.log_events`.
- Requests while the database cannot be reached return `503` with `Retry-After` instead of `500`.
- `LogIngestion:MaxQueuedBytes`, `MaxQueueSize` and `MaxQueueBytes` now apply to Log Events and spans separately, so the memory used can reach twice the value when spans are sent.

### Fixed

- `docker compose up` failed on a new checkout, because the database volume was mounted at the path PostgreSQL 18 images refuse.
- `GET /api/v1/logs` returned `500` for every page containing a Log Event whose `exception` or `properties` was nested 30 levels deep. `POST /api/v1/logs` now rejects such a value with `400`, as the documented limit is 29 levels. A Log Event of this kind that was stored earlier still breaks the search until it is deleted or retention removes it.
