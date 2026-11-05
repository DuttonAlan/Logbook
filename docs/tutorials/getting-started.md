# Getting started

In this tutorial you run Logbook and its database with Docker Compose, send your first Log Event and find it again. It takes about five minutes.

> Logbook has finished milestone M1: the server receives and searches logs through its API. The SDK (M2) and the log explorer in the web interface (M3) come next, so this tutorial uses `curl` for now. It grows with each milestone; see the [roadmap](../roadmap.md).

## What you need

- Docker with Docker Compose
- Git

## 1. Get the sources

```sh
git clone https://github.com/DuttonAlan/Logbook.git
cd Logbook
```

## 2. Set the database password and the admin key

Docker Compose reads the password of the PostgreSQL user and the key for the admin API from a file `.env`, which Git ignores:

```sh
cp .env.example .env
```

Open `.env` and set:

- `POSTGRES_PASSWORD` to a password of your choice.
- `LOGBOOK_ADMIN_KEY` to a random value of at least 32 characters, e.g. the output of `openssl rand -base64 32`.

## 3. Start Logbook

```sh
docker compose up --build
```

Compose starts three containers in order:

1. `db`: PostgreSQL, with its data in the volume `db-data`.
2. `migrate`: creates or updates the database schema, then exits. Logbook never changes the schema at startup.
3. `logbook`: the server with the web interface, on port 5080.

The first build takes a few minutes.

## 4. Check that it works

Open <http://localhost:5080/>. The web interface shows *Server status: Healthy*.

The same status is available for monitoring tools:

```sh
curl http://localhost:5080/health
```

```json
{"status":"Healthy"}
```

## 5. Send your first Log Event

Every program that sends logs is an Application, with its own API Key. In a second terminal, in the same folder, read the admin key from `.env` and create an Application:

```sh
export LOGBOOK_ADMIN_KEY=$(grep '^LOGBOOK_ADMIN_KEY=' .env | cut -d= -f2-)

curl -X POST http://localhost:5080/api/v1/applications \
  -H "Authorization: Bearer $LOGBOOK_ADMIN_KEY" \
  -H "Content-Type: application/json" \
  -d '{"name":"My First Application"}'
```

The response contains the API Key as `apiKey.key`. It is shown only this once, so copy it:

```sh
export LOGBOOK_API_KEY=…
```

Now send a Log Event with this API Key:

```sh
curl -X POST http://localhost:5080/api/v1/logs \
  -H "X-Logbook-Key: $LOGBOOK_API_KEY" \
  -H "Content-Type: application/json" \
  -d "[{\"timestamp\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"level\":\"Error\",\"message\":\"Order 42 failed\",\"environment\":\"Development\",\"properties\":{\"OrderId\":42}}]"
```

```json
{"accepted":1}
```

## 6. Find it again

Logbook writes Log Events in the background, so they can be found about a second later. Search for a word of the message, with the admin key:

```sh
curl -G http://localhost:5080/api/v1/logs \
  -H "Authorization: Bearer $LOGBOOK_ADMIN_KEY" \
  --data-urlencode "text=failed" \
  --data-urlencode "level=Error"
```

The response contains your Log Event, with its structured properties, and `nextCursor` to get the next page, which is `null` here because there are no more:

```json
{
  "items": [
    {
      "id": "…",
      "applicationId": "…",
      "timestamp": "2026-11-04T10:15:00Z",
      "level": "Error",
      "message": "Order 42 failed",
      "messageTemplate": null,
      "category": null,
      "environment": "Development",
      "eventId": null,
      "exception": null,
      "traceId": null,
      "spanId": null,
      "properties": { "OrderId": 42 }
    }
  ],
  "nextCursor": null
}
```

## 7. Stop Logbook

Press <kbd>Ctrl</kbd>+<kbd>C</kbd>, then remove the containers:

```sh
docker compose down
```

The database stays in the volume `db-data`. To remove it as well, run `docker compose down --volumes`.

## Next steps

- [Connect an application](../how-to/connect-application.md) to send the logs of a .NET application.
- [Log API](../reference/log-api.md) lists everything you can send and search for.
- [Manage Applications and API Keys](../how-to/manage-applications.md) to rotate an API Key or remove an Application.
- [Run Logbook from source](../how-to/run-from-source.md) to develop it.
- [Architecture](../explanation/architecture.md) explains how the parts fit together.
