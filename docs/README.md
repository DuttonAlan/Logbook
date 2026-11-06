# Logbook documentation

The documentation follows the [Diátaxis](https://diataxis.fr/) framework and has four parts. Each part answers a different kind of question, so pick the one that fits what you need right now.

| | Part | Read it when you want to… |
|---|---|---|
| 🎓 | [Tutorials](#tutorials) | learn Logbook by doing, step by step, from nothing to a working result |
| 🛠️ | [How-to guides](#how-to-guides) | get a specific job done, e.g. run Logbook from source |
| 📖 | [Reference](#reference) | look up exact facts: settings, endpoints, events |
| 💡 | [Explanation](#explanation) | understand how Logbook works and why it is designed that way |

Logbook is built step by step; the [roadmap](roadmap.md) shows what exists and what comes next.

## Tutorials

Start here if you are new to Logbook.

1. [Getting started](tutorials/getting-started.md): run Logbook with Docker Compose and open the web interface.

## How-to guides

- [Run Logbook from source](how-to/run-from-source.md)
- [Manage Applications and API Keys](how-to/manage-applications.md)
- [Connect an application](how-to/connect-application.md)
- [Search and follow logs](how-to/explore-logs.md)

## Reference

- [Configuration](reference/configuration.md): every setting, its default and its limits
- [Admin API](reference/admin-api.md): manage Applications and API Keys
- [Log API](reference/log-api.md): send, search and follow Log Events
- [SDK behavior](reference/sdk-behavior.md): retries, limits, shutdown and diagnostics of the SDK
- [Health checks](reference/health-checks.md)

## Explanation

- [Architecture](explanation/architecture.md)

## Terms

Logbook capitalizes the names of its own objects (Application, API Key, Log Event) to tell them apart from the everyday words. An *Application* is a program that sends its logs to Logbook; a *Log Event* is one entry it sends.
