// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Api.Authentication;

/// <summary>
///     The claims of a request authenticated by <see cref="ApiKeyAuthenticationHandler"/>.
/// </summary>
public static class ApiKeyClaimTypes
{
    /// <summary>
    ///     Id of the API Key.
    /// </summary>
    public const string ApiKeyId = "logbook:api_key_id";

    /// <summary>
    ///     Id of the Application the API Key belongs to.
    /// </summary>
    public const string ApplicationId = "logbook:application_id";
}
