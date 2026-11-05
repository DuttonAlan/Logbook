# Changelog

All notable changes to Logbook are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and Logbook uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Before 1.0, minor versions may contain breaking changes; they are listed under *Changed* or *Removed*.

## [Unreleased]

### Added

- Project foundations: server (`Logbook.Api`, `Logbook.Core`, `Logbook.Infrastructure`) on .NET 10 and PostgreSQL, web interface with React, SDK packages `Logbook.Sdk`, `Logbook.Extensions.Logging` and `Logbook.AspNetCore`.
- Health endpoints `/health` and `/health/live`.
- Container images for the server and the database migrations, and a Docker Compose setup for local evaluation.
- Documentation following Diátaxis, and the roadmap.
- Applications and API Keys: admin API under `/api/v1/applications` to create, list, rename and delete Applications and to issue and revoke API Keys. API Keys are shown once when issued and stored only as SHA-256 hashes.
- Setting `AdminApi:Key`, required at startup: the admin API is protected by this single key until sign-in exists. Docker Compose reads it from `LOGBOOK_ADMIN_KEY` in `.env`, so an existing `.env` needs this new value.
- Log API: `POST /api/v1/logs` receives batches of Log Events as JSON, optionally compressed with gzip, authenticated with the API Key of an Application in the header `X-Logbook-Key`. Log Events are stored in the schema `telemetry`, partitioned by day. A Log Event without an Environment is stored as `Unknown`.
- Settings `LogIngestion:MaxRequestBytes`, `LogIngestion:MaxEventBytes`, `LogIngestion:MaxEventAge` and `LogIngestion:MaxFutureOffset` for the limits of the log API.
- Ingestion queue: accepted Log Events are written to the database in batches by a background service, and what is left is written on shutdown. `POST /api/v1/logs` returns `503` with `Retry-After` when the queue is full. Settings `LogIngestion:MaxQueuedBytes`, `LogIngestion:WriteBatchSize` and `LogIngestion:WriteInterval`.
- Search: `GET /api/v1/logs` finds Log Events by time range, level, Application, Environment and full-text search on the message, newest first with cursor paging. It requires the admin key.
- Rate limit per API Key on `POST /api/v1/logs`: `429` with `Retry-After` above `RateLimiting:ApiKeyPermitsPerMinute`, by default 6000 requests per minute.

### Fixed

- `docker compose up` failed on a new checkout, because the database volume was mounted at the path PostgreSQL 18 images refuse.
