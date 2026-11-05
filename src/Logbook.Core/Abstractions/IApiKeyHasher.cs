// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.ValueObjects;

namespace Logbook.Core.Abstractions;

/// <summary>
///     Hashes high-entropy API Keys created by <see cref="IApiKeyGenerator"/>.
/// </summary>
/// <remarks>
///     Not suitable for passwords, which need a slow hash.
/// </remarks>
public interface IApiKeyHasher
{
    /// <summary>
    ///     Computes the one-way hash of an API Key.
    /// </summary>
    /// <param name="apiKey">The plain-text API Key.</param>
    /// <returns>The hash to store.</returns>
    public ApiKeyHash Hash(string apiKey);
}
