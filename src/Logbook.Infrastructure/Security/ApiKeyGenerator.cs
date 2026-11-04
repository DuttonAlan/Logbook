// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Buffers.Text;
using System.Security.Cryptography;

using Logbook.Core.Abstractions;

namespace Logbook.Infrastructure.Security;

/// <summary>
///     Generates 256-bit random API Keys from a cryptographically secure source, encoded as Base64Url.
/// </summary>
public sealed class ApiKeyGenerator : IApiKeyGenerator
{
    private const int KeySizeInBytes = 32;

    /// <inheritdoc/>
    public string Generate()
    {
        return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(KeySizeInBytes));
    }
}
