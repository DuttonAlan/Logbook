# Track custom metrics

Send numbers your application measures, such as orders placed, items in a queue or the size of a payload, and chart them in Logbook. The SDK aggregates the measurements in your application and sends one metric point per metric and minute, so measuring often costs almost nothing.

## Prerequisites

- An application that sends its logs with `Logbook.Extensions.Logging` or `Logbook.AspNetCore`, see [Connect an application](connect-application.md).

## Record a measurement

`AddLogbook()` registers `LogbookMetrics` in dependency injection. Inject it and record:

```csharp
public sealed class OrderService(LogbookMetrics metrics)
{
    public void Place(Order order)
    {
        // …
        metrics.Record("orders.placed", 1, ("Region", order.Region));
        metrics.Record("orders.amount", order.Total, ("Region", order.Region), ("Currency", order.Currency));
    }
}
```

- **The name** says what is measured. Keep the number of names small, and put what varies into dimensions.
- **Dimensions** are what you can filter by later, such as a region or a status. Every combination is a series of its own, and the SDK keeps at most 1000 series per minute, so never use an Id, a user name or other values with many possibilities.
- **The value** can be a count, such as 1 for each order, or an amount, such as a duration or a size. Logbook keeps the count, sum, minimum and maximum, so the chart can show either.

Without dependency injection, create `new LogbookMetrics(client, options, environment)` next to your `LogbookClient`, and dispose it before the client.

## Collect System.Diagnostics.Metrics

If your application or a library already uses [`System.Diagnostics.Metrics`](https://learn.microsoft.com/dotnet/core/diagnostics/metrics-instrumentation), name the meters in the setting `Logbook:Meters`:

```json
{
  "Logbook": {
    "Meters": ["Shop.*", "Microsoft.AspNetCore.Hosting"]
  }
}
```

Counters, histograms and gauges of those meters arrive as metrics named like the instrument, with their tags as dimensions. Observable instruments are read once a minute. A name ending in `*` matches every meter that starts with the rest.

To send more or less often, change `Logbook:MetricInterval`, for example to `00:00:10`. See [Configuration](../reference/configuration.md#sdk).

## See the metrics

In the web interface, open **Metrics**. Choose the metric, how to combine the measurements of each interval (average, sum, count, minimum or maximum), the time range and, optionally, an Application. Add a dimension filter as `Name=Value`, e.g. `Region=EU`, to see only that part. Hover over a bar to see its exact value, or open *Show as a table*.

The address keeps everything you chose, so you can bookmark a chart or send it to a colleague.

## If nothing arrives

- Measurements are sent at the end of each minute, so wait for the next one.
- The metric list only shows metrics with points in the chosen time range.
- A metric recorded with a value that is not a finite number, or with an empty name, is ignored.
- The `EventSource` named `Logbook-Sdk` reports when a series was dropped because of the limit of 1000, and every problem sending. See [SDK behavior](../reference/sdk-behavior.md#diagnostics).
