# Architecture

Logbook is a single ASP.NET Core application backed by one PostgreSQL database, a React web interface it serves, and a set of SDK packages that applications use to send their logs. This page explains how it is put together and why. It is useful background for operators and the starting point for contributors.

## The big picture

```mermaid
flowchart LR
    App[".NET application<br/>+ Logbook SDK"] -- "batched Log Events<br/>API Key" --> Api["Log API<br/>/api/*"]
    Browser -- "web interface<br/>(static files)" --> Web["React app"]
    Web -- "/api/* queries" --> Api
    Api --> Core["Core: domain and use cases"]
    Core --> Infra["Infrastructure: EF Core, background services"]
    Infra --> DB[("PostgreSQL<br/>config · telemetry")]
```

One process serves everything: the log API that applications send to, the query API, and the web interface. Background services in the same process write buffered Log Events in batches and will drop expired data.

## The server: three projects

```
Logbook.Api  →  Logbook.Core  ←  Logbook.Infrastructure
```

**Logbook.Core** knows nothing about HTTP or databases. It contains:

- the **domain**: entities such as Application and API Key, created through factory methods and changed through methods that enforce their rules, so an invalid object cannot exist.
- the **use cases**: one service class per action, each with a single `ExecuteAsync` method and its own request and response types.
- **abstractions** for what it needs from the outside, e.g. repositories.

**Logbook.Infrastructure** implements those abstractions with Entity Framework Core and PostgreSQL and runs the background services.

**Logbook.Api** is the thin outer layer: controllers that translate HTTP into calls to Core services, error handling, health checks and the wiring in `Program.cs`.

## Applications and API Keys

An Application is a program that sends its logs to Logbook. It authenticates with an API Key, and can have several at once, so a key can be replaced without losing logs: issue a new one, update the program, revoke the old one.

An API Key is a 256-bit random value. Logbook shows it once when it is issued and stores only its SHA-256 hash, so a leaked database does not reveal any usable key. A fast hash is enough here, unlike for passwords, because a random value of that size cannot be guessed. The first API Key of an Application and any key issued without an expiry stay valid until they are revoked: an expired key would make an application silently lose its logs, because the SDK drops what it cannot send rather than disturb the application.

The admin API that manages them is protected by a single key from the configuration, sent as a Bearer token. It is deliberately simple, because it only has to hold until sign-in arrives in milestone M4. Logbook refuses to start without it, so an unprotected admin API cannot happen by accident. The key is separate from API Keys in both directions: an API Key can never manage Logbook.

## Receiving Log Events

An application sends its Log Events in batches to `POST /api/v1/logs`, with its API Key in the header `X-Logbook-Key`. The Application of a Log Event always comes from the API Key, never from the body, so one Application cannot write into another. The Environment does come from the body: it is a label for filtering, so a Log Event without one is kept and stored as `Unknown` rather than lost.

The API Key is checked before the body is read, so a request without a valid key costs almost nothing. The size limit is checked while reading, also after decompression, because neither a chunked request nor a small gzip body that expands to gigabytes announces its real size.

A batch is accepted or rejected as a whole, which keeps the answer to the sender simple: `202`, or an error that a retry will not fix. That only works if ordinary logs do not get rejected, so values that PostgreSQL cannot store but a log line can contain, such as the character U+0000, are replaced instead of failing the batch.

An accepted batch goes into a queue in memory, and a background service writes from it with PostgreSQL's `COPY`. Sending and writing are separated so that a burst of logs or a slow database does not slow down the applications sending them, and many small requests become one large write. The price is that `202` does not mean stored yet: if the process crashes, what is in the queue is lost. For logs this is a fair trade, and the SDK loses data in the same way when an application crashes.

The queue is limited by bytes rather than by number of Log Events, because a single Log Event can be hundreds of kilobytes. When it is full, requests get `503`, so the pressure moves back to the senders, whose SDKs retry and eventually drop the oldest events instead of Logbook running out of memory.

The queue is shared, so one Application could fill it for all the others. That is what the rate limit per API Key is for: it stops a program that sends far too often, before its request is even read. It answers `429`, which tells that one sender to slow down, whereas `503` means that Logbook as a whole is overloaded. The limit refills continuously rather than per minute, because logs are bursty: right after a burst is often when they matter most, and an application should not lose them until the next minute starts.

API Keys that are not valid have their own limit per IP address, because each one costs a lookup in the database. Guessing a 256-bit key is not realistic, so this is about load rather than security. Only failures count, so applications with a valid API Key never use it up.

What happens when a write fails depends on the cause. If the database is unavailable, the batch is kept and retried with increasing delays, because every Log Event in it is still fine. If PostgreSQL refuses the data, retrying would block all writing forever, so the batch is split until the one Log Event at fault is found, and only that one is dropped. A retry can also repeat a write that succeeded but whose confirmation was lost; the primary key recognizes those Log Events, so they are stored once.

## Searching Log Events

Searching requires the admin key, not an API Key. An API Key lives in an application's configuration and on servers, so it is the credential most likely to leak; if it could read logs, one leak would expose everything those logs contain.

