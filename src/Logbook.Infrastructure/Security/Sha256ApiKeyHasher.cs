// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

using Logbook.Core.Abstractions;
using Logbook.Core.Domain.ValueObjects;

namespace Logbook.Infrastructure.Security;

/// <summary>
///     Hashes API Keys with SHA-256, encoded as Base64Url.
/// </summary>
/// <remarks>
///     A fast hash is sufficient because API Keys are 256-bit random values that cannot be brute-forced, and it keeps
///     the check on every ingestion request cheap. Never use it for passwords.
/// </remarks>
public sealed class Sha256ApiKeyHasher : IApiKeyHasher
{
    /// <inheritdoc/>
    public ApiKeyHash Hash(string apiKey)
    {
        ArgumentNullException.ThrowIfNull(apiKey);

        return ApiKeyHash.FromHashedValue(Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey))));
    }
}
