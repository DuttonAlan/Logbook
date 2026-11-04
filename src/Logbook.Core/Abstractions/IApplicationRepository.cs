// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Abstractions;

/// <summary>
///     Defines a contract for Application operations.
/// </summary>
/// <remarks>
///     Applications are changed by loading them tracked via <see cref="GetApplicationById"/>, changing them through
///     their methods and calling <see cref="SaveChangesAsync"/>.
///     Inside <c>Logbook.Core</c> the simple name <c>Application</c> means the namespace <c>Logbook.Core.Application</c>,
///     so the entity is written as <c>Domain.Entities.Application</c>.
/// </remarks>
public interface IApplicationRepository
{
    /// <summary>
    ///     Creates a new Application, including its API Keys.
    /// </summary>
    /// <param name="application">The Application to create.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>A Task that completes once the Application is created.</returns>
    public Task CreateApplication(Domain.Entities.Application application, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets an Application by Id, including its API Keys.
    /// </summary>
    /// <param name="id">Id of the Application.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>The tracked Application, or null if the Application does not exist.</returns>
    public Task<Domain.Entities.Application?> GetApplicationById(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets all Applications ordered by their Name, without their API Keys.
    /// </summary>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>All Applications.</returns>
    public Task<IReadOnlyList<Domain.Entities.Application>> GetApplications(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Checks whether another Application already has the given Name.
    /// </summary>
    /// <param name="name">The Name, compared character by character.</param>
    /// <param name="excludedId">Id of an Application to ignore, e.g. the one being renamed.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>True if the Name is taken.</returns>
    public Task<bool> NameExists(string name, Guid? excludedId = null, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes an Application, including its API Keys.
    /// </summary>
    /// <param name="application">The tracked Application to delete.</param>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>A Task that completes once the Application is deleted.</returns>
    public Task DeleteApplication(Domain.Entities.Application application, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Saves all changes made to tracked Applications.
    /// </summary>
    /// <param name="cancellationToken">CancellationToken to cancel the request.</param>
    /// <returns>A Task that completes once the changes are saved.</returns>
    public Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
