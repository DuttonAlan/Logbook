// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Text.Json;

namespace Logbook.Sdk;

/// <summary>
///     Writes spans as the body of a request to <c>POST /api/v1/spans</c>.
/// </summary>
/// <remarks>
///     The format is written by hand, because the SDK cannot share types with the server. The end-to-end test in
///     <c>Logbook.Api.IntegrationTests</c> guards it against drifting apart.
/// </remarks>
internal static class SpanBatchWriter
{
    /// <summary>
    ///     Writes as many of the spans as fit into one request, in order, and compresses them. Spans outside of the time
    ///     window of the server are left out, because one of them would cost the whole batch.
    /// </summary>
    /// <param name="spans">The spans, oldest first. Must not be empty.</param>
    /// <param name="limits">The limits of the server.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The batch. It has no spans if none of them can be sent.</returns>
    public static LogEventBatch Write(IReadOnlyList<BufferedItem<LogbookSpan>> spans, LogEventLimits limits, DateTimeOffset now)
    {
        return TelemetryBatchWriter.Write(
            spans,
            limits.MaxRequestBytes,
            (LogbookSpan span, out LogbookSpan? guarded) =>
            {
                // Its text was cut when it was created.
                var isExpired = LogEventGuard.IsOutsideTimeWindow(span.Timestamp, limits, now);
                guarded = isExpired ? null : span;

                return isExpired ? LogEventGuardResult.Expired : LogEventGuardResult.Accepted;
            },
            WriteSpan);
    }

    private static void WriteSpan(Utf8JsonWriter writer, LogbookSpan span)
    {
        writer.WriteStartObject();
        writer.WriteString("timestamp", span.Timestamp);
        writer.WriteString("kind", span.Kind == LogbookSpanKind.Dependency ? "Dependency" : "Request");
        writer.WriteString("name", span.Name);
        writer.WriteNumber("durationMs", span.DurationMilliseconds);
        writer.WriteBoolean("success", span.Success);
        writer.WriteString("traceId", span.TraceId);
        writer.WriteString("spanId", span.SpanId);
        TelemetryBatchWriter.WriteOptionalString(writer, "parentSpanId", span.ParentSpanId);
        TelemetryBatchWriter.WriteOptionalString(writer, "environment", span.Environment);

        if (span.StatusCode is { } statusCode)
        {
            writer.WriteNumber("statusCode", statusCode);
        }

        TelemetryBatchWriter.WriteOptionalString(writer, "dependencyType", span.DependencyType);
        TelemetryBatchWriter.WriteOptionalString(writer, "target", span.Target);
        TelemetryBatchWriter.WriteOptionalString(writer, "data", span.Data);
        writer.WriteEndObject();
    }
}
