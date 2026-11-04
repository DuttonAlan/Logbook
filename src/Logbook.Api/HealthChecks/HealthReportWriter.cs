// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

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
}
