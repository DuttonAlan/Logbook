# Contributing to Logbook

Thank you for your interest in improving Logbook. This guide explains how to propose changes and what a good pull request looks like.

By taking part, you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Ways to contribute

- **Report a bug:** open an [issue](https://github.com/DuttonAlan/Logbook/issues/new/choose) with the bug template.
- **Report a vulnerability:** not in an issue. Follow [SECURITY.md](SECURITY.md).
- **Suggest a feature:** open an issue with the feature template first. Logbook deliberately stays small, so it is worth agreeing on a feature before you build it. The [roadmap](docs/roadmap.md) shows what is planned.
- **Improve the documentation:** pull requests are welcome directly. The docs follow [Diátaxis](https://diataxis.fr/); see [docs/README.md](docs/README.md) for which part a change belongs in.
- **Fix a bug or build an agreed feature:** see below.

## Development setup

Prerequisites:

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Node.js 24](https://nodejs.org/) for the web interface
- Docker (the tests start PostgreSQL with Testcontainers)
- PostgreSQL 17 or newer to run Logbook locally, e.g. in Docker

[Run Logbook from source](docs/how-to/run-from-source.md) shows how to create the database and start the server and the web interface.

## Build and test

```sh
dotnet build Logbook.slnx
dotnet test --solution Logbook.slnx
```

Run a single test class or method:

```sh
dotnet test --project tests/Logbook.Api.IntegrationTests --filter-class "*HealthEndpointTests"
dotnet test --project tests/Logbook.Sdk.Tests --filter-method "*Defaults_AreBoundedAndBatched"
```

The web interface:

```sh
cd src/Logbook.Web
npm ci
npm run lint
npm run build
```

There is no separate lint step for C#. Analyzers and the `.editorconfig` style rules run during the build, and **warnings are errors**, so a clean build means the style is right.

CI runs the build in Release, the tests, a check that every model change has a migration, the SDK packages, the web interface and the container images. Your pull request must pass it.

## Project structure

```
src/Logbook.Core              domain entities and use cases; no ASP.NET Core or EF Core
src/Logbook.Infrastructure    EF Core, PostgreSQL, background services
src/Logbook.Api               controllers, Program.cs, hosts the web interface
src/Logbook.Web               web interface (React, TypeScript, Vite)
sdk/Logbook.Sdk               client: buffering, batching, sending (netstandard2.0, net10.0)
sdk/Logbook.Extensions.Logging  ILogger provider
sdk/Logbook.AspNetCore        request and dependency tracking
samples/SampleWebApp          application to try the SDK against a local server
tests/Logbook.Api.IntegrationTests
tests/Logbook.Sdk.Tests
tests/Logbook.Sdk.Tests.NetStandard
docs/                         documentation (Diátaxis)
```

Dependencies of the server point inward only: `Api → Core ← Infrastructure`. The SDK never references the server; it only shares the wire format. [Architecture](docs/explanation/architecture.md) explains the design.

## Conventions

**Code**

- Every `.cs` file starts with the MIT license header (enforced by the build):
  ```csharp
  // Copyright(c) Alan Dutton.
  // Licensed under the MIT License.
  // See the LICENSE file in the project root for license information.
  ```
- Public members need XML documentation comments; they also feed Swagger. Controllers document every response code with `<response>` and `[ProducesResponseType]`.
- Domain entities are created through static `Create(...)` factories and changed through methods. Invalid input throws `DomainValidationException`.
- One service class per use case in `Core/Application/<Area>/<UseCase>/`, registered in `Core/ServiceCollectionExtensions.cs`.
- Use `TimeProvider` for the current time; timestamps are UTC.
- Logging uses source-generated `[LoggerMessage]` methods in per-area `*Log.cs` classes, with unique Event Ids per area.

**Errors**

- API errors: throw `NotFoundException`, `ConflictException` or `DomainValidationException`. `InvalidOperationException` and `ArgumentException` mean a bug and become `500`.

**SDK**

- The SDK must never throw into or block the host application. Logging calls only enqueue; sending happens in the background. When the server is unreachable, the SDK retries, then drops the oldest events.
- Every SDK change needs a test for the failure path, e.g. the server is down or slow.
- The SDK targets `netstandard2.0`, so use only APIs available there or guard them with `#if NET`.

**Security rules**

- Never log API Keys or connection strings, only their Ids.
- Store API Keys only as hashes.
- Log events can contain personal data. Never write them to Logbook's own logs.

**Database**

- Every model change needs a migration:
  ```sh
  dotnet ef migrations add <Name> --project src/Logbook.Infrastructure --startup-project src/Logbook.Api
  ```
- Logbook never migrates at startup. Note in the changelog if a migration is not compatible with the previous version running.

**Tests**

- `tests/Logbook.Api.IntegrationTests` tests the server through HTTP. `LogbookApiFactory` starts PostgreSQL and the app.
- `tests/Logbook.Sdk.Tests` tests the SDK without a server. `tests/Logbook.Sdk.Tests.NetStandard` runs the same tests against its `netstandard2.0` build.
- Add a test for every bug fix and every new rule.

## Pull requests

1. Fork the repository and create a branch from `main`.
2. Keep the change focused: one bug or one feature per pull request.
3. Add or update tests.
4. Update the documentation when behavior, configuration or endpoints change, and add an entry under *Unreleased* in [CHANGELOG.md](CHANGELOG.md).
5. Make sure `dotnet build` and `dotnet test` pass, and `npm run lint && npm run build` if you changed the web interface.
6. Open the pull request and fill in the template.

Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/), e.g. `feat: accept gzip-compressed batches` or `docs: explain retention`.

A maintainer reviews every pull request.

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE.txt).
