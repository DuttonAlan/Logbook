// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Infrastructure.Persistence;

/// <summary>
///     The PostgreSQL schemas of Logbook. They separate data by how it is used and retained.
/// </summary>
public static class Schemas
{
    /// <summary>
    ///     Configuration that is kept: Applications, API Keys, retention settings, saved searches and, later, alert rules.
    /// </summary>
    public const string Config = "config";

    /// <summary>
    ///     Telemetry that expires: Log Events, spans and metric points, partitioned by day.
    /// </summary>
    public const string Telemetry = "telemetry";
}
