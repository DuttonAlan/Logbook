# Manage Applications and API Keys

Every program that sends logs to Logbook is an Application, and it authenticates with an API Key. This guide creates an Application, rotates its API Key and removes it again with `curl`. All endpoints and fields are listed in the [admin API reference](../reference/admin-api.md).

> An API Key can [send Log Events](../reference/log-api.md#send-log-events), which the SDK does for you, see [Connect an application](connect-application.md). The admin key can [search them](../reference/log-api.md#search-log-events), but only through the API so far; see the [roadmap](../roadmap.md).

## Prerequisites

- A running Logbook, e.g. from [Getting started](../tutorials/getting-started.md) or [Run Logbook from source](run-from-source.md).
- The admin key from the setting [`AdminApi:Key`](../reference/configuration.md#server). In the shell where you run the commands below:

```sh
export LOGBOOK_URL=http://localhost:5080
export LOGBOOK_ADMIN_KEY=…
```

The admin key gives full access to the admin API. Only send it over HTTPS outside your own computer.

## Create an Application

```sh
curl -X POST "$LOGBOOK_URL/api/v1/applications" \
  -H "Authorization: Bearer $LOGBOOK_ADMIN_KEY" \
  -H "Content-Type: application/json" \
  -d '{"name":"Shop Web App"}'
```

The response contains the Id of the Application and its first API Key:

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

Store `apiKey.key` now, e.g. in the secret store of your application. **It is shown only once**, because Logbook keeps only its hash.

## Rotate an API Key

An Application can have several active API Keys, so you can replace one without losing logs.

1. Issue a new API Key:

   ```sh
   curl -X POST "$LOGBOOK_URL/api/v1/applications/$APPLICATION_ID/api-keys" \
     -H "Authorization: Bearer $LOGBOOK_ADMIN_KEY"
   ```

   To let it expire, send a body with `-H "Content-Type: application/json" -d '{"expiresAt":"2027-10-11T00:00:00Z"}'`. Without one, it stays valid until you revoke it.

2. Update your application to use the new API Key.

3. Revoke the old one. Its Id is in the response that issued it, and the metadata of all API Keys is in `GET /api/v1/applications/{id}`:

   ```sh
   curl -X POST "$LOGBOOK_URL/api/v1/applications/$APPLICATION_ID/api-keys/$API_KEY_ID/revoke" \
     -H "Authorization: Bearer $LOGBOOK_ADMIN_KEY"
   ```

If an API Key has leaked, revoke it right away, even before the new one is in place.

## Remove an Application

```sh
curl -X DELETE "$LOGBOOK_URL/api/v1/applications/$APPLICATION_ID" \
  -H "Authorization: Bearer $LOGBOOK_ADMIN_KEY"
```

This also deletes its API Keys and all its Log Events, and cannot be undone. Log Events still waiting to be written are discarded as well. With many Log Events the request can take a while; if it is interrupted, for example by the timeout of a reverse proxy, send it again to finish deleting them.

## Change the admin key

Set `AdminApi:Key` to a new value and restart Logbook. The old key stops working right away; API Keys of Applications are not affected.
