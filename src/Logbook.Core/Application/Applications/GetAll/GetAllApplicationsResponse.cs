// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.Applications.GetAll;

/// <summary>
///     Represents an Application in the list of all Applications.
/// </summary>
/// <param name="Id" example="0199d1a2-7c3e-7b4a-9f10-5c963f66afa6">Id of the Application.</param>
/// <param name="Name" example="Shop Web App">Display Name of the Application.</param>
/// <param name="CreatedAt">Point in time (UTC) when the Application was created.</param>
public sealed record GetAllApplicationsResponse(Guid Id, string Name, DateTime CreatedAt);
