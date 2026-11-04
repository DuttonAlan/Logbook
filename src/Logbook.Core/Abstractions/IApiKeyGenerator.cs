// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Abstractions;

/// <summary>
///     Generates cryptographically random API Keys.
/// </summary>
public interface IApiKeyGenerator
{
    /// <summary>
    ///     Generates a new random API Key with at least 256 bits of entropy, encoded as URL-safe text.
    /// </summary>
    /// <returns>The plain-text API Key.</returns>
    public string Generate();
}
