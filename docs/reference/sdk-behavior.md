# SDK behavior

What the `ILogger` provider turns a call into, what `Logbook.Sdk` does with each response of the server, with events that break a limit, and when the application shuts down, and how to follow all of this through its diagnostics. The settings are listed in [Configuration](configuration.md#sdk).

The client never throws into the application. It only makes the application wait while it is disposed, for at most `ShutdownTimeout`.

## From `ILogger` to an event

| Field | Comes from |
|---|---|
| `timestamp` | The time of the call. |
| `level` | The `LogLevel`. |
| `message` | The rendered message. |
| `messageTemplate` | The template of the call, e.g. `Order {OrderId} failed`. Also for source-generated `[LoggerMessage]` methods. |
| `category` | The category of the logger. |
| `environment` | The option `Environment`, otherwise the name from `IHostEnvironment`. |
| `eventId` | The Id of the `EventId`. An Id of 0 is left out. |
| `traceId`, `spanId` | `Activity.Current`, if it has W3C identifiers. |
| `exception` | The exception, see [below](#exceptions). |
| `properties` | The values of the call and of its scopes, see [below](#properties). |

Logs of the categories `Logbook.Sdk`, `Logbook.Extensions.Logging`, `Logbook.AspNetCore` and `System.Net.Http.HttpClient`, each including what is below them, are never sent, so that sending cannot create more to send. This also excludes the logs of the other `HttpClient`s of the application.

### Properties

| Source | Property |
|---|---|
| The named values of the call | One property for each. |
| A scope with named values, e.g. a dictionary or `BeginScope("Order {OrderId}", 42)` | One property for each. |
| A scope with text, e.g. `BeginScope("Import")` or the text of a scope with a template | Listed in the array `Scopes`, outermost first. |
| The name of the `EventId` | `EventName`. |

For values of the same name, the call wins over the innermost scope, and that one over the outer scopes. `Scopes` and `EventName` never replace a value of the application.

Values are captured during the call, so later changes to an object are not sent. Text, numbers, booleans, `null`, `DateTime`, `DateTimeOffset` and `Guid` keep their JSON type. Dictionaries become objects and other collections become arrays, up to 100 items and 5 levels deep, and a collection deeper than that is sent as text. One event reads at most 1000 values from the call, 1000 from all scopes together, and 10,000 items from all collections together. Its values hold at most two times `MaxEventBytes` of text, and those after that are left out. This keeps a call to the logger cheap, as it runs on the thread of your application. Everything else, including enums, is sent as text using `ToString`, formatted with the invariant culture. Objects are never read through reflection, because that would run their getters and could send secrets nobody meant to log.

### Exceptions

```json
{
  "type": "System.InvalidOperationException",
  "message": "…",
  "stackTrace": "…",
  "inner": { "type": "System.ArgumentException", "message": "…", "stackTrace": "…", "inner": null }
}
```

An `AggregateException` has the array `innerExceptions` instead of `inner`. At most 32 exceptions and 8 levels are sent. If a member of an exception or a value throws, the rest of the event is still sent.

If any of these limits is reached, or text in a value or an exception or the name of a value is over `MaxMessageLength`, the event is marked as [truncated](#events-over-a-limit).

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
| Event over `MaxEventBytes`, counting `exception` and `properties` as the server stores them | All long text is cut to one limit, including strings inside `exception` and `properties`, and the event is marked as truncated. The limit is lowered until the event fits, so short text stays whole. |
| Event still over `MaxEventBytes`, e.g. because of many small properties | Dropped. |
| JSON of the event over `MaxRequestBytes` | Dropped. |
| Timestamp older than `MaxEventAge` or further ahead than `MaxFutureOffset`, with a margin for clocks that differ of 5 minutes, or half of the setting if that is less | Dropped. With `MaxFutureOffset` at 0, there is no margin, so an application whose clock is ahead of the server can lose batches. |
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
