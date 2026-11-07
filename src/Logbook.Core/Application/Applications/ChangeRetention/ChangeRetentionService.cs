// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Abstractions;
using Logbook.Core.Application.Exceptions;
using Logbook.Core.Domain.Exceptions;

using Microsoft.Extensions.Logging;

namespace Logbook.Core.Application.Applications.ChangeRetention;

/// <summary>
///     The Service to change how long the Log Events of an Application are kept.
/// </summary>
/// <param name="applicationRepository">An Instance of a IApplicationRepository to change the Application.</param>
/// <param name="timeProvider">The TimeProvider for the modification time.</param>
/// <param name="logger">The Logger to record the change.</param>
public sealed class ChangeRetentionService(
    IApplicationRepository applicationRepository,
    TimeProvider timeProvider,
    ILogger<ChangeRetentionService> logger)
{
    /// <summary>
    ///     Changes the retention of an existing Application. Log Events beyond it are deleted by the next retention run.
    /// </summary>
    /// <param name="id">Id of the Application.</param>
    /// <param name="request">The request with the new retention.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>A Task that completes once the retention is changed.</returns>
    /// <exception cref="NotFoundException">Thrown when the Application does not exist.</exception>
    /// <exception cref="DomainValidationException">Thrown when the retention is out of range.</exception>
    public async Task ExecuteAsync(
        Guid id,
        ChangeRetentionRequest request,
        CancellationToken cancellationToken = default)
    {
        var application = await applicationRepository.GetApplicationById(id, cancellationToken)
            ?? throw new NotFoundException($"Application '{id}' was not found.");

        application.ChangeRetention(request.RetentionDays, timeProvider.GetUtcNow().UtcDateTime);

        await applicationRepository.SaveChangesAsync(cancellationToken);

        logger.RetentionChanged(application.Id, application.RetentionDays);
    }
}
