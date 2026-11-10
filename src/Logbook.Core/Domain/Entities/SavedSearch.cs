// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.Exceptions;

namespace Logbook.Core.Domain.Entities;

/// <summary>
///     Represents a search of the log explorer that was saved under a name, so that it can be run again with one click.
/// </summary>
/// <remarks>
///     The search is kept as the query string of the log explorer, which holds all of its filters. Saved searches are shared
///     by everyone who can sign in, like the Applications.
/// </remarks>
public sealed class SavedSearch
{
    /// <summary>
    ///     Maximum length of the Name.
    /// </summary>
    public const int NameMaxLength = 100;

    /// <summary>
    ///     Maximum length of the Query.
    /// </summary>
    public const int QueryMaxLength = 4000;

    /// <summary>
    ///     Used by EF Core to materialize a saved search from the database.
    /// </summary>
    private SavedSearch()
    {
    }

    /// <summary>
    ///     Id of the saved search.
    /// </summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>
    ///     Display Name, unique within Logbook.
    /// </summary>
    public string Name { get; private set; } = null!;

    /// <summary>
    ///     The query string of the log explorer, without the leading <c>?</c>, e.g. <c>level=Error&amp;range=24h</c>.
    /// </summary>
    public string Query { get; private set; } = null!;

    /// <summary>
    ///     Point in time (UTC) when the saved search was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    ///     Point in time (UTC) when the saved search was last changed, or <see langword="null"/> if it never was.
    /// </summary>
    public DateTime? ModifiedAt { get; private set; }

    /// <summary>
    ///     Creates a new saved search.
    /// </summary>
    /// <param name="name">The Display Name.</param>
    /// <param name="query">The query string of the log explorer. A leading <c>?</c> is removed.</param>
    /// <param name="now">The current point in time (UTC), used as the creation time.</param>
    /// <returns>The created saved search.</returns>
    /// <exception cref="DomainValidationException">Thrown when the Name or the Query is not valid.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="now"/> is not in UTC.</exception>
    public static SavedSearch Create(string name, string? query, DateTime now)
    {
        UtcGuard.EnsureUtc(now);

        return new SavedSearch
        {
            Name = NormalizeName(name),
            Query = NormalizeQuery(query),
            CreatedAt = now
        };
    }

    /// <summary>
    ///     Changes the Name and the Query. Does nothing if both stay the same.
    /// </summary>
    /// <param name="name">The new Display Name.</param>
    /// <param name="query">The new query string of the log explorer. A leading <c>?</c> is removed.</param>
    /// <param name="now">The current point in time (UTC), used as the modification time.</param>
    /// <exception cref="DomainValidationException">Thrown when the Name or the Query is not valid.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="now"/> is not in UTC.</exception>
    public void Change(string name, string? query, DateTime now)
    {
        UtcGuard.EnsureUtc(now);

        var normalizedName = NormalizeName(name);
        var normalizedQuery = NormalizeQuery(query);

        if (normalizedName == Name && normalizedQuery == Query)
        {
            return;
        }

        Name = normalizedName;
        Query = normalizedQuery;
        ModifiedAt = now;
    }

    private static string NormalizeName(string name)
    {
        DomainValidationException.ThrowIfNullOrWhiteSpace(name);

        var trimmed = name.Trim();

        if (trimmed.Length > NameMaxLength)
        {
            throw new DomainValidationException($"Name must not be longer than {NameMaxLength} characters.", nameof(name));
        }

        if (trimmed.Any(char.IsControl))
        {
            throw new DomainValidationException("Name must not contain control characters.", nameof(name));
        }

        return trimmed;
    }

    private static string NormalizeQuery(string? query)
    {
        var trimmed = (query ?? string.Empty).Trim();

        if (trimmed.StartsWith('?'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.Length > QueryMaxLength)
        {
            throw new DomainValidationException($"Query must not be longer than {QueryMaxLength} characters.", nameof(query));
        }

        // A query string is encoded: anything else is not what the log explorer saves, and could not be a link.
        if (trimmed.Any(character => char.IsControl(character) || char.IsWhiteSpace(character) || character == '#'))
        {
            throw new DomainValidationException("Query must be an encoded query string, without spaces or a fragment.", nameof(query));
        }

        return trimmed;
    }
}
