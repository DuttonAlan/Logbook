// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Domain.ValueObjects;

/// <summary>
///     The one-way hash of an API Key. Immutable and compared by value.
/// </summary>
/// <remarks>
///     A separate type keeps a plain-text API Key from being stored or compared as a hash by mistake, because both are
///     strings. Create it with <c>IApiKeyHasher.Hash</c>, never from a plain-text API Key.
/// </remarks>
public sealed record ApiKeyHash
{
    private ApiKeyHash(string value) => Value = value;

    /// <summary>
    ///     The encoded hash.
    /// </summary>
    public string Value { get; }

    /// <summary>
    ///     Wraps an already computed hash, e.g. the result of a hasher or a value loaded from the database.
    /// </summary>
    /// <param name="value">The encoded hash. Must never be a plain-text API Key.</param>
    /// <returns>The wrapped hash.</returns>
    /// <exception cref="ArgumentException">Thrown when the value is empty.</exception>
    public static ApiKeyHash FromHashedValue(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return new ApiKeyHash(value);
    }

    /// <summary>
    ///     Returns a placeholder, so the hash is not written to logs by accident.
    /// </summary>
    /// <returns>A placeholder instead of the hash.</returns>
    public override string ToString() => "ApiKeyHash(***)";
}
