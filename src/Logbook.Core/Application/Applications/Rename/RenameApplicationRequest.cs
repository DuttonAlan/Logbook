// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.ComponentModel.DataAnnotations;

namespace Logbook.Core.Application.Applications.Rename;

/// <summary>
///     Represents the request to rename an Application.
/// </summary>
/// <param name="Name" example="Shop Web App">New Display Name of the Application, unique within Logbook.</param>
public sealed record RenameApplicationRequest(
    [Required, StringLength(Domain.Entities.Application.NameMaxLength, MinimumLength = 1)] string Name);
