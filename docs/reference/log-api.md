# Log API

The log API is what applications send their Log Events to. The [SDK](../explanation/architecture.md#the-sdk) will call it for you (milestone M2); until then, you can call it directly.

- **Authentication:** the API Key of an Application in the header `X-Logbook-Key`. Without an active API Key the endpoint returns `401`, whatever the body contains. The admin key is not accepted. To get an API Key, see [Manage Applications and API Keys](../how-to/manage-applications.md).
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
| `exception` | no | JSON object | Stored as sent, usually with `type`, `message`, `stackTrace` and `inner`. |
| `traceId`, `spanId` | no | 64 characters | The W3C identifiers of the trace and span. |
| `properties` | no | JSON object | Structured properties. Stored as sent, and can be nested. |

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
| `500` | An unexpected error. This includes the database being unavailable, because the API Key is checked there. |
| `503` | The queue is full, with a `Retry-After` header. None of the Log Events were accepted. |

A batch is accepted as a whole. **If one Log Event is not valid, none are accepted**, and the `400` response names the position of the first invalid one, counted from 0. Sending the same batch again does not help. After a `5xx` response, send it again later: no Log Event of it was accepted, so this creates no duplicates.

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

### Example

```sh
curl -X POST "$LOGBOOK_URL/api/v1/logs" \
  -H "X-Logbook-Key: $LOGBOOK_API_KEY" \
  -H "Content-Type: application/json" \
  -d "[{\"timestamp\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"level\":\"Information\",\"message\":\"Hello Logbook\",\"environment\":\"Development\"}]"
```
