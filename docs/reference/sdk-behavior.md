# SDK behavior

What `Logbook.Sdk` does with each response of the server, with events that break a limit, and when the application shuts down, and how to follow all of this through its diagnostics. The settings are listed in [Configuration](configuration.md#sdk).

The client never throws into the application. It only makes the application wait while it is disposed, for at most `ShutdownTimeout`.

## Responses

| Response | What the client does | Next attempt |
|---|---|---|
| `2xx` | The events leave the buffer. | Right away if a batch is due. |
| `400`, `413`, `415`, `422` | The batch is dropped, as sending it again would fail again. | Right away if a batch is due. |
| `408`, `429`, `5xx` | The events stay in the buffer. | After the backoff. |
| No connection, or no answer within `RequestTimeout` | The events stay in the buffer. | After the backoff. |
| `401` and everything else, including `403`, `404` and redirects | The events stay in the buffer, as this needs someone to fix the API Key or the address. Reported as an error. | After about a minute. |

Redirects are not followed, because a redirected POST turns into a GET whose `200` would look like delivered events.

The backoff starts at 1 second and doubles with every failure up to 30 seconds, with the actual delay between half and all of that value at random. A `Retry-After` header is respected as a minimum, up to 5 minutes. Once the server has accepted or rejected a batch, the backoff starts over.

While the server cannot be reached, nothing but time is lost: a failed batch stays in the buffer until the buffer is full, and then the oldest events are dropped first. Every attempt starts from the oldest events that are left.

## Events over a limit

The server rejects a whole batch for a single event it does not accept, so the client checks each event before sending it.

| Problem | What the client does |
|---|---|
| `message` or `messageTemplate` over `MaxMessageLength` | Cut, and marked as truncated. |
| `category` over 500 characters, `environment` over 100, `traceId` or `spanId` over 64 | Cut, and marked as truncated. |
| Event over `MaxEventBytes` | All long text is cut to one limit, including strings inside `exception` and `properties`, and the event is marked as truncated. The limit is lowered until the event fits, so short text stays whole. |
| Event still over `MaxEventBytes`, e.g. because of many small properties | Dropped. |
| JSON of the event over `MaxRequestBytes` | Dropped. |
| Timestamp older than `MaxEventAge` or further ahead than `MaxFutureOffset`, with a margin of 5 minutes for clocks that differ | Dropped. |
| `exception` or `properties` that is not a JSON object, is nested more than 29 levels deep, or contains invalid text | The value is left out, and the event is sent without it. |

An event marked as truncated has the property `logbook.truncated` set to `true`.

## Shutdown

Disposing the client ends all waiting, including a backoff and the `FlushInterval`, and sends what is left in batches. A request that is already under way may finish. One failed attempt ends this, as there is no point in using up the time of the shutdown while the server is down. After `ShutdownTimeout`, a request under way is canceled. Events that could not be sent are lost and reported.

Give the application enough time to stop. Docker's default is 10 seconds, and the host of an ASP.NET Core application allows 30 seconds by default.

## Diagnostics

The SDK reports through an `EventSource` named `Logbook-Sdk`, not through `ILogger`, as it would otherwise send its own logs to itself. To follow it, use for example `dotnet-trace collect --providers Logbook-Sdk` or an `EventListener` in the application. Only numbers, reasons and the names of exception types are written: never the API Key or the content of an event.

| Event | Level | Payload | Meaning |
|---|---|---|---|
| `ClientDisabled` | Error | `cause` | The client ignores all events, because of the named setting or exception. |
| `BatchSent` | Verbose | `eventCount`, `compressedBytes` | The server accepted a batch. |
| `RequestFailed` | Warning | `error`, `eventCount`, `retryDelayMilliseconds` | A temporary failure, with the status code or the name of the exception. |
| `RequestRefused` | Error | `statusCode`, `eventCount`, `retryDelayMilliseconds` | The server refused the request, e.g. with `401` for a wrong or revoked API Key. |
| `BatchRejected` | Error | `statusCode`, `eventCount` | The server rejected a batch, and it was dropped. |
| `EventsDropped` | Warning | `reason`, `eventCount` | Events were dropped. The reason is `BufferFull`, `TooLarge`, `Expired` or `Shutdown`. |
| `EventsTruncated` | Warning | `eventCount` | Events were delivered with their text cut. |
