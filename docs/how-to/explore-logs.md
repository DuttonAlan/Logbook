# Search and follow logs

The web interface is where you read your logs: search them, look into a single Log Event, and watch new ones arrive. It is served by Logbook itself, on the same address as the API.

## Prerequisites

- A running Logbook with some Log Events, e.g. from [Getting started](../tutorials/getting-started.md) or an application you have [connected](connect-application.md).
- A way to [sign in](configure-sign-in.md). Your session ends when you choose *Sign out*, when you close the browser, and at the latest after [`SignIn:SessionLifetime`](../reference/configuration.md#sign-in), by default 12 hours.

Only open the web interface over HTTPS outside your own computer, as your password and your logs travel over the connection.

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

The filters are part of the address, so you can reload the page or send the link to a colleague, who has to sign in to open it. *Refresh* searches again from now.

Times are shown in the time zone of your browser. Hover over a time to see the full date.

## Save a search

To run a search again with one click, choose **Save…** next to the filters, enter a name such as *Errors in Production*, and choose **Save**. The saved search keeps the text, the levels, the Application, the Environment, the properties and the time range. A relative time range such as the last 24 hours counts from when you open it again.

- **Open** a saved search from the list *Saved searches…* next to the button. The list shows the name of the saved search that matches the current filters.
- **Update** one by saving the current search under its name again: it replaces what it searches for.
- **Delete** one in the dialog of **Save…**.

Saved searches are shared by everyone who can sign in. See [Saved searches](../reference/admin-api.md#saved-searches).

## See the overview

**Overview** shows what is going on at a glance, for a time range and optionally one Application:

- the number of Log Events, errors and requests, and the share of failed requests,
- Log Events over time, with warnings and errors stacked on top,
- requests over time, with those that failed,
- the slowest requests, by the 95th percentile of their duration, and
- the most frequent exceptions.

Hover over a bar to see its values, or open *Show as a table*. Select a request to see its occurrences, or an exception to see its Log Events. The requests come from [request tracking](track-requests.md). Custom metrics have their own page, see [Track custom metrics](track-metrics.md).

## Read a Log Event

Select a row to open its details: the whole message, its template, the exception with its stack trace and inner exceptions, and the properties. With the row selected, the arrow keys move to the next and previous Log Event, and <kbd>Esc</kbd> closes the details.

- **Filter** next to a property adds it as a filter.
- **Open the transaction** shows the Log Event on the [timeline](track-requests.md#read-the-transactions) of its trace.
- **All occurrences of this exception** shows the Log Events of the same [group](track-requests.md#find-what-fails-most).
- **All Log Events of this trace** shows what belongs to the same trace, across all Applications and Environments. It replaces your other filters, because those could hide a part of the trace.
- **Copy as JSON** copies the whole Log Event.

## Follow new Log Events

Choose **Live tail** to see Log Events as they are stored, usually within a second or two. The search and all filters except the time range apply. It starts with the latest Log Events that match.

- To read something, **Pause** or simply scroll down: the list holds still, and a button counts what has arrived. Choose that button or **Resume** to catch up. Changing a filter also resumes, with the new results.
- If the connection is lost, the live tail says so and reconnects by itself, then fills in the latest Log Events.
- The browser keeps the latest 5000 Log Events of a live tail. For anything older, turn the live tail off and search.
- If Log Events arrive faster than they can be shown, a message says that some were skipped. They are stored all the same and can be searched.

With several Logbook servers, a live tail only shows what the server it is connected to has received. See [Follow Log Events](../reference/log-api.md#follow-log-events).
