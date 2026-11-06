# Search and follow logs

The web interface is where you read your logs: search them, look into a single Log Event, and watch new ones arrive. It is served by Logbook itself, on the same address as the API.

## Prerequisites

- A running Logbook with some Log Events, e.g. from [Getting started](../tutorials/getting-started.md) or an application you have [connected](connect-application.md).
- The admin key from the setting [`AdminApi:Key`](../reference/configuration.md#server). The web interface asks for it once per tab, and forgets it when you close the tab or choose *Forget admin key*. Sign-in replaces it in milestone M4.

Only open the web interface over HTTPS outside your own computer, as the admin key and your logs travel with every request.

## Find Log Events

The list shows the newest Log Events first and loads more as you scroll. All filters are combined:

| Filter | What it does |
|---|---|
| Search messages | [Full-text search](../reference/log-api.md#full-text-search) for whole words: `order failed`, `"exact phrase"`, `a or b`, `-excluded`. `fail` does not find `failed`. |
| Time range | From the last 15 minutes to all time, or a custom range in your local time. The default is the last 24 hours. |
| Levels | Choose one or several. None chosen means all. |
| Application | Only the Log Events of this Application. |
| Environment | Compared exactly, e.g. `Production`. The field suggests the Environments in what you have seen so far. |
| Property | A [top-level property](../reference/log-api.md#property-filters), as `Name=Value`, e.g. `OrderId=42`. Add several to require all of them. |

The filters are part of the address, so you can reload the page or send the link to a colleague, who needs the admin key to open it. *Refresh* searches again from now.

Times are shown in the time zone of your browser. Hover over a time to see the full date.

## Read a Log Event

Select a row to open its details: the whole message, its template, the exception with its stack trace and inner exceptions, and the properties. With the row selected, the arrow keys move to the next and previous Log Event, and <kbd>Esc</kbd> closes the details.

- **Filter** next to a property adds it as a filter.
- **All Log Events of this trace** shows what belongs to the same trace, across all Applications and Environments. It replaces your other filters, because those could hide a part of the trace.
- **Copy as JSON** copies the whole Log Event.

## Follow new Log Events

Choose **Live tail** to see Log Events as they are stored, usually within a second or two. The search and all filters except the time range apply. It starts with the latest Log Events that match.

- To read something, **Pause** or simply scroll down: the list holds still, and a button counts what has arrived. Choose that button or **Resume** to catch up.
- If the connection is lost, the live tail says so and reconnects by itself, then fills in the latest Log Events.
- The browser keeps the latest 5000 Log Events of a live tail. For anything older, turn the live tail off and search.
- If Log Events arrive faster than they can be shown, a message says that some were skipped. They are stored all the same and can be searched.

With several Logbook servers, a live tail only shows what the server it is connected to has received. See [Follow Log Events](../reference/log-api.md#follow-log-events).
