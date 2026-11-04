// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;

namespace Logbook.Core.Application.Applications.GetAll;

/// <summary>
///     The Service to get all Applications.
/// </summary>
/// <param name="applicationRepository">An Instance of a IApplicationRepository to get all Applications.</param>
public sealed class GetAllApplicationsService(IApplicationRepository applicationRepository)
{
    /// <summary>
    ///     Gets all Applications ordered by their Name.
    /// </summary>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>All Applications.</returns>
    public async Task<IReadOnlyList<GetAllApplicationsResponse>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var applications = await applicationRepository.GetApplications(cancellationToken);

        return applications
            .Select(application => new GetAllApplicationsResponse(application.Id, application.Name, application.CreatedAt))
            .ToList();
    }
}
