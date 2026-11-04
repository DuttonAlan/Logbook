// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Application.Exceptions;
using Logbook.Core.Domain.Exceptions;

using Microsoft.Extensions.Logging;

namespace Logbook.Core.Application.Applications.Rename;

/// <summary>
///     The Service to rename an Application.
/// </summary>
/// <param name="applicationRepository">An Instance of a IApplicationRepository to change the Application.</param>
/// <param name="timeProvider">The TimeProvider for the modification time.</param>
/// <param name="logger">The Logger to record the change.</param>
public sealed class RenameApplicationService(
    IApplicationRepository applicationRepository,
    TimeProvider timeProvider,
    ILogger<RenameApplicationService> logger)
{
    /// <summary>
    ///     Renames an existing Application. Renaming it to its current Name changes nothing.
    /// </summary>
    /// <param name="id">Id of the Application.</param>
    /// <param name="request">The request to rename the Application.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>A Task that completes once the Application is renamed.</returns>
    /// <exception cref="NotFoundException">Thrown when the Application does not exist.</exception>
    /// <exception cref="DomainValidationException">Thrown when the Name is invalid.</exception>
    /// <exception cref="ConflictException">Thrown when another Application already has the Name.</exception>
    public async Task ExecuteAsync(
        Guid id,
        RenameApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        var application = await applicationRepository.GetApplicationById(id, cancellationToken)
            ?? throw new NotFoundException($"Application '{id}' was not found.");

        application.Rename(request.Name, timeProvider.GetUtcNow().UtcDateTime);

        if (await applicationRepository.NameExists(application.Name, application.Id, cancellationToken))
        {
            throw new ConflictException($"An Application named '{application.Name}' already exists.");
        }

        await applicationRepository.SaveChangesAsync(cancellationToken);

        logger.ApplicationRenamed(application.Id, application.Name);
    }
}
