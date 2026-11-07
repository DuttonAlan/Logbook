# Run Logbook from source

This guide shows how to build and run Logbook directly with the .NET SDK and Node.js, e.g. to develop or debug it. To only try Logbook out, [Getting started](../tutorials/getting-started.md) with Docker is quicker.

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Node.js 24](https://nodejs.org/)
- PostgreSQL 17 or newer, e.g. `docker run -d --name logbook-db -p 5432:5432 -e POSTGRES_PASSWORD=12345 postgres:17-alpine`
- Docker, only to run the integration tests

## 1. Configure the database

`src/Logbook.Api/appsettings.Development.json` expects PostgreSQL on `localhost:5432` with user `postgres` and password `12345`. To use a different database without changing that file, set the connection string as an environment variable in the shell where you run the commands below:

```sh
export ConnectionStrings__Database="Host=localhost;Database=logbook;Username=…;Password=…"
```

## 2. Create the database schema

Logbook never migrates the database at startup:

```sh
dotnet tool install --global dotnet-ef
dotnet ef database update --project src/Logbook.Infrastructure --startup-project src/Logbook.Api
```

## 3. Run the server

```sh
dotnet run --project src/Logbook.Api
```

| URL | What |
|---|---|
| <http://localhost:5080/health> | Readiness status |
| <http://localhost:5080/swagger> | Swagger UI of the API (only in `Development`) |

The `https` launch profile also listens on <https://localhost:7080>. Trust the .NET development certificate once with `dotnet dev-certs https --trust`.

The [admin API](../reference/admin-api.md) needs a session. In `Development`, sign in as `admin` with the password `development-only-password-do-not-deploy`, from `appsettings.Development.json`. In Swagger UI, call `POST /api/v1/auth/sign-in` with these first.

## 4. Run the web interface

In a second terminal:

```sh
cd src/Logbook.Web
npm ci
npm run dev
```

Open <http://localhost:5173/>. The Vite development server reloads on every change and forwards `/api` and `/health` to the server on port 5080. Sign in with the name and password above. See [Search and follow logs](explore-logs.md) for what it can do.

## 5. Build and test

```sh
dotnet build Logbook.slnx
dotnet test --solution Logbook.slnx
```

The integration tests start PostgreSQL with Testcontainers, so Docker must be running. The web interface is checked with `npm run lint && npm run build` in `src/Logbook.Web`. See [CONTRIBUTING.md](../../CONTRIBUTING.md) for more, e.g. how to run a single test.
