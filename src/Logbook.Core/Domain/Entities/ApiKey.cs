// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.ValueObjects;

namespace Logbook.Core.Domain.Entities;

/// <summary>
///     Represents an API Key an <see cref="Application"/> uses to send its Log Events.
/// </summary>
/// <remarks>
///     An Application can have multiple active API Keys at once to allow rotation without downtime.
///     API Keys are created and revoked through the <see cref="Application"/>.
/// </remarks>
public sealed class ApiKey
{
    /// <summary>
    ///     Id of the API Key.
    /// </summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>
    ///     Id of the <see cref="Application"/> the API Key belongs to. Set by EF Core when the API Key is saved.
    /// </summary>
    public Guid ApplicationId { get; private set; }

    /// <summary>
    ///     Hash of the API Key. The plain-text API Key is only shown once when it is issued and never stored.
    /// </summary>
    public required ApiKeyHash KeyHash { get; init; }

    /// <summary>
    ///     Point in time (UTC) when the API Key was issued.
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    ///     Point in time (UTC) when the API Key expires, or <see langword="null"/> if it does not expire.
    /// </summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>
    ///     Point in time (UTC) when the API Key was revoked, or <see langword="null"/> if it is not revoked.
    /// </summary>
    public DateTime? RevokedAt { get; private set; }

    /// <summary>
    ///     Checks whether the API Key can be used, i.e. it is neither revoked nor expired.
    /// </summary>
    /// <param name="now">The current point in time (UTC).</param>
    /// <returns><see langword="true"/> if the API Key is active, otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="now"/> is not in UTC.</exception>
    public bool IsActive(DateTime now)
    {
        UtcGuard.EnsureUtc(now);

        return RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);
    }

    /// <summary>
    ///     Revokes the API Key. Revoking an already revoked API Key keeps the original revocation time.
    /// </summary>
    /// <param name="now">The current point in time (UTC).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="now"/> is not in UTC.</exception>
    public void Revoke(DateTime now)
    {
        UtcGuard.EnsureUtc(now);

        RevokedAt ??= now;
    }
}
