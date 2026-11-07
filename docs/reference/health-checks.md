# Health checks

| Endpoint | Access | Use |
|---|---|---|
| `GET /health/live` | Public | Liveness: the process answers. Never touches the database. |
| `GET /health` | Public | Readiness: only the overall status. |
| `GET /health/details` | Signed-in user or monitoring key | All checks with their data, and the version. See [Details](#details). |

`AllowedHosts` also applies to these, so a probe must send one of the allowed host names in `Host`.

## Status

| Status | HTTP | Meaning |
|---|---|---|
| `Healthy` | `200` | Everything works. |
| `Degraded` | `200` | Logbook works, but needs attention (see below). |
| `Unhealthy` | `503` | Logbook cannot serve requests correctly. |

The two public endpoints return only `{"status": "Healthy"}`. The details are not public, because they reveal e.g. the schema version.

## Checks

| Name | `Unhealthy` when | `Degraded` when |
|---|---|---|
| `database` | The database cannot be reached. | – |
| `migrations` | Migrations are pending: the database is older than this version. Run the migrator. | The database has migrations this version does not know: it is newer, e.g. after a rollback. |

## Details

`GET /health/details` is for monitoring that should say more than "not healthy". A monitoring tool cannot sign in, so it can use a key of its own, from the setting [`HealthDetails:MonitoringKey`](configuration.md#server), which gives access to nothing else:

```sh
curl -H "Authorization: Bearer $MONITORING_KEY" https://logbook.example.com/health/details
```

It answers with the same status codes as above, or `401` without access. The response is never cached:

```json
{
  "status": "Healthy",
  "version": "1.0.0+a1b2c3d",
  "duration": "00:00:00.0123456",
  "checks": [
    {
      "name": "database",
      "status": "Healthy",
      "description": null,
      "duration": "00:00:00.0052147",
      "data": {}
    },
    {
      "name": "migrations",
      "status": "Healthy",
      "description": "The database schema is up to date.",
      "duration": "00:00:00.0064210",
      "data": {
        "current": "20261106094536_AddRetentionAndDataProtectionKeys",
        "latest": "20261106094536_AddRetentionAndDataProtectionKeys",
        "pending": [],
        "unknown": []
      }
    }
  ]
}
```

Errors of a check are left out, as they can contain the address of the database. They are in the log of the server.
