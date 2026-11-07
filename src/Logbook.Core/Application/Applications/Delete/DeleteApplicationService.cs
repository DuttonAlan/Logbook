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
/// <param name="logEventRepository">An Instance of a ILogEventRepository to delete the Log Events of the Application.</param>
/// <param name="spanRepository">An Instance of a ISpanRepository to delete the spans of the Application.</param>
/// <param name="logger">The Logger to record the deletion.</param>
public sealed class DeleteApplicationService(
    IApplicationRepository applicationRepository,
    ILogEventRepository logEventRepository,
    ISpanRepository spanRepository,
    ILogger<DeleteApplicationService> logger)
{
    /// <summary>
    ///     Deletes an Application based on the Id, including its API Keys, Log Events and spans.
    /// </summary>
    /// <remarks>
    ///     The Application goes first, so its API Keys stop working and no more Log Events can arrive. Deleting many Log
    ///     Events takes time and can be interrupted, so they are deleted even if the Application no longer exists: calling
    ///     this again finishes an interrupted deletion.
    /// </remarks>
    /// <param name="id">Id of the Application.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>A Task that completes once the Application and everything it sent is deleted.</returns>
    public async Task ExecuteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var application = await applicationRepository.GetApplicationById(id, cancellationToken);

        if (application is not null)
        {
            await applicationRepository.DeleteApplication(application, cancellationToken);

            logger.ApplicationDeleted(application.Id, application.Name);
        }

        var deleted = await logEventRepository.DeleteByApplication(id, cancellationToken);

        if (deleted > 0)
        {
            logger.LogEventsOfApplicationDeleted(id, deleted);
        }

        var deletedSpans = await spanRepository.DeleteByApplication(id, cancellationToken);

        if (deletedSpans > 0)
        {
            logger.SpansOfApplicationDeleted(id, deletedSpans);
        }
    }
}
