# Roadmap

Logbook is built in milestones. Each one ends with something usable. **Nothing is published before all of them are done: version 1.0 is M0 to M8.** Planned work is broken down further in GitHub issues; this page shows the big picture.

## Vision

Logbook is a **lightweight, self-hosted logging solution for .NET**: run one container, add one NuGet package, and see structured logs within five minutes. It sits between a plain log viewer and a full APM suite such as Application Insights: simple to run, open source, with the essentials of request and exception tracking.

**Principles**

- **Simple first.** One server process, one PostgreSQL database, one container image (plus the migrator).
- **.NET native.** The SDK plugs into `ILogger`; existing applications need no code changes.
- **Never hurt the host application.** The SDK never blocks and never throws. It buffers, retries, then drops.
- **Structured.** Message templates and properties are stored, so they can be searched and filtered.

**Not planned for 1.0:** distributed storage for very large volumes, code-level profiling, multi-tenant SaaS.

## Decisions

| Topic | Decision |
|---|---|
| Runtime | .NET 10 (LTS) |
| Server | ASP.NET Core with controllers, `Api → Core ← Infrastructure` like Authora |
| Storage | PostgreSQL with EF Core and Npgsql; schemas `config` and `telemetry` |
| Web interface | React and TypeScript with Vite, served by the server from `wwwroot` |
| SDK | `net10.0`, published to NuGet |
| Distribution | Container images `logbook` and `logbook-migrator` on GHCR |
| License | MIT |

## Log Event

The unit Logbook stores. The SDK sends Log Events in batches:

```json
{
  "timestamp": "2026-11-04T10:15:00.123Z",
  "level": "Error",
  "messageTemplate": "Order {OrderId} failed",
  "message": "Order 42 failed",
  "category": "Shop.Orders.OrderService",
  "environment": "Production",
  "eventId": 4001,
  "exception": { "type": "System.InvalidOperationException", "message": "…", "stackTrace": "…", "inner": null },
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "spanId": "00f067aa0ba902b7",
  "properties": { "OrderId": 42, "UserId": "abc" }
}
```

Every Log Event belongs to an **Application**, which the server takes from the API Key, and to an **Environment**, e.g. `Production`, which the SDK sends. The SDK reads it from `IHostEnvironment` unless the options override it, so one API Key can be used in several Environments. A Log Event without an Environment is stored with the Environment `Unknown`. The Environment is a filter label, not a security boundary; to isolate a stage, give it its own Application.

---

## M0: Foundations ✅

- [x] Solution in Authora's structure: `Logbook.Api`, `Logbook.Core`, `Logbook.Infrastructure`, SDK packages in `sdk/`, tests, sample application
- [x] Conventions enforced by the build: license header, analyzers, code style, XML documentation, warnings as errors
- [x] `LogbookContext` on PostgreSQL, `MigrationsHealthCheck`, `/health` and `/health/live`
- [x] Error handling with problem details (`NotFoundException`, `ConflictException`, `DomainValidationException`)
- [x] React web interface served by the server, with the Vite development proxy
- [x] Container images `runtime` and `migrator`, Docker Compose for local evaluation
- [x] CI: build, tests, missing-migration check, SDK packages, web interface, container images; CodeQL; Dependabot; release workflow
- [x] Documentation (Diátaxis, VitePress), README, CONTRIBUTING, SECURITY, CODE_OF_CONDUCT, CHANGELOG

## M1: Log API ✅

- [x] Entities `Application` and `ApiKey` (only the SHA-256 hash is stored), schema `config`
- [x] Entity `LogEvent` in schema `telemetry`, partitioned by day; `properties` and `exception` as `jsonb` with a GIN index; full-text search on `message`
- [x] Use cases and admin API to create, list, rename and delete Applications, and to issue and revoke API Keys, protected by a single configured admin key at first, replaced by sign-in in M4
- [x] `POST /api/v1/logs`: batched JSON, gzip support, size limits per request and per event
- [x] API Key authentication with the header `X-Logbook-Key`
- [x] Ingestion queue with batched writes (`COPY` or multi-row inserts) in a background service
- [x] `GET /api/v1/logs`: filter by time range, level, Application, Environment and text; cursor paging
- [x] Rate limits per API Key
- [x] Integration tests with Testcontainers; reference docs for the API

## M2: C# SDK ✅

- [x] `Logbook.Sdk`: bounded buffer limited by events and by memory, background sender, batching by size and time, gzip, drop-oldest when full
- [x] Retries with exponential backoff and `Retry-After`, size guards with truncation
- [x] `Logbook.Extensions.Logging`: `builder.Logging.AddLogbook(…)`, configuration from the section `Logbook`, Environment from `IHostEnvironment` with an override in the options
- [x] Message templates, scopes and `EventId` as structured properties
- [x] Exceptions with inner and aggregate exceptions
- [x] `traceId` and `spanId` from `Activity.Current`
- [x] Flush on shutdown; self-diagnostics through `EventSource`, so the SDK never logs into itself
- [x] Tests for every failure path: server down, slow, returning errors, buffer full
- [x] How-to guide: connect an application

## M3: Web interface, log explorer ✅

- [x] Routing, layout and API client (TanStack Router and Query)
- [x] Log list with virtualized infinite scrolling and level colors
- [x] Filters: time range, level, Application, Environment, free text, properties
- [x] Detail panel with properties, exception and stack trace
- [x] **Live tail** with server-sent events
- [x] "All Log Events of this trace"
- [x] Manage Applications and API Keys

## M4: Security and operations ✅

- [x] Sign-in to the web interface and admin API, replacing the admin key: a local administrator first, OpenID Connect (e.g. Authora) next
- [x] `/health/details` for authorized monitoring
- [x] Security headers, forwarded headers for reverse proxies, refusal to start with insecure production settings
- [x] Retention per Application: a background service drops expired partitions
- [x] How-to guides: deploy to production, configure a reverse proxy, upgrade, back up and restore

## M5: Application Insights-style telemetry ✅

- [x] `Logbook.AspNetCore`: track **requests** (route, status, duration)
- [x] **Dependencies**: `HttpClient` automatically, SQL optionally (drivers with an `ActivitySource`, not yet Microsoft.Data.SqlClient)
- [x] **Exceptions**: grouped by type and stack fingerprint, with counts and first and last occurrence
- [x] **Transactions**: request, dependencies and Log Events of one trace on one timeline

## M6: Dashboards and metrics

- [ ] Overview: Log Events over time, error rate, slowest requests, most frequent exceptions
- [ ] Custom metrics from the SDK
- [ ] Saved searches

## M7: Alerts

- [ ] Rules: "more than N errors in M minutes", "new exception type"
- [ ] Channels: webhook, email, then Slack and Teams
- [ ] Alert history

## M8: Ecosystem

- [ ] **OpenTelemetry (OTLP) ingestion**, so any OpenTelemetry SDK can send to Logbook
- [ ] Serilog sink and NLog target
- [ ] Query language, e.g. `level = Error and OrderId = 42`
- [ ] SDKs for JavaScript and Python

## Release 1.0

Only after M0 to M8 are finished:

- [ ] Images on GHCR, SDK packages published to NuGet from the release workflow
