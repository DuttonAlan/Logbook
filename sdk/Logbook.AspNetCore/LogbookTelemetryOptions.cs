// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.AspNetCore;

/// <summary>
///     Configuration of what an ASP.NET Core application tracks, read from the section <c>Logbook:Telemetry</c>.
/// </summary>
/// <remarks>
///     Where and how the spans are sent is configured with the options of the client, in the section <c>Logbook</c>.
/// </remarks>
public sealed class LogbookTelemetryOptions
{
    /// <summary>
    ///     Name of the configuration section.
    /// </summary>
    public const string SectionName = "Logbook:Telemetry";

    /// <summary>
    ///     Whether to track the requests the application receives.
    /// </summary>
    public bool TrackRequests { get; set; } = true;

    /// <summary>
    ///     Whether to track the requests the application makes with <see cref="HttpClient"/>.
    /// </summary>
    public bool TrackHttpDependencies { get; set; } = true;

    /// <summary>
    ///     Whether to track the commands the application sends to a database. Off by default, as one request can run many
    ///     commands. This needs a database driver that publishes an <c>ActivitySource</c>, see
    ///     <see cref="SqlActivitySources"/>.
    /// </summary>
    public bool TrackSqlDependencies { get; set; }

    /// <summary>
    ///     Whether to send the text of the SQL statements. Off by default, as it can contain values if a statement is not
    ///     parameterized.
    /// </summary>
    public bool CaptureSqlText { get; set; }

    /// <summary>
    ///     The names of the <c>ActivitySource</c>s of database drivers. The drivers Npgsql and MySqlConnector are always
    ///     included, and this adds others.
    /// </summary>
    public IList<string> SqlActivitySources { get; } = [];

    /// <summary>
    ///     Paths of requests not to track, each including what is below it, e.g. <c>/health</c>. Useful for what is called
    ///     often and says little, such as probes of a load balancer.
    /// </summary>
    public IList<string> ExcludedRequestPaths { get; } = [];
}
