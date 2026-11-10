# Telemetry API

Besides Log Events, Logbook stores **spans**: measured operations of an Application. A span is either a *request* the Application received or a *dependency* it called. It also stores **custom metrics** the Application measures. This page describes the endpoints for spans, transactions, groups of exceptions, metrics and the overview of the dashboard. The [`Logbook.AspNetCore`](../how-to/track-requests.md) package sends spans for you, and the web interface reads them for you.

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

## Send metrics

`POST /api/v1/metrics`

Custom metrics arrive **aggregated**: one metric point holds the measurements of one metric with one set of dimensions over a short interval, as their count, sum, minimum and maximum. [`LogbookMetrics`](../how-to/track-metrics.md) in the SDK aggregates for you, by default per minute. The body is a JSON array with at least one metric point, and can be compressed with `Content-Encoding: gzip`:

```json
[
  {
    "timestamp": "2026-11-04T10:15:00Z",
    "name": "orders.placed",
    "count": 12,
    "sum": 1250.5,
    "min": 12.5,
    "max": 320,
    "environment": "Production",
    "dimensions": { "Region": "EU", "Shop": "A" }
  }
]
```

| Field | Required | Limit | Description |
|---|---|---|---|
| `timestamp` | yes | within the [accepted time window](log-api.md#limits) | When the interval of the measurements started, with an offset or `Z`. Stored in UTC. |
| `name` | yes | 200 characters | The name of the metric, with few distinct values, e.g. `orders.placed`. Compared exactly. |
| `count` | yes | at least 1 | The number of measurements. |
| `sum` | yes | a finite number | The sum of the measurements. |
| `min`, `max` | yes | finite, `min` not larger than `max` | The smallest and the largest measurement. |
| `environment` | no | 100 characters | A label to filter by. If it is missing or empty, `Unknown` is stored. |
| `dimensions` | no | 10 entries; names 100, values 500 characters | Strings to filter by, e.g. `{"Region": "EU"}`. Names are trimmed and must be unique and not empty. An entry without a value is left out. |

The responses, the handling of a batch as a whole, the rate limit and when metric points are stored are the same as for [sending Log Events](log-api.md#responses), with `{"accepted": 1}` for the number of metric points. Like spans, metric points have a queue of their own, with the limit `LogIngestion:MaxQueuedBytes`. Retention and deleting an Application cover them as they cover Log Events.

## Read a metric

`GET /api/v1/metrics?name=orders.placed`

Combines the metric points of one metric into buckets of equal width, for a chart.

| Parameter | Description |
|---|---|
| `name` | Required. The name of the metric, compared exactly. |
| `from` | The start of the range. Default: one day before `to`. |
| `to` | The end of the range. Default: now. Must be later than `from`, and at most 3650 days after it. |
| `applicationId` | Only metric points of this Application. |
| `environment` | Only metric points of this Environment, compared exactly. |
| `dimension` | Only metric points with this dimension, as `Name=Value`, e.g. `Region=EU`. Repeat it, up to 10 times, to require several. |

```json
{
  "name": "orders.placed",
  "from": "2026-11-03T10:00:00Z",
  "to": "2026-11-04T10:15:00Z",
  "bucketSeconds": 900,
  "buckets": [
    { "timestamp": "2026-11-03T10:00:00Z", "count": 120, "sum": 15400.5, "min": 12.5, "max": 320 },
    { "timestamp": "2026-11-03T10:15:00Z", "count": 0, "sum": 0, "min": null, "max": null }
  ]
}
```

- **Buckets** are 1, 5, 15 or 30 minutes, 1, 3, 6 or 12 hours, 1 day or 1 week wide: the narrowest that splits the range into at most 120 buckets. They are aligned to fixed points in time (UTC), so `from` is the start of the bucket that contains the requested start.
- **Every bucket** of the range is returned, also those without measurements, which have a `count` of 0 and no `min` or `max`.
- **The average** of a bucket is `sum` divided by `count`. Combining the points of several Applications, Environments or dimensions adds up their counts and sums.

## List metrics

`GET /api/v1/metrics/names`

Returns the names of the metrics with metric points in a time range, ordered by name, at most 1000. The parameters `from` (default: 7 days before `to`), `to`, `applicationId` and `environment` work as above.

```json
{ "items": [{ "name": "orders.placed", "lastSeen": "2026-11-04T10:15:00Z" }] }
```

`lastSeen` is the start of the interval of the latest metric point in the range.

## Overview

`GET /api/v1/overview`

What the dashboard shows: Log Events and requests over time, and the slowest requests. The parameters `from`, `to`, `applicationId` and `environment` work as for [reading a metric](#read-a-metric), with the same buckets. `slowest` is the number of slowest requests, from 0 to 50. Default: `10`.

```json
{
  "from": "2026-11-03T10:00:00Z",
  "to": "2026-11-04T10:15:00Z",
  "bucketSeconds": 900,
  "logEvents": [{ "timestamp": "2026-11-03T10:00:00Z", "total": 1520, "warnings": 12, "errors": 3 }],
  "requests": [{ "timestamp": "2026-11-03T10:00:00Z", "count": 840, "failed": 7 }],
  "requestCount": 20160,
  "failedRequestCount": 42,
  "slowestRequests": [
    {
      "applicationId": "0199d1a2-7c3e-7b4a-9f10-5c963f66afa6",
      "name": "GET /orders/{id}",
      "count": 840,
      "failed": 7,
      "averageDurationMs": 42.5,
      "p95DurationMs": 180,
      "maxDurationMs": 1250
    }
  ]
}
```

- `logEvents` and `requests` have every bucket of the range. `errors` counts the levels Error and Critical.
- Only spans of the kind `Request` count as requests. The error rate is `failedRequestCount` divided by `requestCount`.
- `slowestRequests` groups the requests of each Application by name, ordered by the 95th percentile of their duration, slowest first.
- The most frequent exceptions come from [`GET /api/v1/exceptions?sort=Count`](#find-groups-of-exceptions).
