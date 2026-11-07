// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Text.Json;

namespace Logbook.Core.Application.ExceptionGroups.Search;

/// <summary>
///     Represents a group of exceptions of one Application: those with the same type and stack fingerprint.
/// </summary>
/// <param name="ApplicationId" example="0199d1a2-7c3e-7b4a-9f10-5c963f66afa6">Id of the Application.</param>
/// <param name="Fingerprint" example="9f86d081884c7d659a2feaa0c55ad015">What identifies the group. Search the Log Events with it to see each occurrence.</param>
/// <param name="Count" example="17">The number of occurrences in the requested time range.</param>
/// <param name="FirstOccurrence" example="2026-11-01T08:00:00Z">Point in time (UTC) of the earliest occurrence that is still stored, also if it is before the requested time range.</param>
/// <param name="LastOccurrence" example="2026-11-04T10:15:00.123Z">Point in time (UTC) of the latest occurrence in the requested time range.</param>
/// <param name="Exception">The exception of the latest occurrence, as a JSON object.</param>
public sealed record ExceptionGroupResponse(
    Guid ApplicationId,
    string Fingerprint,
    long Count,
    DateTime FirstOccurrence,
    DateTime LastOccurrence,
    JsonElement Exception);
