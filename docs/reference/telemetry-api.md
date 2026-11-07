# Telemetry API

Besides Log Events, Logbook stores **spans**: measured operations of an Application. A span is either a *request* the Application received or a *dependency* it called. This page describes the endpoints for spans, for transactions, and for groups of exceptions. The [`Logbook.AspNetCore`](../how-to/track-requests.md) package sends spans for you, and the web interface reads them for you.

- **Authentication to send:** the API Key of an Application in the header `X-Logbook-Key`, as for [Log Events](log-api.md).
- **Authentication to read:** a session from [signing in](admin-api.md#sign-in). An API Key can only send.
- **Format:** JSON in and out. Errors are [problem details](https://www.rfc-editor.org/rfc/rfc9457).

## Send spans

`POST /api/v1/spans`

The body is a JSON array with at least one span, and can be compressed with `Content-Encoding: gzip`:

```json
[
  {
    "timestamp": "2026-11-04T10:15:00.123Z",
    "kind": "Dependency",
    "name": "GET payments.example.com",
    "durationMs": 42.5,
    "success": true,
    "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
    "spanId": "00f067aa0ba902b7",
    "parentSpanId": "53995c3f42cd8ad8",
    "environment": "Production",
    "statusCode": 200,
    "dependencyType": "HTTP",
    "target": "payments.example.com",
    "data": "https://payments.example.com/charge"
  }
]
```

| Field | Required | Limit | Description |
|---|---|---|---|
| `timestamp` | yes | within the [accepted time window](log-api.md#limits) | When the operation started, with an offset or `Z`. Stored in UTC. |
| `kind` | yes | | `Request` or `Dependency`. |
| `name` | yes | 500 characters | The name, with few distinct values, e.g. `GET /orders/{id}` rather than `GET /orders/42`. |
| `durationMs` | yes | not negative | How long the operation took, in milliseconds. |
| `success` | yes | | Whether the operation succeeded. |
| `traceId`, `spanId` | yes | 64 characters | The W3C identifiers of the trace and span. |
| `parentSpanId` | no | 64 characters | The span of the operation that caused this one. |
| `environment` | no | 100 characters | A label to filter by. If it is missing or empty, `Unknown` is stored. |
| `statusCode` | no | 32-bit integer | The status of the operation, e.g. the HTTP status. |
| `dependencyType` | no | 100 characters | The type of a dependency, e.g. `HTTP` or `SQL`. |
| `target` | no | 500 characters | What a dependency called, e.g. a host or a database. |
| `data` | no | 8192 characters | The details of the operation, e.g. a path or an SQL statement. |

The Application is always the one the API Key belongs to. The responses, the handling of a batch as a whole, the rate limit and when spans are stored are the same as for [sending Log Events](log-api.md#responses), with `{"accepted": 1}` for the number of spans.

Spans share the `LogIngestion` limits with Log Events, except that they have a queue of their own: `LogIngestion:MaxQueuedBytes` applies to each of the two, so a burst of requests cannot keep Log Events from being accepted.

## Search spans

`GET /api/v1/spans`

Returns one page of spans, newest first. All parameters are optional, and all filters must match.

| Parameter | Description |
|---|---|
| `from` | Only spans that started at or after this point in time. |
| `to` | Only spans that started before this point in time. Must be later than `from`. |
| `kind` | `Request` or `Dependency`. |
| `applicationId` | Only spans of this Application. |
| `environment` | Only spans of this Environment, compared exactly. |
| `name` | Only spans whose name contains this text, not case-sensitive. |
| `traceId` | Only spans of this trace, compared exactly. |
| `success` | `true` for only those that succeeded, `false` for only those that failed. |
| `minDurationMs` | Only spans that took at least this many milliseconds. |
| `limit` | Maximum number of spans on the page, from 1 to 1000. Default: `100`. |
| `cursor` | The `nextCursor` of the previous page. |

The response has `items`, with the fields above plus `id` and `applicationId`, and `nextCursor`. [Paging](log-api.md#paging) works as for Log Events.

| Status | Description |
|---|---|
| `200` | The page. `items` is empty if nothing matches. |
| `400` | A parameter is not valid, e.g. an unknown `kind` or an empty time range. |
| `401` | There is no valid session. |
| `503` | The database cannot be reached, with a `Retry-After` header in seconds. |

## Get a transaction

`GET /api/v1/traces/{traceId}`

Returns everything stored for one trace, across all Applications and Environments, oldest first. This is what the timeline in the web interface shows.

```json
{
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "spans": [ { "name": "GET /orders/{id}", "kind": "Request", "durationMs": 42.5, "…": "…" } ],
  "logEvents": [ { "message": "Order 42 failed", "level": "Error", "…": "…" } ],
  "isTruncated": false
}
```

The response holds at most 1000 spans and 1000 Log Events. If a trace has more, `isTruncated` is `true`, and the latest are left out, as what a trace started with explains the rest.

| Status | Description |
|---|---|
| `200` | The trace. |
| `400` | The Trace Id is longer than 64 characters. |
| `401` | There is no valid session. |
| `404` | Nothing is stored for this trace. |
| `503` | The database cannot be reached, with a `Retry-After` header in seconds. |

## Find groups of exceptions

`GET /api/v1/exceptions`

Returns the exceptions in the stored Log Events, grouped per Application by type and stack fingerprint.

| Parameter | Description |
|---|---|
| `from` | Only count occurrences at or after this point in time. |
| `to` | Only count occurrences before this point in time. Must be later than `from`. |
| `applicationId` | Only groups of this Application. |
| `environment` | Only occurrences in this Environment, compared exactly. |
| `sort` | `LastOccurrence`, the default, or `Count`. |
| `limit` | Maximum number of groups, from 1 to 200. Default: `50`. There is no paging. |

```json
{
  "items": [
    {
      "applicationId": "0199d1a2-7c3e-7b4a-9f10-5c963f66afa6",
      "fingerprint": "9f86d081884c7d659a2feaa0c55ad015",
      "count": 17,
      "firstOccurrence": "2026-11-01T08:00:00Z",
      "lastOccurrence": "2026-11-04T10:15:00.123Z",
      "exception": { "type": "System.InvalidOperationException", "message": "…", "stackTrace": "…", "inner": null }
    }
  ]
}
```

- `count` and `lastOccurrence` cover the requested time range.
- `firstOccurrence` is the earliest occurrence still stored, also if it is before the range, because it answers whether a failure is new. Retention moves it forward.
- `exception` is the exception of the latest occurrence.

To see each occurrence, [search the Log Events](log-api.md#search-log-events) with `exceptionFingerprint` and `applicationId`.

### What makes two exceptions the same

The fingerprint is computed when a Log Event is received, from its `exception`:

- **The `type`** of the exception.
- **The methods in `stackTrace`**, at most the first 64. File names, line numbers, the language of the stack trace and the numbers the compiler adds to lambdas and async methods do not count, so a new build keeps its groups.
- **The types of the inner exceptions**, in any order, but not their stacks.

The message does not count, as it often contains values such as an Id. An `exception` with neither a `type` nor a `stackTrace` belongs to no group. Log Events stored before this feature have no fingerprint, and are not grouped.
