// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Text.Json;

namespace Logbook.Sdk;

/// <summary>
///     Writes metric points as the body of a request to <c>POST /api/v1/metrics</c>.
/// </summary>
/// <remarks>
///     The format is written by hand, because the SDK cannot share types with the server. The end-to-end test in
///     <c>Logbook.Api.IntegrationTests</c> guards it against drifting apart.
/// </remarks>
internal static class MetricBatchWriter
{
    /// <summary>
    ///     Writes as many of the metric points as fit into one request, in order, and compresses them. Points outside of the
    ///     time window of the server are left out, because one of them would cost the whole batch.
    /// </summary>
    /// <param name="metrics">The metric points, oldest first. Must not be empty.</param>
    /// <param name="limits">The limits of the server.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The batch. It has no metric points if none of them can be sent.</returns>
    public static LogEventBatch Write(IReadOnlyList<BufferedItem<LogbookMetric>> metrics, LogEventLimits limits, DateTimeOffset now)
    {
        return TelemetryBatchWriter.Write(
            metrics,
            limits.MaxRequestBytes,
            (LogbookMetric metric, out LogbookMetric? guarded) =>
            {
                // Its values were made acceptable when it was created.
                var isExpired = LogEventGuard.IsOutsideTimeWindow(metric.Timestamp, limits, now);
                guarded = isExpired ? null : metric;

                return isExpired ? LogEventGuardResult.Expired : LogEventGuardResult.Accepted;
            },
            WriteMetric);
    }

    private static void WriteMetric(Utf8JsonWriter writer, LogbookMetric metric)
    {
        writer.WriteStartObject();
        writer.WriteString("timestamp", metric.Timestamp);
        writer.WriteString("name", metric.Name);
        writer.WriteNumber("count", metric.Count);
        writer.WriteNumber("sum", metric.Sum);
        writer.WriteNumber("min", metric.Min);
        writer.WriteNumber("max", metric.Max);
        TelemetryBatchWriter.WriteOptionalString(writer, "environment", metric.Environment);

        if (metric.Dimensions.Count > 0)
        {
            writer.WriteStartObject("dimensions");

            foreach (var dimension in metric.Dimensions)
            {
                writer.WriteString(dimension.Key, dimension.Value);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }
}
