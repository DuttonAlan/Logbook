# Getting started

In this tutorial you run Logbook and its database with Docker Compose, send your first Log Event and find it again. It takes about five minutes.

## What you need

- Docker with Docker Compose
- Git

## 1. Get the sources

```sh
git clone https://github.com/DuttonAlan/Logbook.git
cd Logbook
```

## 2. Set the passwords

Docker Compose reads the password of the PostgreSQL user and the password to sign in to Logbook from a file `.env`, which Git ignores:

```sh
cp .env.example .env
```

Open `.env` and set:

- `POSTGRES_PASSWORD` to a password of your choice.
- `LOGBOOK_ADMIN_PASSWORD` to a password of at least 12 characters, e.g. the output of `openssl rand -base64 24`.

## 3. Start Logbook

```sh
docker compose up --build
```

Compose starts three containers in order:

1. `db`: PostgreSQL, with its data in the volume `db-data`.
2. `migrate`: creates or updates the database schema, then exits. Logbook never changes the schema at startup.
3. `logbook`: the server with the web interface, on port 5080.

The first build takes a few minutes.

## 4. Open the web interface

Open <http://localhost:5080/> and sign in with the name `admin` and the value of `LOGBOOK_ADMIN_PASSWORD` from your `.env`.

The top right shows the status of the server, which should be *Healthy*. The same status is available for monitoring tools:

```sh
curl http://localhost:5080/health
```

```json
{"status":"Healthy"}
```

## 5. Create an Application

Every program that sends logs is an Application, with its own API Key.

1. Choose **Applications**.
2. Enter a name, e.g. `My First Application`, and choose **Create**.
3. The API Key is shown only this once. Copy it, and in a second terminal, make it available:

```sh
export LOGBOOK_API_KEY=…
```

## 6. Send your first Log Event

Go back to **Logs** and choose **Live tail**, so that you can watch it arrive. Then send a Log Event with this API Key:

```sh
curl -X POST http://localhost:5080/api/v1/logs \
  -H "X-Logbook-Key: $LOGBOOK_API_KEY" \
  -H "Content-Type: application/json" \
  -d "[{\"timestamp\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"level\":\"Error\",\"message\":\"Order 42 failed\",\"environment\":\"Development\",\"properties\":{\"OrderId\":42}}]"
```

```json
{"accepted":1}
```

Logbook writes Log Events in the background, so yours appears about a second later.

## 7. Find it again

Turn **Live tail** off, enter `failed` in the search field and choose the level **Error**. Your Log Event is the only result.

Select it to open its details. They show the property `OrderId`. Choose **Filter** next to it, and the search now also requires `OrderId=42`. The filters are part of the address, so you could send this search to someone else.

## 8. Stop Logbook

Press <kbd>Ctrl</kbd>+<kbd>C</kbd>, then remove the containers:

```sh
docker compose down
```

The database stays in the volume `db-data`. To remove it as well, run `docker compose down --volumes`.

## Next steps

- [Connect an application](../how-to/connect-application.md) to send the logs of a .NET application.
- [Search and follow logs](../how-to/explore-logs.md) shows what else the web interface can do.
- [Log API](../reference/log-api.md) lists everything you can send and search for.
- [Manage Applications and API Keys](../how-to/manage-applications.md) to rotate an API Key or remove an Application.
- [Deploy to production](../how-to/deploy-production.md) to use it for real.
- [Run Logbook from source](../how-to/run-from-source.md) to develop it.
- [Architecture](../explanation/architecture.md) explains how the parts fit together.
