// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.Applications.IssueApiKey;

/// <summary>
///     Represents the request to issue an API Key.
/// </summary>
/// <param name="ExpiresAt" example="2027-10-11T00:00:00Z">Point in time when the API Key expires, or null if it does not expire. Must be in the future.</param>
public sealed record IssueApiKeyRequest(DateTimeOffset? ExpiresAt);
