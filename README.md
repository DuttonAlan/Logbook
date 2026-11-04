# Logbook

### Log. Search. Understand.

[![Build](https://github.com/DuttonAlan/Logbook/actions/workflows/build.yml/badge.svg)](https://github.com/DuttonAlan/Logbook/actions/workflows/build.yml)
[![CodeQL](https://github.com/DuttonAlan/Logbook/actions/workflows/codeql.yml/badge.svg)](https://github.com/DuttonAlan/Logbook/actions/workflows/codeql.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)

Logbook is a lightweight, self-hosted **logging solution for .NET applications**: a log API, a web interface to search and watch your logs, and a C# SDK that plugs into `ILogger`, built on .NET 10, React and PostgreSQL.

Add one NuGet package to your application, run one container, and see structured logs within minutes, without the weight of a full APM suite.

> **Project status: very early.** Logbook is being built step by step; the server cannot receive logs yet. See the [roadmap](docs/roadmap.md) for what is planned and in which order.

## Features

Planned for version 1.0:

- 📥 **Log API**: batched, compressed ingestion of structured log events, authenticated per Application with API Keys
- 🧩 **C# SDK**: an `ILogger` provider that buffers and sends in the background, and never blocks or crashes your application
- 🔎 **Web interface**: search and filter by time, level, Application and text, see properties and exceptions, and follow a live tail
- 🔗 **Correlation**: every log event carries its trace and span, so all logs of a request are one click away
- 🧹 **Retention** per Application, so the database stays small
- 🏠 **Self-hosted**: one container and one PostgreSQL database

## Quick start

You need Docker.

```sh
git clone https://github.com/DuttonAlan/Logbook.git && cd Logbook

cp .env.example .env    # then set POSTGRES_PASSWORD
docker compose up --build
```

Open <http://localhost:5080/>. The [Getting started tutorial](docs/tutorials/getting-started.md) walks through these steps.

## Components

| Component | Path | Description |
|---|---|---|
| Server | `src/Logbook.Api`, `src/Logbook.Core`, `src/Logbook.Infrastructure` | Log API, query API and host of the web interface |
| Web interface | `src/Logbook.Web` | React and TypeScript, built with Vite |
| SDK | `sdk/Logbook.Sdk` | Core client: buffering, batching, retries |
| | `sdk/Logbook.Extensions.Logging` | `ILogger` provider: `builder.Logging.AddLogbook(…)` |
| | `sdk/Logbook.AspNetCore` | Request, dependency and exception tracking |

## Documentation

The [documentation](docs/README.md) has four parts:

- **Tutorials**: [Getting started](docs/tutorials/getting-started.md)
- **How-to guides**: [run Logbook from source](docs/how-to/run-from-source.md)
- **Reference**: [configuration](docs/reference/configuration.md), [health checks](docs/reference/health-checks.md)
- **Explanation**: [architecture](docs/explanation/architecture.md)

## Roadmap

Logbook is built in milestones, each ending with something usable. Version 1.0 is M0 to M4:

| Milestone | Content |
|---|---|
| M0 | Foundations: solution, CI, container images, documentation |
| M1 | Log API: Applications, API Keys, ingestion and query |
| M2 | C# SDK: `ILogger` provider, published to NuGet |
| M3 | Web interface: log explorer with filters and live tail |
| M4 | Security and operations: sign-in, retention, production guide |

After 1.0: request and dependency tracking, dashboards, alerts and OpenTelemetry (OTLP) ingestion. Details: [roadmap](docs/roadmap.md).

## Contributing

Contributions are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) for how to build, test and submit changes, and follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Security

Please do **not** report vulnerabilities in public issues. See [SECURITY.md](SECURITY.md) for how to report them privately.

## License

[MIT](LICENSE.txt) © Alan Dutton
