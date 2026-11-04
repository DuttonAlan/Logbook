# Getting started

In this tutorial you run Logbook and its database with Docker Compose and open the web interface. It takes about five minutes.

> Logbook is at milestone M1: the server runs, reports its health and manages Applications and API Keys, but cannot receive logs yet. This tutorial grows with each milestone; see the [roadmap](../roadmap.md).

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

## 5. Stop Logbook

Press <kbd>Ctrl</kbd>+<kbd>C</kbd>, then remove the containers:

```sh
docker compose down
```

The database stays in the volume `db-data`. To remove it as well, run `docker compose down --volumes`.

## Next steps

- [Manage Applications and API Keys](../how-to/manage-applications.md) to create your first Application.
- [Run Logbook from source](../how-to/run-from-source.md) to develop it.
- [Architecture](../explanation/architecture.md) explains how the parts fit together.
