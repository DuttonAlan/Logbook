// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.Exceptions;
using Logbook.Core.Domain.ValueObjects;

namespace Logbook.Core.Domain.Entities;

/// <summary>
///     Represents an Application: a program that sends its Log Events to Logbook.
/// </summary>
/// <remarks>
///     The Application is the aggregate root for its <see cref="ApiKey"/>s, which can only be changed through the
///     methods of this class. An Application can only be created via <see cref="Create(string, DateTime)"/>, so an
///     invalid Application can never exist.
/// </remarks>
public sealed class Application
{
    /// <summary>
    ///     Maximum length of the Name.
    /// </summary>
    public const int NameMaxLength = 100;

    private readonly List<ApiKey> _apiKeys = [];

    /// <summary>
    ///     Used by EF Core to materialize an Application from the database.
    /// </summary>
    private Application()
    {
    }

    /// <summary>
    ///     Id of the Application.
    /// </summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>
    ///     Display Name of the Application, unique within Logbook. Set via <see cref="Rename(string, DateTime)"/>.
    /// </summary>
    public string Name { get; private set; } = null!;

    /// <summary>
    ///     API Keys of the Application, including expired and revoked ones.
    /// </summary>
    public IReadOnlyCollection<ApiKey> ApiKeys => _apiKeys;

    /// <summary>
    ///     Point in time (UTC) when the Application was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    ///     Point in time (UTC) when the Application was last renamed, or <see langword="null"/> if it never was.
    /// </summary>
    public DateTime? ModifiedAt { get; private set; }

    /// <summary>
    ///     Creates a new Application without API Keys.
    /// </summary>
    /// <param name="name">The Display Name of the Application.</param>
    /// <param name="now">The current point in time (UTC), used as the creation time.</param>
    /// <returns>The created Application.</returns>
    /// <exception cref="DomainValidationException">Thrown when the Name is empty or too long.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="now"/> is not in UTC.</exception>
    public static Application Create(string name, DateTime now)
    {
        UtcGuard.EnsureUtc(now);

        return new Application
        {
            Name = NormalizeName(name),
            CreatedAt = now
        };
    }

    /// <summary>
    ///     Changes the Display Name. Does nothing if the Name stays the same.
    /// </summary>
    /// <param name="name">The new Name of the Application.</param>
    /// <param name="now">The current point in time (UTC), used as the modification time.</param>
    /// <exception cref="DomainValidationException">Thrown when the Name is empty or too long.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="now"/> is not in UTC.</exception>
    public void Rename(string name, DateTime now)
    {
        UtcGuard.EnsureUtc(now);

        var normalized = NormalizeName(name);

        if (normalized == Name)
        {
            return;
        }

        Name = normalized;
        ModifiedAt = now;
    }

    /// <summary>
    ///     Adds a new API Key to the Application.
    /// </summary>
    /// <param name="keyHash">The hash of the API Key. The plain-text API Key must never be passed to the domain.</param>
    /// <param name="now">The current point in time (UTC), used as the creation time.</param>
    /// <param name="expiresAt">Point in time (UTC) when the API Key expires, or <see langword="null"/> if it does not expire.</param>
    /// <returns>The created API Key.</returns>
    /// <exception cref="DomainValidationException">Thrown when the expiry is not in the future.</exception>
    /// <exception cref="ArgumentException">Thrown when a point in time is not in UTC.</exception>
    public ApiKey AddApiKey(ApiKeyHash keyHash, DateTime now, DateTime? expiresAt)
    {
        ArgumentNullException.ThrowIfNull(keyHash);
        UtcGuard.EnsureUtc(now);

        if (expiresAt is { } expiry)
        {
            UtcGuard.EnsureUtc(expiry, nameof(expiresAt));

            if (expiry <= now)
            {
                throw new DomainValidationException("The expiry of the API Key must be in the future.", nameof(expiresAt));
            }
        }

        var apiKey = new ApiKey { KeyHash = keyHash, CreatedAt = now, ExpiresAt = expiresAt };
        _apiKeys.Add(apiKey);

        return apiKey;
    }

    /// <summary>
    ///     Revokes an API Key of the Application. Does nothing if the API Key does not exist or is already revoked.
    /// </summary>
    /// <param name="apiKeyId">The Id of the API Key to revoke.</param>
    /// <param name="now">The current point in time (UTC).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="now"/> is not in UTC.</exception>
    public void RevokeApiKey(Guid apiKeyId, DateTime now)
    {
        UtcGuard.EnsureUtc(now);

        _apiKeys.FirstOrDefault(apiKey => apiKey.Id == apiKeyId)?.Revoke(now);
    }

    private static string NormalizeName(string name)
    {
        DomainValidationException.ThrowIfNullOrWhiteSpace(name);

        var trimmed = name.Trim();

        if (trimmed.Length > NameMaxLength)
        {
            throw new DomainValidationException($"Name must not be longer than {NameMaxLength} characters.", nameof(name));
        }

        return trimmed;
    }
}
