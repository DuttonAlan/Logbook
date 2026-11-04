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
