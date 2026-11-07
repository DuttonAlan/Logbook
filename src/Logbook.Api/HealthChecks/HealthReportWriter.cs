// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Logbook.Api.HealthChecks;

/// <summary>
///     Writes a <see cref="HealthReport"/> as JSON.
/// </summary>
internal static class HealthReportWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    ///     Writes only the overall status, e.g. <c>{"status":"Healthy"}</c>. The data of the checks, e.g. the applied
    ///     migrations, would reveal the exact version that is running.
    /// </summary>
    /// <param name="httpContext">The HttpContext of the request.</param>
    /// <param name="report">The report to write.</param>
    /// <returns>A Task that completes once the status is written.</returns>
    public static Task WriteStatusAsync(HttpContext httpContext, HealthReport report)
    {
        httpContext.Response.ContentType = "application/json";

        return JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            new HealthStatusResponse(report.Status),
            SerializerOptions,
            httpContext.RequestAborted);
    }

    /// <summary>
    ///     Writes the report with all checks and the version of Logbook, for those who are allowed to see it. Exceptions of
    ///     the checks are left out, as they can contain the address of the database.
    /// </summary>
    /// <param name="httpContext">The HttpContext of the request.</param>
    /// <param name="report">The report to write.</param>
    /// <returns>A Task that completes once the report is written.</returns>
    public static Task WriteDetailsAsync(HttpContext httpContext, HealthReport report)
    {
        httpContext.Response.ContentType = "application/json";
        httpContext.Response.Headers.CacheControl = "no-store";

        var version = typeof(HealthReportWriter).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        var details = new HealthDetailsResponse(
            report.Status,
            version,
            report.TotalDuration,
            report.Entries
                .Select(entry => new HealthCheckDetails(
                    entry.Key,
                    entry.Value.Status,
                    entry.Value.Description,
                    entry.Value.Duration,
                    entry.Value.Data))
                .ToList());

        return JsonSerializer.SerializeAsync(httpContext.Response.Body, details, SerializerOptions, httpContext.RequestAborted);
    }
}
