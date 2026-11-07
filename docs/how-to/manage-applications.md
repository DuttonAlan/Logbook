# Manage Applications and API Keys

Every program that sends logs to Logbook is an Application, and it authenticates with an API Key. This guide creates an Application, rotates its API Key and removes it again.

The easiest way is the web interface: choose *Applications*, where you can create and rename Applications, issue and revoke API Keys, and delete an Application. The rest of this guide does the same with `curl`, for scripts. All endpoints and fields are listed in the [admin API reference](../reference/admin-api.md).

> An API Key can [send Log Events](../reference/log-api.md#send-log-events), which the SDK does for you, see [Connect an application](connect-application.md). Signing in lets you [search them](explore-logs.md).

## Prerequisites

- A running Logbook, e.g. from [Getting started](../tutorials/getting-started.md) or [Run Logbook from source](run-from-source.md).
- The password of the local administrator from the setting [`SignIn:Administrator:Password`](../reference/configuration.md#sign-in). In the shell where you run the commands below, sign in and keep the session in a file:

```sh
export LOGBOOK_URL=http://localhost:5080
export LOGBOOK_ADMIN_PASSWORD=…

curl --cookie-jar cookies.txt -X POST "$LOGBOOK_URL/api/v1/auth/sign-in" \
  -H "Content-Type: application/json" \
  -d "{\"userName\":\"admin\",\"password\":\"$LOGBOOK_ADMIN_PASSWORD\"}"
```

The file gives full access until the session ends, and the password does so for good. Only send them over HTTPS outside your own computer. Requests that change something also need the header `X-Requested-With`, as shown below.

## Create an Application

```sh
curl --cookie cookies.txt -X POST "$LOGBOOK_URL/api/v1/applications" \
  -H "X-Requested-With: curl" \
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
   curl --cookie cookies.txt -X POST "$LOGBOOK_URL/api/v1/applications/$APPLICATION_ID/api-keys" \
     -H "X-Requested-With: curl"
   ```

   To let it expire, send a body with `-H "Content-Type: application/json" -d '{"expiresAt":"2027-10-11T00:00:00Z"}'`. Without one, it stays valid until you revoke it.

2. Update your application to use the new API Key.

3. Revoke the old one. Its Id is in the response that issued it, and the metadata of all API Keys is in `GET /api/v1/applications/{id}`:

   ```sh
   curl --cookie cookies.txt -X POST "$LOGBOOK_URL/api/v1/applications/$APPLICATION_ID/api-keys/$API_KEY_ID/revoke" \
     -H "X-Requested-With: curl"
   ```

If an API Key has leaked, revoke it right away, even before the new one is in place.

## Change the retention

Log Events are kept for [`Retention:DefaultDays`](../reference/configuration.md#retention), by default 30 days. To keep those of one Application for a shorter or longer time, choose it in the web interface, or:

```sh
curl --cookie cookies.txt -X PUT "$LOGBOOK_URL/api/v1/applications/$APPLICATION_ID/retention" \
  -H "X-Requested-With: curl" \
  -H "Content-Type: application/json" \
  -d '{"retentionDays":14}'
```

With `null` instead of a number, the Application goes back to the default. The next retention run, by default within an hour, removes what is older, and this cannot be undone. Log Events the Application sends later with an older timestamp also go with the next run.

## Remove an Application

```sh
curl --cookie cookies.txt -X DELETE "$LOGBOOK_URL/api/v1/applications/$APPLICATION_ID" \
  -H "X-Requested-With: curl"
```

This also deletes its API Keys and all its Log Events, and cannot be undone. Log Events still waiting to be written are discarded as well. With many Log Events the request can take a while; if it is interrupted, for example by the timeout of a reverse proxy, send it again to finish deleting them.

## Change the password

Set [`SignIn:Administrator:Password`](../reference/configuration.md#sign-in) to a new value and restart Logbook. All sessions of the local administrator end right away; API Keys of Applications are not affected.
