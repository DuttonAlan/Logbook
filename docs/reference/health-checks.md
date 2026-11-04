# Health checks

| Endpoint | Access | Use |
|---|---|---|
| `GET /health/live` | Public | Liveness: the process answers. Never touches the database. |
| `GET /health` | Public | Readiness: only the overall status. |

A detailed report for authorized monitoring follows with sign-in in milestone M4.

## Status

| Status | HTTP | Meaning |
|---|---|---|
| `Healthy` | `200` | Everything works. |
| `Degraded` | `200` | Logbook works, but needs attention (see below). |
| `Unhealthy` | `503` | Logbook cannot serve requests correctly. |

Both endpoints return only `{"status": "Healthy"}`. The details are not public, because they reveal e.g. the schema version.

## Checks

| Name | `Unhealthy` when | `Degraded` when |
|---|---|---|
| `database` | The database cannot be reached. | – |
| `migrations` | Migrations are pending: the database is older than this version. Run the migrator. | The database has migrations this version does not know: it is newer, e.g. after a rollback. |
