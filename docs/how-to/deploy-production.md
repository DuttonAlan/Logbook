# Deploy to production

This guide runs Logbook on a server with Docker Compose, behind a reverse proxy that terminates HTTPS. Logbook itself only needs the two container images and a PostgreSQL database, so the same settings work on any other platform that runs containers.

> The images are not on GHCR yet, as they will be [published with version 1.0](../roadmap.md#release-1-0). Until then, build them from a checkout of this repository with `docker build --target runtime -t logbook:local .` and `docker build --target migrator -t logbook-migrator:local .`, and use those names in `compose.yaml` below.

## Prerequisites

- A server with Docker and Docker Compose.
- A host name for Logbook that points to the server. This guide uses `logbook.example.com`.
- PostgreSQL 17 or newer. This guide runs it in a container. To use one you already have, remove the service `db` and change the connection strings.

## 1. Set the secrets

Create a folder for the deployment, and in it a file `.env`, which only you can read:

```sh
POSTGRES_PASSWORD=…        # e.g. from: openssl rand -hex 24
LOGBOOK_ADMIN_PASSWORD=…   # e.g. from: openssl rand -base64 24
```

## 2. Describe the services

Create `compose.yaml`. Replace the host name, and choose the version in the image tags: `1.0` follows the fixes of 1.0, while `1.0.0` stays exactly the same.

```yaml
name: logbook

services:
  db:
    image: postgres:18-alpine
    restart: unless-stopped
    environment:
      POSTGRES_DB: logbook
      POSTGRES_USER: logbook
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?}
    volumes:
      - db-data:/var/lib/postgresql
    networks: [internal]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U logbook -d logbook"]
      interval: 5s
      timeout: 5s
      retries: 30

  # Applies the database migrations, then exits. Logbook never migrates at startup.
  migrate:
    image: ghcr.io/duttonalan/logbook-migrator:1.0
    environment:
      ConnectionStrings__Database: Host=db;Database=logbook;Username=logbook;Password=${POSTGRES_PASSWORD}
    networks: [internal]
    depends_on:
      db:
        condition: service_healthy

  logbook:
    image: ghcr.io/duttonalan/logbook:1.0
    restart: unless-stopped
    # Longer than the shutdown timeout of the host (30 s), so Log Events in the queue get written.
    stop_grace_period: 35s
    environment:
      AllowedHosts: logbook.example.com
      ConnectionStrings__Database: Host=db;Database=logbook;Username=logbook;Password=${POSTGRES_PASSWORD}
      SignIn__Administrator__Password: ${LOGBOOK_ADMIN_PASSWORD:?}
      ReverseProxy__Enabled: "true"
      ReverseProxy__KnownNetworks__0: 172.30.0.0/24
    networks: [internal, proxy]
    depends_on:
      migrate:
        condition: service_completed_successfully

networks:
  internal:
  # The proxy connects through this network, whose addresses Logbook trusts for the forwarded headers.
  proxy:
    ipam:
      config:
        - subnet: 172.30.0.0/24

volumes:
  db-data:
```

No port of Logbook is published: everything goes through the proxy. Add the proxy from [Configure a reverse proxy](reverse-proxy.md) to this file, then start everything:

```sh
docker compose up -d
```

## 3. Check it

Open <https://logbook.example.com/> and sign in as `admin` with `LOGBOOK_ADMIN_PASSWORD`. Then go through the [checklist](#checklist).

If Logbook does not start, its log says why, including every [insecure setting](../reference/configuration.md#production-checks) it refuses:

```sh
docker compose logs logbook
```

## 4. Monitor it

A load balancer or orchestrator can use [`/health` and `/health/live`](../reference/health-checks.md). `AllowedHosts` also applies to them, so a probe must send the host name:

```sh
curl -H "Host: logbook.example.com" http://…:8080/health
```

For a monitoring tool that should see each check and the version, set `HealthDetails__MonitoringKey` and let it read [`/health/details`](../reference/health-checks.md#details).

## 5. Keep it running

- Decide how long to keep logs. [`Retention:DefaultDays`](../reference/configuration.md#retention) is 30 days, and each Application [can have its own](manage-applications.md#change-the-retention). Log Events past their retention are removed for good.
- [Back up](back-up-and-restore.md) the database.
- Read [how to upgrade](upgrade.md) before the next version.

## Several servers

Logbook can run several times against one database. Sessions work on all of them, because their keys are in the database. Three things are per server: the [rate limits](../reference/log-api.md#rate-limit), the queue of Log Events that are not written yet, and the [live tail](../reference/log-api.md#follow-log-events), which only shows what its own server has written and so needs sticky sessions.

## Checklist

- [ ] The address only works with HTTPS, and HTTP redirects to it.
- [ ] Only the proxy can reach port 8080 of Logbook, and only Logbook can reach the database.
- [ ] The secrets are not in Git.
- [ ] Failed sign-ins in the log show the addresses of the clients, not of the proxy. Otherwise the [forwarded headers](reverse-proxy.md#tell-logbook-about-the-proxy) are not set up, and everyone shares one rate limit.
- [ ] Backups run, and you have tried a restore.

## What the database holds

Anyone who can read the database can read all logs, and also the keys in `config.data_protection_keys` that sign sessions, which are stored unencrypted. That is why Logbook logs the warning `No XML encryptor configured` when it creates such a key. Protect the database and its backups like the logs themselves.
