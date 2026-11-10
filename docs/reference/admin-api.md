# Admin API

The admin API manages the Applications that send logs to Logbook and their API Keys. To use it step by step, see [Manage Applications and API Keys](../how-to/manage-applications.md).

- **Authentication:** a session from [signing in](#sign-in), in the cookie `logbook.session`. Without a valid session every endpoint returns `401`. API Keys of Applications are not accepted.
- **Changes:** requests other than `GET` also need the header `X-Requested-With`, with any value, or they return `403`. Another site cannot set this header in the browser of a signed-in user, so it cannot send such a request on their behalf.
- **Format:** JSON in and out; points in time in UTC. Errors are [problem details](https://www.rfc-editor.org/rfc/rfc9457): `400` (validation), `401`, `403`, `404`, `409` (conflict with the current state), `415` (a request body that is not JSON), `503` (the database cannot be reached, with a `Retry-After` header).
- **Schema:** the OpenAPI document and Swagger UI (`/swagger`) are served in `Development`.

## Sign-in

To set it up, see [Configure sign-in](../how-to/configure-sign-in.md).

| Endpoint | Success | Description |
|---|---|---|
| `GET /api/v1/auth/session` | `200` | Who is signed in, and how someone can sign in. Works without a session. |
| `POST /api/v1/auth/sign-in` | `204` | Signs in the local administrator with `userName` and `password`, and sets the session cookie. `401` if they are wrong or the local administrator is turned off, `429` with `Retry-After` after too many failures from one IP address ([`RateLimiting:InvalidSignInPermitsPerMinute`](configuration.md#server)). |
| `POST /api/v1/auth/sign-out` | `204` | Removes the session cookie from the browser. It does not revoke the session: a copy of the cookie works until it expires. |
| `GET /api/v1/auth/oidc/sign-in` | `302` | Redirects to the OpenID Connect provider, for a browser. The optional `returnUrl` is a path on this server to continue at. `404` if this sign-in is off. |

`GET /api/v1/auth/session` returns:

```json
{
  "isSignedIn": true,
  "userName": "admin",
  "canSignInWithPassword": true,
  "openIdConnectProvider": "Authora"
}
```

A session lasts for [`SignIn:SessionLifetime`](configuration.md#sign-in), however much it is used, and ends earlier when the password of the local administrator changes. The cookie is `HttpOnly` and `SameSite=Strict`, and `Secure` when the client uses HTTPS.

## Applications

| Endpoint | Success | Description |
|---|---|---|
| `POST /api/v1/applications` | `201` | Create, with `name`. Also issues the first API Key, which does not expire. `409` if the name is taken. |
| `GET /api/v1/applications` | `200` | List, ordered by name. Returns a plain JSON array of `id`, `name` and `createdAt`. |
| `GET /api/v1/applications/{id}` | `200` | Details, including the metadata of all API Keys. `404` if unknown. |
| `PUT /api/v1/applications/{id}` | `204` | Rename, with `name`. API Keys stay valid. `404` if unknown, `409` if the name is taken. |
| `PUT /api/v1/applications/{id}/retention` | `204` | Change how long its Log Events are kept, see [Retention](#retention). `404` if unknown. |
| `DELETE /api/v1/applications/{id}` | `204` | Delete, together with its API Keys and Log Events. Also succeeds if the Application does not exist. With many Log Events this can take a while. If the request is interrupted, send it again to finish deleting them. |

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
  "modifiedAt": null,
  "retentionDays": null,
  "effectiveRetentionDays": 30
}
```

## Retention

The body of `PUT /api/v1/applications/{id}/retention` is `{"retentionDays": 14}`, from 1 to 3650, or `{"retentionDays": null}` to use the setting [`Retention:DefaultDays`](configuration.md#retention). Anything else returns `400`.

In `GET /api/v1/applications/{id}`, `retentionDays` is what was set for this Application, and `effectiveRetentionDays` is what applies. Log Events past it are removed by the next retention run, and cannot be recovered.

## Saved searches

A saved search is a search of the log explorer under a name. Everyone who can sign in shares them.

| Endpoint | Success | Description |
|---|---|---|
| `GET /api/v1/saved-searches` | `200` | List, ordered by name. Returns a plain JSON array. |
| `POST /api/v1/saved-searches` | `201` | Create, with `name` and `query`. `409` if the name is taken, or if 500 saved searches exist. |
| `PUT /api/v1/saved-searches/{id}` | `200` | Rename or replace the query, with `name` and `query`. `404` if unknown, `409` if the name is taken. |
| `DELETE /api/v1/saved-searches/{id}` | `204` | Delete. Also succeeds if the saved search does not exist. |

A saved search looks like this:

```json
{
  "id": "0199d1a2-7c3e-7b4a-9f10-5c963f66afa6",
  "name": "Errors in Production",
  "query": "level=%5B%22Error%22%5D&env=Production&range=24h",
  "createdAt": "2026-11-04T10:15:00Z",
  "modifiedAt": null
}
```

- `name` has 1 to 100 characters, is trimmed, must be unique, and must not contain control characters. Names are compared exactly.
- `query` is the encoded query string of the log explorer at `/`, without the leading `?`, which is removed if sent. It has at most 4000 characters and no spaces, control characters or `#`. Logbook does not interpret it: the web interface opens `/?{query}`.

## API Keys

| Endpoint | Success | Description |
|---|---|---|
| `POST /api/v1/applications/{id}/api-keys` | `201` | Issue a new API Key; existing API Keys stay active. The body is optional. `400` if `expiresAt` is not in the future, `404` if the Application is unknown. |
| `POST /api/v1/applications/{id}/api-keys/{apiKeyId}/revoke` | `204` | Revoke. Revoking again succeeds and keeps the original time. `404` if the Application or API Key is unknown. |

The optional body of the first endpoint is `{"expiresAt": "2027-10-11T00:00:00Z"}`. Without it, the API Key does not expire. The response is the `apiKey` object shown above.

The plain-text `key` is returned **only once**, in the response that issues it, with `Cache-Control: no-store`. Logbook stores only its SHA-256 hash, so a lost API Key cannot be recovered: issue a new one and revoke the old one. A revoked or expired API Key stays listed for history, with `isActive` set to `false`.
