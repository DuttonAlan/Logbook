// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Diagnostics;
using System.Globalization;

using Logbook.Sdk;

using Microsoft.Extensions.Hosting;

namespace Logbook.AspNetCore;

/// <summary>
///     Listens to the activities of the application for as long as it runs, and records its calls to dependencies.
/// </summary>
/// <remarks>
///     <para>
///         HttpClient and database drivers announce their calls through an <see cref="ActivitySource"/>, so tracking them
///         needs no change to the application or to how its clients are created. The tags are those of the OpenTelemetry
///         semantic conventions, including the older names database drivers still use.
///     </para>
///     <para>
///         Listening also makes ASP.NET Core and HttpClient start activities in the first place, and with them the Trace Id
///         that connects a request to its dependencies and Log Events, also across applications.
///     </para>
/// </remarks>
/// <param name="recorder">Records the calls.</param>
internal sealed class DependencyTrackingService(SpanRecorder recorder) : IHostedService, IDisposable
{
    private const string HostingSourceName = "Microsoft.AspNetCore";
    private const string HttpSourceName = "System.Net.Http";
    private const string NpgsqlSourceName = "Npgsql";
    private const string MySqlConnectorSourceName = "MySqlConnector";
    private const string HttpDependencyType = "HTTP";
    private const string SqlDependencyType = "SQL";

    private const int FirstHttpErrorStatus = 400;

    private static readonly string[] KnownSqlSourceNames = [NpgsqlSourceName, MySqlConnectorSourceName];

    private ActivityListener? _listener;
    private HashSet<string> _sqlSourceNames = [];

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!recorder.IsEnabled || _listener is not null)
        {
            return Task.CompletedTask;
        }

        try
        {
            var options = recorder.Options;

            _sqlSourceNames = options.TrackSqlDependencies
                ? [.. KnownSqlSourceNames, .. options.SqlActivitySources]
                : [];

            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == HostingSourceName || IsTracked(source.Name),
                Sample = (ref ActivityCreationOptions<ActivityContext> creation) => Sample(creation.Source.Name),
                SampleUsingParentId = (ref ActivityCreationOptions<string> creation) => Sample(creation.Source.Name),
                ActivityStopped = OnActivityStopped
            };

            ActivitySource.AddActivityListener(_listener);
        }
#pragma warning disable CA1031 // The SDK must never throw into the host application.
        catch (Exception)
#pragma warning restore CA1031
        {
            // The application works just the same without tracking.
            _listener?.Dispose();
            _listener = null;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _listener?.Dispose();
        _listener = null;
    }

    private static string? ReadText(Activity activity, string tag)
    {
        return activity.GetTagItem(tag) is { } value ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
    }

    private static int? ReadStatusCode(Activity activity)
    {
        return int.TryParse(ReadText(activity, "http.response.status_code"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var statusCode)
            ? statusCode
            : null;
    }

    // A query string can contain secrets. The runtime hides its values by default, but an application can turn that off.
    private static string? WithoutQuery(string? url)
    {
        var end = url?.IndexOfAny(['?', '#']) ?? -1;

        return end >= 0 ? url![..end] : url;
    }

    // Drivers also announce opening a connection, which is not a command and only happens when the pool has none idle.
    private static bool IsConnectionOpen(Activity activity)
    {
        return activity.Source.Name switch
        {
            NpgsqlSourceName => activity.OperationName.StartsWith("CONNECT ", StringComparison.Ordinal),
            MySqlConnectorSourceName => activity.OperationName == "Open",
            _ => false
        };
    }

    private bool IsTracked(string sourceName)
    {
        return (sourceName == HttpSourceName && recorder.Options.TrackHttpDependencies) || _sqlSourceNames.Contains(sourceName);
    }

    private ActivitySamplingResult Sample(string sourceName)
    {
        // The requests that send events would otherwise each create an event to send.
        if (SendingFlow.IsCurrent)
        {
            return ActivitySamplingResult.None;
        }

        // Only tracked calls need their tags collected. Everything else only has to carry the Trace Id on.
        return IsTracked(sourceName) ? ActivitySamplingResult.AllDataAndRecorded : ActivitySamplingResult.PropagationData;
    }

    private void OnActivityStopped(Activity activity)
    {
        try
        {
            if (activity.IdFormat != ActivityIdFormat.W3C || !IsTracked(activity.Source.Name))
            {
                return;
            }

            if (activity.Source.Name == HttpSourceName)
            {
                RecordHttp(activity);
            }
            else
            {
                RecordSql(activity);
            }
        }
#pragma warning disable CA1031 // The SDK must never throw into the host application.
        catch (Exception)
#pragma warning restore CA1031
        {
            // One span is not worth failing a call for.
        }
    }

    private void RecordHttp(Activity activity)
    {
        var host = ReadText(activity, "server.address");
        var port = ReadText(activity, "server.port");
        var method = ReadText(activity, "http.request.method") ?? activity.DisplayName;
        var statusCode = ReadStatusCode(activity);

        var isSuccess = activity.Status != ActivityStatusCode.Error
            && ReadText(activity, "error.type") is null
            && statusCode is null or < FirstHttpErrorStatus;

        recorder.RecordDependency(
            activity,
            $"{method} {host}",
            HttpDependencyType,
            isSuccess,
            statusCode,
            port is null or "80" or "443" ? host : $"{host}:{port}",
            WithoutQuery(ReadText(activity, "url.full")));
    }

    private void RecordSql(Activity activity)
    {
        if (IsConnectionOpen(activity))
        {
            return;
        }

        var database = ReadText(activity, "db.namespace") ?? ReadText(activity, "db.name");
        var statement = recorder.Options.CaptureSqlText
            ? ReadText(activity, "db.query.text") ?? ReadText(activity, "db.statement")
            : null;

        recorder.RecordDependency(
            activity,
            $"{SqlDependencyType} {database}".TrimEnd(),
            SqlDependencyType,
            activity.Status != ActivityStatusCode.Error && ReadText(activity, "error.type") is null,
            statusCode: null,
            database,
            statement);
    }
}
