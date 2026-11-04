// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;

using Microsoft.Extensions.Logging;

namespace Logbook.Core.Application.Applications.Delete;

/// <summary>
///     The Service to delete an Application.
/// </summary>
/// <param name="applicationRepository">An Instance of a IApplicationRepository to delete an Application.</param>
/// <param name="logger">The Logger to record the deletion.</param>
public sealed class DeleteApplicationService(
    IApplicationRepository applicationRepository,
    ILogger<DeleteApplicationService> logger)
{
    /// <summary>
    ///     Deletes an Application based on the Id, including its API Keys.
    ///     Does nothing if the Application does not exist.
    /// </summary>
    /// <param name="id">Id of the Application.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>A Task that completes once the Application is deleted.</returns>
    public async Task ExecuteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var application = await applicationRepository.GetApplicationById(id, cancellationToken);

        if (application is null)
        {
            return;
        }

        await applicationRepository.DeleteApplication(application, cancellationToken);

        logger.ApplicationDeleted(application.Id, application.Name);
    }
}
