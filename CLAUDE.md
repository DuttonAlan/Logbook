# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Logbook is a lightweight, self-hosted logging solution for .NET applications: a log API, a React web interface to search and watch logs, and a C# SDK that plugs into `ILogger`. The server is built on .NET 10, ASP.NET Core and EF Core with PostgreSQL. The structure and conventions follow the sibling project Authora (github.com/DuttonAlan/Authora).

## Git

Never run `git commit`, `git pull` or `git push`. Leave all changes uncommitted in the working tree; the user handles git.

## Commands

```sh
dotnet build Logbook.slnx                      # warnings are errors (TreatWarningsAsErrors + EnforceCodeStyleInBuild)
dotnet test --solution Logbook.slnx            # all tests; the integration tests need Docker (Testcontainers starts postgres:17-alpine)

# Single test class / method (xunit v3 on Microsoft.Testing.Platform, see global.json)
dotnet test --project tests/Logbook.Api.IntegrationTests --filter-class "*HealthEndpointTests"
dotnet test --project tests/Logbook.Sdk.Tests --filter-method "*Defaults_AreBoundedAndBatched"

# Run the API locally (Development: Swagger UI enabled, connection string in appsettings.Development.json) -> http://localhost:5080
dotnet run --project src/Logbook.Api

# Web interface (Vite dev server on http://localhost:5173, proxies /api and /health to the API)
cd src/Logbook.Web && npm ci && npm run dev
npm run lint && npm run build                  # oxlint and TypeScript; CI runs both

# EF Core migrations (Infrastructure holds the DbContext, Api is the startup project)
dotnet ef migrations add <Name> --project src/Logbook.Infrastructure --startup-project src/Logbook.Api
dotnet ef database update --project src/Logbook.Infrastructure --startup-project src/Logbook.Api
dotnet ef migrations has-pending-model-changes --project src/Logbook.Infrastructure --startup-project src/Logbook.Api

# Containers: two targets of one Dockerfile (runtime = server + web interface on HTTP 8080, migrator = EF migration bundle)
docker build --target runtime -t logbook .
docker build --target migrator -t logbook-migrator .
docker compose up --build   # local stack on http://localhost:5080; needs .env, see docs/tutorials/getting-started.md
```

There is no separate C# lint step: analyzers and `.editorconfig` style rules run on build and fail it. CI (`.github/workflows/build.yml`) runs restore, Release build, tests, `has-pending-model-changes`, packs the SDK, lints and builds the web interface, and builds both container images, so every model change needs a migration.

The API does **not** apply migrations at startup (run `database update` first).

## Architecture

Server: three projects with a strict dependency direction: `Logbook.Api` → `Logbook.Core` ← `Logbook.Infrastructure`.

- **Logbook.Core** has no EF/ASP.NET dependency.
  - `Domain/` – rich entities created through static `Create(...)` factories and changed through methods. Invalid input throws `DomainValidationException`. Timestamps are UTC.
  - `Application/<Area>/<UseCase>/` – one service class per use case with a single `ExecuteAsync`, plus its `*Request`/`*Response` records. Each new service must be registered in `Core/ServiceCollectionExtensions.cs`.
  - `Abstractions/` – repository interfaces (`I*Repository`) and other ports implemented in Infrastructure.
- **Logbook.Infrastructure** – `LogbookContext` with `IEntityTypeConfiguration`s in `Persistence/Configurations`, repositories, migrations, health checks and background services. Two PostgreSQL schemas (`Persistence/Schemas.cs`): `config` (Applications, API Keys, settings) and `telemetry` (log events, partitioned by day so retention drops partitions).
- **Logbook.Api** – thin controllers that call Core services, `ProblemDetailsExceptionHandler`, health endpoints and `Program.cs` for wiring. It serves the built web interface from `wwwroot` (only in the container image) with a fallback to `index.html`; unknown `/api/*` routes return 404.

Web interface: `src/Logbook.Web`, React + TypeScript with Vite. Not part of the .NET solution. The Dockerfile builds it and copies `dist/` into the Api's `wwwroot`.

SDK: `sdk/` holds the NuGet packages; shared package settings are in `sdk/Directory.Build.props`. `Logbook.Sdk` (netstandard2.0 + net10.0) is the client: bounded in-memory buffer, background batching, retries. `Logbook.Extensions.Logging` is the `ILogger` provider, `Logbook.AspNetCore` adds request and dependency tracking. The SDK never references the server projects. It must never throw into or block the host application.

### Errors

Services throw `NotFoundException` (404), `ConflictException` (409), `DomainValidationException` (400) or `ServiceBusyException` (503 with `Retry-After`); `ProblemDetailsExceptionHandler` maps them, plus unique-violation and concurrency `DbUpdateException`s, to 409. `InvalidOperationException`/`ArgumentException` are intentionally **not** mapped and stay 500 — don't throw them for business-rule violations.

### Conventions

- Every `.cs` file starts with the MIT copyright header (`file_header_template` in `.editorconfig`, IDE0073).
- Public members need XML doc comments (`GenerateDocumentationFile`; they also feed Swagger). Controllers document every response code with `<response>` and `[ProducesResponseType]`.
- Logging uses source-generated `[LoggerMessage]` extensions in per-area `*Log.cs` classes with unique EventIds per area. Never log API Keys, only their Ids, and never write received log events into Logbook's own logs.
- Options classes are bound in `Program.cs` with `ValidateOnStart()` and validated by `IValidateOptions<T>` validators registered via `TryAddEnumerable`.
- API Keys are stored only as SHA-256 hashes; use `TimeProvider` for the current time.
- Package versions live in each `.csproj` (no central package management), like in Authora.

### Tests

- `tests/Logbook.Api.IntegrationTests`: `LogbookApiFactory` (`WebApplicationFactory<Program>`) starts a PostgreSQL container, migrates it and runs in environment `Testing`. `MigrationTests` checks for model changes without a migration and needs no database.
- `tests/Logbook.Sdk.Tests`: unit tests of the SDK without a server.
- Test-wide build settings live in `tests/Directory.Build.props`.

## Docs

The documentation follows Diátaxis (`docs/README.md` is the index). Keep it in sync with the code: a change to endpoints, settings or flows updates the matching reference page and any affected guide.

- `docs/tutorials/` – learning by doing (Docker Compose quickstart).
- `docs/how-to/` – task guides (run from source; later: production deployment, reverse proxy, connect an application).
- `docs/reference/` – exact facts: `configuration.md` (every setting, default and limit), `log-api.md`, `admin-api.md`, `health-checks.md`.
- `docs/explanation/` – design and reasoning: architecture.
- `docs/roadmap.md` – the milestones M0–M8 with their features. Tick off items as they are built.
- Website: VitePress builds `docs/` (`.github/workflows/docs.yml`, GitHub Pages). `docs/index.md` is the homepage, `docs/README.md` becomes `/overview`, the sidebar is in `docs/.vitepress/config.mts`, so a new page must be added there. Links that leave `docs/` are rewritten to GitHub at build time. Check with `npm ci && npm run build` in `docs/` (fails on dead links).
- `CHANGELOG.md` – add an entry under *Unreleased* for every user-visible change. Bugs and shortcuts are tracked as GitHub issues, not in files.