Results are paged with a cursor, a position in the order of `Timestamp` and `Id`, rather than with page numbers. An offset would have to read and skip all earlier rows, so it gets slower with every page, and new Log Events would shift the results between requests. For the same reason there is no total count: on a table this size, counting is the expensive part.

A search reads an index backwards from the newest Log Event until the page is full. That is fast when many Log Events match, but slow when few do, and a filter that matches nothing would read the whole table. So the filters that can match few rows have their own indexes: Application, Environment, and a small one for the levels from `Warning` up, which are rare but what people look for. Each index slows down writing, so there is deliberately none for the lower levels or for `Category`.

Full-text search covers whole words without stemming. Stemming follows the rules of one language, while logs mix languages and identifiers, and `failed` being found by `fail` is worth less than `OrderService` being found exactly as written.

## The web interface

`src/Logbook.Web` is a React application written in TypeScript and built with Vite. When the container image is built, its output is copied into the server's `wwwroot`, so one container serves the API and the interface from the same origin: no CORS, one address to secure. Every path that is not an API route or a file returns `index.html`, so the interface handles its own routes in the browser.

During development the Vite server runs on its own port and forwards API calls to the server, so changes appear without a rebuild.

## The SDK

The SDK packages live in `sdk/` and are published to NuGet:

| Package | Purpose |
|---|---|
| `Logbook.Sdk` | The client: a bounded in-memory buffer, background batching and sending, retries. Targets `netstandard2.0` and `net10.0`, so older applications can use it too. |
| `Logbook.Extensions.Logging` | The `ILogger` provider. Existing code that logs through `ILogger` sends to Logbook without changes. |
| `Logbook.AspNetCore` | Request, dependency and exception tracking for ASP.NET Core applications. |

The most important rule of the SDK is that it never hurts the application it runs in. A call to `ILogger` only puts the event into the buffer and returns. Sending happens in the background; if Logbook is slow or unreachable, the SDK retries and, when the buffer is full, drops the oldest events instead of using more memory or blocking.

A batch stays in the buffer until the server has accepted it, so it counts towards the limits while it is being sent and is the first to be dropped. This is why the buffer is a queue behind a short lock and not a `Channel<T>`, from which an event is gone once it is read. The buffer is limited by memory as well as by number of events, because 10,000 events of 256 KiB would otherwise take gigabytes from the application. A batch is sent when it is full or when its oldest event has waited for the flush interval, compressed with gzip.

The server rejects a whole batch for a single event it does not accept, so the client enforces the same limits before sending. Dropping a large event would lose exactly the logs that tend to matter, such as a huge stack trace, so its long text is cut first and it is marked as truncated. Since a wrong API Key is usually fixed by someone within a while, it is treated like an outage with a long backoff rather than a reason to throw events away. The details are in [SDK behavior](../reference/sdk-behavior.md).

The `ILogger` provider writes the structured values of a call to JSON right away, on the thread of the application, because they could change while the event waits in the buffer. Objects other than plain values and collections are sent as text instead of being serialized: reflection would run their getters, could follow cycles and could send secrets nobody meant to log.

The SDK reports what it does through an `EventSource`. Logging through `ILogger` would send its own logs to itself, and every attempt to send would create more to send.

The SDK does not reference the server projects. The two only share the format of the requests, which is versioned so that older SDKs keep working with newer servers.

## The database

PostgreSQL holds all state, in two schemas:

| Schema | Content |
|---|---|
| `config` | Applications, API Key hashes, retention settings and, later, alert rules. Small, kept, backed up. |
| `telemetry` | Log Events and, later, requests and dependencies. Large, written constantly, expires. |

The split follows how the data is used. Log Events are stored in tables **partitioned by day** in UTC, so retention drops whole partitions instead of deleting rows, which is fast and leaves no bloat. Their properties are stored as `jsonb` with a GIN index, so you can search by any property your application logs.

Deleting an Application deletes its Log Events, because logs can contain personal data, and "delete" should mean it. They have no foreign key to their Application: a cascade would delete them all in one long transaction, and the key would be checked for every inserted row. Instead, the Application goes first, so its API Keys stop working, and then its Log Events go in small steps, so an interrupted request keeps its progress and repeating it finishes the job. Log Events still in the queue cannot slip through: each write first locks the Applications it writes for and discards Log Events whose Application is gone, while a deletion waits for writes in progress and then removes their rows too.

Logbook never changes its schema at startup. Migrations are a separate, explicit step (the `migrator` container image or `dotnet ef database update`), so a deployment never alters the database by accident and the application user needs no rights to change the schema.

Partitions are the one thing that has to appear while Logbook runs, because new days keep coming. A database function created by the migrations makes the partition for a day when the first Log Event of that day arrives. It runs with the rights of the user that applied the migrations, so the application user only needs to be allowed to call it. The application user does need `USAGE` on the schema `telemetry` and `SELECT`, `INSERT` and `DELETE` on `telemetry.log_events`. Only days inside the accepted time window of a Log Event get a partition, so a sender cannot create an unlimited number of tables.
