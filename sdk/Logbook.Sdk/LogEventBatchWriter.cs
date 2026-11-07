// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Text.Json;

namespace Logbook.Sdk;

/// <summary>
///     Writes events as the body of a request to <c>POST /api/v1/logs</c>.
/// </summary>
/// <remarks>
///     The format is written by hand, because the SDK cannot share types with the server. The end-to-end test in
///     <c>Logbook.Api.IntegrationTests</c> guards it against drifting apart.
/// </remarks>
internal static class LogEventBatchWriter
{
    /// <summary>
    ///     Writes as many of the events as fit into one request, in order, and compresses them. Events the server would
    ///     reject are left out, because one of them would cost the whole batch.
    /// </summary>
    /// <param name="events">The events, oldest first. Must not be empty.</param>
    /// <param name="limits">The limits of the server.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The batch. It has no events if none of them can be sent.</returns>
    public static LogEventBatch Write(IReadOnlyList<BufferedItem<LogbookEvent>> events, LogEventLimits limits, DateTimeOffset now)
    {
        return TelemetryBatchWriter.Write(
            events,
            limits.MaxRequestBytes,
            (LogbookEvent logEvent, out LogbookEvent? guarded) => LogEventGuard.Check(logEvent, limits, now, out guarded),
            WriteEvent);
    }

    private static void WriteEvent(Utf8JsonWriter writer, LogbookEvent logEvent)
    {
        writer.WriteStartObject();
        writer.WriteString("timestamp", logEvent.Timestamp);
        writer.WriteString("level", GetLevelName(logEvent.Level));
        writer.WriteString("message", logEvent.Message);
        TelemetryBatchWriter.WriteOptionalString(writer, "messageTemplate", logEvent.MessageTemplate);
        TelemetryBatchWriter.WriteOptionalString(writer, "category", logEvent.Category);
        TelemetryBatchWriter.WriteOptionalString(writer, "environment", logEvent.Environment);

        if (logEvent.EventId is { } eventId)
        {
            writer.WriteNumber("eventId", eventId);
        }

        WriteOptionalObject(writer, "exception", logEvent.ExceptionJson);
        TelemetryBatchWriter.WriteOptionalString(writer, "traceId", logEvent.TraceId);
        TelemetryBatchWriter.WriteOptionalString(writer, "spanId", logEvent.SpanId);
        WriteOptionalObject(writer, "properties", logEvent.PropertiesJson);
        writer.WriteEndObject();
    }

    private static void WriteOptionalObject(Utf8JsonWriter writer, string name, string? json)
    {
        // The guard has checked it.
        if (json is not null)
        {
            writer.WritePropertyName(name);
            writer.WriteRawValue(json, skipInputValidation: true);
        }
    }

    private static string GetLevelName(LogbookLevel level)
    {
        return level switch
        {
            LogbookLevel.Trace => "Trace",
            LogbookLevel.Debug => "Debug",
            LogbookLevel.Warning => "Warning",
            LogbookLevel.Error => "Error",
            LogbookLevel.Critical => "Critical",

            // An undefined value would reject the whole batch.
            _ => "Information"
        };
    }
}
