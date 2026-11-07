// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Net.Http.Json;

using Logbook.Api.Authentication;
using Logbook.Api.Authorization;

using Microsoft.AspNetCore.Mvc.Testing;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Creates clients with a session, as a signed-in browser has one.
/// </summary>
public static class AdminClients
{
    /// <summary>
    ///     Creates a client that has signed in as the local administrator, and keeps the session cookie.
    /// </summary>
    /// <param name="host">The Api to sign in to.</param>
    /// <returns>The client.</returns>
    public static HttpClient CreateAdminClient(this WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();

        client.DefaultRequestHeaders.Add(AuthorizationPolicies.RequestedWithHeader, "tests");

        // Blocking, because tests create their clients in constructors and field initializers. It finishes quickly.
        using var response = client
            .PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(LogbookApiFactory.AdminUserName, LogbookApiFactory.AdminPassword))
            .GetAwaiter()
            .GetResult();

        response.EnsureSuccessStatusCode();

        return client;
    }
}
