// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.ComponentModel.DataAnnotations;

namespace Logbook.Core.Application.Applications.ChangeRetention;

/// <summary>
///     Represents the request to change how long the Log Events of an Application are kept.
/// </summary>
/// <param name="RetentionDays" example="14">Days to keep Log Events, or null to use the setting <c>Retention:DefaultDays</c>.</param>
public sealed record ChangeRetentionRequest(
    [Range(Domain.Entities.Application.MinRetentionDays, Domain.Entities.Application.MaxRetentionDays)] int? RetentionDays);
