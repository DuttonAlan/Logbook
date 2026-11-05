// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.ComponentModel.DataAnnotations;
using System.Text.Json;

using Logbook.Core.Domain.Entities;

namespace Logbook.Core.Application.LogEvents.Ingest;

/// <summary>
///     Represents one Log Event in a request to receive Log Events.
/// </summary>
/// <param name="Timestamp" example="2026-11-04T10:15:00.123Z">Point in time when the Log Event occurred, with an offset or Z.</param>
/// <param name="Level" example="Error">Severity of the Log Event.</param>
/// <param name="Message" example="Order 42 failed">The rendered message. Can be empty.</param>
/// <param name="MessageTemplate" example="Order {OrderId} failed">The message before rendering.</param>
/// <param name="Category" example="Shop.Orders.OrderService">The category of the logger.</param>
/// <param name="Environment" example="Production">The Environment. A Log Event without one is stored with the Environment <c>Unknown</c>.</param>
/// <param name="EventId" example="4001">The numeric Id of the event within its category.</param>
/// <param name="Exception">The exception as a JSON object, usually with <c>type</c>, <c>message</c>, <c>stackTrace</c> and <c>inner</c>.</param>
/// <param name="TraceId" example="4bf92f3577b34da6a3ce929d0e0e4736">The W3C Trace Id.</param>
/// <param name="SpanId" example="00f067aa0ba902b7">The W3C Span Id.</param>
/// <param name="Properties">The structured properties as a JSON object.</param>
public sealed record LogEventRequest(
    [Required] DateTimeOffset? Timestamp,
    [Required] LogEventLevel? Level,
    [Required(AllowEmptyStrings = true)] string Message,
    string? MessageTemplate = null,
    string? Category = null,
    string? Environment = null,
    int? EventId = null,
    JsonElement? Exception = null,
    string? TraceId = null,
    string? SpanId = null,
    JsonElement? Properties = null);
