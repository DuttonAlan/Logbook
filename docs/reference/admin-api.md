# Admin API

The admin API manages the Applications that send logs to Logbook and their API Keys. To use it step by step, see [Manage Applications and API Keys](../how-to/manage-applications.md).

- **Authentication:** the admin key from the setting [`AdminApi:Key`](configuration.md#server), as `Authorization: Bearer …`. Without a valid key every endpoint returns `401`. API Keys of Applications are not accepted. The admin key is replaced by sign-in in milestone M4.
- **Format:** JSON in and out; points in time in UTC. Errors are [problem details](https://www.rfc-editor.org/rfc/rfc9457): `400` (validation), `401`, `404`, `409` (conflict with the current state), `415` (a request body that is not JSON).
- **Schema:** the OpenAPI document and Swagger UI (`/swagger`) are served in `Development`.

## Applications

| Endpoint | Success | Description |
|---|---|---|
| `POST /api/v1/applications` | `201` | Create, with `name`. Also issues the first API Key, which does not expire. `409` if the name is taken. |
| `GET /api/v1/applications` | `200` | List, ordered by name. Returns a plain JSON array of `id`, `name` and `createdAt`. |
| `GET /api/v1/applications/{id}` | `200` | Details, including the metadata of all API Keys. `404` if unknown. |
| `PUT /api/v1/applications/{id}` | `204` | Rename, with `name`. API Keys stay valid. `404` if unknown, `409` if the name is taken. |
| `DELETE /api/v1/applications/{id}` | `204` | Delete, together with its API Keys. Its Log Events are not deleted. Also succeeds if the Application does not exist. |

An Application's `name` has 1 to 100 characters, is trimmed, and must be unique. Names are compared exactly, so `Shop` and `shop` are different names.

`POST /api/v1/applications` returns:

```json
{
  "id": "0199d1a2-7c3e-7b4a-9f10-5c963f66afa6",
  "apiKey": {
    "id": "0199d1a2-7c3e-7b4a-9f10-6c963f66afa6",
    "key": "q3Jv0Wc8uYb1nH4kT9sXe2LmPd7aRfG5zVo6iNwB0yE",
    "createdAt": "2026-11-04T10:15:00.123Z",
    "expiresAt": null
  }
}
```

`GET /api/v1/applications/{id}` returns:

```json
{
  "id": "0199d1a2-7c3e-7b4a-9f10-5c963f66afa6",
  "name": "Shop Web App",
  "apiKeys": [
    {
      "id": "0199d1a2-7c3e-7b4a-9f10-6c963f66afa6",
      "createdAt": "2026-11-04T10:15:00.123Z",
      "expiresAt": null,
      "revokedAt": null,
      "isActive": true
    }
  ],
  "createdAt": "2026-11-04T10:15:00.123Z",
  "modifiedAt": null
}
```

## API Keys

| Endpoint | Success | Description |
|---|---|---|
| `POST /api/v1/applications/{id}/api-keys` | `201` | Issue a new API Key; existing API Keys stay active. The body is optional. `400` if `expiresAt` is not in the future, `404` if the Application is unknown. |
| `POST /api/v1/applications/{id}/api-keys/{apiKeyId}/revoke` | `204` | Revoke. Revoking again succeeds and keeps the original time. `404` if the Application or API Key is unknown. |

The optional body of the first endpoint is `{"expiresAt": "2027-10-11T00:00:00Z"}`. Without it, the API Key does not expire. The response is the `apiKey` object shown above.

The plain-text `key` is returned **only once**, in the response that issues it, with `Cache-Control: no-store`. Logbook stores only its SHA-256 hash, so a lost API Key cannot be recovered: issue a new one and revoke the old one. A revoked or expired API Key stays listed for history, with `isActive` set to `false`.
