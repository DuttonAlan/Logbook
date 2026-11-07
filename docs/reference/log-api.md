# Log API

The log API is what applications send their Log Events to and what you search them with. The [SDK](../explanation/architecture.md#the-sdk) sends for you, and the [web interface](../how-to/explore-logs.md) searches and follows for you. You can also call it directly.

- **Authentication to send:** the API Key of an Application in the header `X-Logbook-Key`. Without an active API Key the endpoint returns `401`, whatever the body contains. A session is not accepted. To get an API Key, see [Manage Applications and API Keys](../how-to/manage-applications.md).
- **Authentication to search and follow:** a session from [signing in](admin-api.md#sign-in). An API Key can only send, so one that leaks from an application cannot read any logs.
- **Format:** JSON in and out. Errors are [problem details](https://www.rfc-editor.org/rfc/rfc9457).
- **Schema:** the OpenAPI document and Swagger UI (`/swagger`) are served in `Development`.

## Send Log Events

`POST /api/v1/logs`

The body is a JSON array with at least one Log Event:

```json
[
  {
    "timestamp": "2026-11-04T10:15:00.123Z",
    "level": "Error",
    "messageTemplate": "Order {OrderId} failed",
    "message": "Order 42 failed",
    "category": "Shop.Orders.OrderService",
    "environment": "Production",
    "eventId": 4001,
    "exception": { "type": "System.InvalidOperationException", "message": "…", "stackTrace": "…", "inner": null },
    "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
    "spanId": "00f067aa0ba902b7",
    "properties": { "OrderId": 42, "UserId": "abc" }
  }
]
```

| Field | Required | Limit | Description |
|---|---|---|---|
| `timestamp` | yes | within the [accepted time window](#limits) | When the Log Event occurred, with an offset or `Z`. Without one, it is read in the time zone of the server. Stored in UTC. |
| `level` | yes | | `Trace`, `Debug`, `Information`, `Warning`, `Error` or `Critical`. Not case-sensitive. |
| `message` | yes | 65,536 characters | The rendered message. Can be empty. Searched by full-text search. |
| `messageTemplate` | no | 65,536 characters | The message before rendering. |
| `category` | no | 500 characters | The category of the logger, usually a class name. |
| `environment` | no | 100 characters | A label to filter by, e.g. `Production`. If it is missing or empty, `Unknown` is stored. Not a security boundary. |
| `eventId` | no | 32-bit integer | The numeric Id of the event within its category. |
| `exception` | no | JSON object, 29 levels | Stored as sent, usually with `type`, `message`, `stackTrace` and `inner`. Objects and arrays both count as levels. |
| `traceId`, `spanId` | no | 64 characters | The W3C identifiers of the trace and span. |
| `properties` | no | JSON object, 29 levels | Structured properties. Stored as sent, and can be nested. Objects and arrays both count as levels. |

The Application is always the one the API Key belongs to. An `applicationId` in the body, like any unknown field, is ignored.

PostgreSQL cannot store everything that is valid JSON, so some values are stored differently instead of failing the request:

- The character U+0000 becomes U+FFFD in all text, including property names.
- In `exception` and `properties`, a number that is very long or outside the range of a 64-bit floating-point number is stored as a string with the same digits.

### Compression

Send `Content-Encoding: gzip` to compress the body. No other encoding is supported.

### Responses

| Status | Description |
|---|---|
| `202` | Accepted, with the number of Log Events: `{"accepted": 1}`. They are written to the database shortly afterwards, see [below](#when-log-events-are-stored). |
| `400` | The body is not valid: malformed JSON or gzip, an empty array, or a Log Event that breaks a rule above. |
| `401` | The API Key is missing, unknown, revoked or expired. |
| `413` | The body is larger than `LogIngestion:MaxRequestBytes`, before or after decompression. |
| `415` | The body is not JSON, or the content encoding is not gzip. |
| `429` | Too many requests from this API Key, or too many invalid API Keys from this IP address, with a `Retry-After` header in seconds. None of the Log Events were accepted. See [rate limit](#rate-limit). |
| `500` | An unexpected error. |
| `503` | The queue is full, or the database cannot be reached to check the API Key, with a `Retry-After` header in seconds. None of the Log Events were accepted. |

A batch is accepted as a whole. **If one Log Event is not valid, none are accepted**, and the `400` response names the position of the first invalid one, counted from 0. Sending the same batch again does not help. After a `429` or `5xx` response, send it again later: no Log Event of it was accepted, so this creates no duplicates.

### When Log Events are stored

`202` means Logbook has taken responsibility for the Log Events, not that they are in the database yet. They wait in memory and are written in batches, normally within about a second, so they can be searched shortly after that.

- **The database is unavailable:** writing is retried until it works. Accepted Log Events are kept, and nothing is lost if Logbook keeps running.
- **Logbook stops:** it writes what is left within the shutdown timeout of the host (30 seconds by default). Whatever could not be written is lost, and the number is logged as an error. Give the container at least that long to stop: Docker's default is only 10 seconds.
- **Logbook crashes or is killed:** Log Events that were not written yet are lost.
- **PostgreSQL refuses a single Log Event:** only that one is dropped, and its Id is logged as an error. This should not happen, because values that PostgreSQL cannot store are replaced when they arrive.

### Limits

Set with the [`LogIngestion` settings](configuration.md#server):

| Limit | Default |
|---|---|
| Size of the request body, before and after decompression | 5 MiB |
| Size of one Log Event: `message`, `messageTemplate`, `exception` and `properties` together | 256 KiB (`400` if exceeded) |
| Accepted time window of `timestamp` | from 7 days in the past to 1 day in the future |
| Memory for Log Events that are not written yet | 256 MiB (`503` if exceeded) |

The time window exists because Logbook stores each day in its own partition, so a wrong clock could otherwise create any number of tables.

### Rate limit

Each API Key can send 6000 requests per minute, set with [`RateLimiting:ApiKeyPermitsPerMinute`](configuration.md#server). That is about 10,000 Log Events per second with batches of 100, so the limit is meant to stop a misbehaving program rather than to restrict normal use.

- The limit counts requests, not Log Events, and every authenticated request counts, also one that is then rejected as invalid. To send more, put more Log Events in a batch.
- A burst of up to the full limit is allowed after the API Key has not been used for a while. The allowance refills continuously, so after a burst an application can go on at the allowed rate rather than wait for a minute to end.
- Each API Key has its own limit, also within one Application.
- The limit applies per server: with several servers, an API Key can send that many requests to each one.
- Searching and following are not limited.
- A rejected API Key is logged as a warning with its Id, at most once per minute.

Invalid API Keys have a separate limit, because each one costs a lookup in the database: 600 per minute for each IP address, set with [`RateLimiting:InvalidApiKeyPermitsPerMinute`](configuration.md#server). Valid API Keys do not use it up, but each one holds a place in it while it is being checked, so parallel requests cannot pass the limit together. Above the limit, **all** requests with an API Key from that IP address get `429` until the allowance refills, because an API Key cannot be checked without the lookup. Behind a reverse proxy, all clients share the address of the proxy unless [forwarded headers](../how-to/reverse-proxy.md#tell-logbook-about-the-proxy) are set up, so one client flooding Logbook with invalid API Keys would also block the others. The IP address is logged as a warning, at most once per minute.

### Example

```sh
curl -X POST "$LOGBOOK_URL/api/v1/logs" \
  -H "X-Logbook-Key: $LOGBOOK_API_KEY" \
  -H "Content-Type: application/json" \
  -d "[{\"timestamp\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"level\":\"Information\",\"message\":\"Hello Logbook\",\"environment\":\"Development\"}]"
```

## Search Log Events

`GET /api/v1/logs`

Returns one page of Log Events, newest first. All parameters are optional, and all filters must match.

| Parameter | Description |
|---|---|
| `from` | Only Log Events at or after this point in time, with an offset or `Z`. |
| `to` | Only Log Events before this point in time. Must be later than `from`. |
| `level` | Only Log Events with this level. Repeat it for several levels: `level=Error&level=Critical`. |
| `applicationId` | Only Log Events of this Application. |
| `environment` | Only Log Events of this Environment, compared exactly. `Unknown` finds Log Events sent without one. At most 100 characters. |
| `text` | Only Log Events whose message matches this [full-text search](#full-text-search). At most 500 characters. |
| `traceId` | Only Log Events of this trace, compared exactly. At most 64 characters. |
| `property` | Only Log Events with this [property](#property-filters), as `Name=Value`. Repeat it for several, which must all match. At most 10. |
| `exceptionFingerprint` | Only Log Events with an exception of this [group](telemetry-api.md#find-groups-of-exceptions), compared exactly. At most 32 characters. |
| `limit` | Maximum number of Log Events on the page, from 1 to 1000. Default: `100`. |
| `cursor` | The `nextCursor` of the previous page. |

URL-encode the values. In particular, a `+` in an offset must be sent as `%2B`, or it is read as a space and rejected.

The response contains the Log Events with the fields [listed above](#send-log-events), where `properties` and `exception` are JSON objects, plus `id`, `applicationId` and `exceptionFingerprint`, which is null without an exception:

```json
{
  "items": [
    {
      "id": "0199d1a2-7c3e-7b4a-9f10-7c963f66afa6",
      "applicationId": "0199d1a2-7c3e-7b4a-9f10-5c963f66afa6",
      "timestamp": "2026-11-04T10:15:00.123Z",
      "level": "Error",
      "message": "Order 42 failed",
      "messageTemplate": "Order {OrderId} failed",
      "category": "Shop.Orders.OrderService",
      "environment": "Production",
      "eventId": 4001,
      "exception": { "type": "System.InvalidOperationException", "message": "…", "stackTrace": "…", "inner": null },
      "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
      "spanId": "00f067aa0ba902b7",
      "properties": { "OrderId": 42, "UserId": "abc" }
    }
  ],
  "nextCursor": "AN4cD2u1QAABmdGifD57Sp8QfJY_Zq-m"
}
```

| Status | Description |
|---|---|
| `200` | The page. `items` is empty if nothing matches. An unknown `applicationId` is not an error. |
| `400` | A parameter is not valid, e.g. an unknown `level`, a `limit` out of range, an empty time range, a `property` without a name, or a `cursor` that Logbook did not return. |
| `401` | There is no valid session. |
| `503` | The database cannot be reached, with a `Retry-After` header in seconds. |

### Paging

If `nextCursor` is not null, there are more Log Events: send the same request again with `cursor` set to it. Treat the value as opaque, as its format can change. There is no total count, because counting would have to read every matching Log Event.

Paging is stable: Log Events that arrive while you page do not shift the results, so you see no duplicates and do not miss any that were already stored. New Log Events appear when you start again from the first page. A Log Event that arrives late, with a timestamp older than your position, is not in the pages you have left.

### Full-text search

`text` searches for whole words in `message`, not case-sensitive. It does not stem words or match parts of them, because logs are full of identifiers and mix languages: `fail` does not find `failed`.

| Input | Finds messages with |
|---|---|
| `order failed` | both words, in any order |
| `"order 42"` | this exact phrase |
| `payment or shipped` | either word |
| `order -failed` | `order`, but not `failed` |

Any input is valid. Characters that have no meaning in a search are ignored, so input with nothing to search for, such as `!!!`, finds nothing. Empty input is treated as no filter.

### Property filters

`property=OrderId=42` finds Log Events whose `properties` have this value, with the `=` URL-encoded as `%3D` where your client does not do that for you. The name ends at the first `=`, so a value can contain one, but a name cannot. It finds only top-level properties, and the whole value must match, not a part of it.

The value is text in the URL, so it matches any JSON type with the same meaning: `OrderId=42` finds both `"OrderId": 42` and `"OrderId": "42"`, and `Active=true` finds both `true` and `"true"`. It cannot find `null`, objects or arrays. Numbers compare by value, so `42.0` also finds `42`, and `1e3` finds `1000`. A value that is not a JSON number, such as `.5` or `007`, only finds that exact text.

### Example

The file `cookies.txt` comes from [signing in with a script](../how-to/configure-sign-in.md#scripts).

```sh
curl --cookie cookies.txt -G "$LOGBOOK_URL/api/v1/logs" \
  --data-urlencode "from=2026-11-04T00:00:00Z" \
  --data-urlencode "level=Error" \
  --data-urlencode "level=Critical" \
  --data-urlencode "environment=Production" \
  --data-urlencode 'text=order -shipped' \
  --data-urlencode "limit=50"
```

## Follow Log Events

`GET /api/v1/logs/stream`

Sends each Log Event as soon as it is stored, as [server-sent events](https://html.spec.whatwg.org/multipage/server-sent-events.html). It accepts the filters of the [search](#search-log-events), except `from`, `to`, `cursor` and `limit`, and they mean exactly the same. It requires a session, and sends no past Log Events: to fill a list, connect first and search when `connected` arrives, which leaves no gap, and expect some Log Events twice.

| Event | Data | Meaning |
|---|---|---|
| `connected` | `{}` | Following has started. Everything stored from now on that matches is sent. |
| `log` | A Log Event, as in the [search](#search-log-events) | A Log Event was stored. |
| `gap` | `{}` | Log Events were skipped, because they arrive faster than they can be sent. They are stored, and can be searched. |
| `heartbeat` | `{}` | Nothing else was sent for 15 seconds, and the connection is alive. |

```text
event: connected
data: {}

event: log
data: {"id":"0199d1a2-7c3e-7b4a-9f10-7c963f66afa6","level":"Error","message":"Order 42 failed", …}
```

| Status | Description |
|---|---|
| `200` | The stream, as `text/event-stream`. It ends when the server stops or the session expires, so connect again after that. |
| `400` | A parameter is not valid, see the [search](#search-log-events). |
| `401` | There is no valid session. |

- **Order:** Log Events are sent in the order they are stored, which is not always the order of their timestamps. A Log Event that arrives late is still sent. Within a batch, the oldest comes first.
- **Limit:** at most 500 Log Events per written batch, the newest ones. A `gap` tells when there were more.
- **Reconnecting:** the stream has no way to continue where it stopped, so search for what you may have missed.
- **Several servers:** a stream only sends what the server it is connected to has written, so behind a load balancer it needs sticky sessions, and it only shows the part of the logs that this server received.
- **Browsers:** `EventSource` works, as the session is a cookie. The web interface reads the response with `fetch`, which also tells it the status when connecting fails.

Each stream looks up every written batch in the database, usually once per second, so it costs one short query per batch while it is connected.

```sh
curl --cookie cookies.txt -N -G "$LOGBOOK_URL/api/v1/logs/stream" \
  --data-urlencode "level=Error"
```
