// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Text.Json;

using Logbook.Core.Domain.Entities;

namespace Logbook.Core.Application.LogEvents.Search;

/// <summary>
///     Represents a stored Log Event.
/// </summary>
/// <param name="Id" example="0199d1a2-7c3e-7b4a-9f10-7c963f66afa6">Id of the Log Event.</param>
/// <param name="ApplicationId" example="0199d1a2-7c3e-7b4a-9f10-5c963f66afa6">Id of the Application that sent the Log Event.</param>
/// <param name="Timestamp" example="2026-11-04T10:15:00.123Z">Point in time (UTC) when the Log Event occurred.</param>
/// <param name="Level" example="Error">Severity of the Log Event.</param>
/// <param name="Message" example="Order 42 failed">The rendered message.</param>
/// <param name="MessageTemplate" example="Order {OrderId} failed">The message before rendering.</param>
/// <param name="Category" example="Shop.Orders.OrderService">The category of the logger.</param>
/// <param name="Environment" example="Production">The Environment.</param>
/// <param name="EventId" example="4001">The numeric Id of the event within its category.</param>
/// <param name="Exception">The exception as a JSON object.</param>
/// <param name="TraceId" example="4bf92f3577b34da6a3ce929d0e0e4736">The W3C Trace Id.</param>
/// <param name="SpanId" example="00f067aa0ba902b7">The W3C Span Id.</param>
/// <param name="Properties">The structured properties as a JSON object.</param>
/// <param name="ExceptionFingerprint" example="9f86d081884c7d659a2feaa0c55ad015">What identifies the failure of the exception. Log Events with the same value form a group.</param>
public sealed record LogEventResponse(
    Guid Id,
    Guid ApplicationId,
    DateTime Timestamp,
    LogEventLevel Level,
    string Message,
    string? MessageTemplate,
    string? Category,
    string Environment,
    int? EventId,
    JsonElement? Exception,
    string? TraceId,
    string? SpanId,
    JsonElement? Properties,
    string? ExceptionFingerprint)
{
    /// <summary>
    ///     Creates the response for a stored Log Event.
    /// </summary>
    /// <param name="logEvent">The Log Event.</param>
    /// <returns>The response.</returns>
    public static LogEventResponse From(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        return new LogEventResponse(
            logEvent.Id,
            logEvent.ApplicationId,
            logEvent.Timestamp,
            logEvent.Level,
            logEvent.Message,
            logEvent.MessageTemplate,
            logEvent.Category,
            logEvent.Environment,
            logEvent.EventId,
            ToJson(logEvent.Exception),
            logEvent.TraceId,
            logEvent.SpanId,
            ToJson(logEvent.Properties),
            logEvent.ExceptionFingerprint);
    }

    // As an element, so the response contains the JSON itself and not a string with escaped JSON.
    private static JsonElement? ToJson(string? json) => json is null ? null : JsonSerializer.Deserialize<JsonElement>(json);
}
