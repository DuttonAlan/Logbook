// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Globalization;

using Logbook.Api.Authorization;

using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;

using Swashbuckle.AspNetCore.SwaggerGen;

namespace Logbook.Api.OpenApi;

/// <summary>
///     Adds the security requirement of its policy and the 401 response to every operation that requires authorization,
///     so they do not have to be repeated on every action.
/// </summary>
public sealed class AuthorizeOperationFilter : IOperationFilter
{
    /// <summary>
    ///     Name of the security scheme for the session in the OpenAPI document.
    /// </summary>
    public const string SecuritySchemeName = "Session";

    /// <summary>
    ///     Name of the security scheme for the API Key of an Application in the OpenAPI document.
    /// </summary>
    public const string ApiKeySecuritySchemeName = "ApiKey";

    /// <inheritdoc/>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        var authorize = metadata.OfType<IAuthorizeData>().FirstOrDefault();

        if (authorize is null || metadata.OfType<IAllowAnonymous>().Any())
        {
            return;
        }

        var (schemeName, description) = authorize.Policy == AuthorizationPolicies.ApplicationApiKey
            ? (ApiKeySecuritySchemeName, "The API Key is missing, unknown, revoked or expired.")
            : (SecuritySchemeName, "There is no valid session, because nobody signed in or it has expired.");

        operation.Responses ??= [];
        operation.Responses.TryAdd(
            StatusCodes.Status401Unauthorized.ToString(CultureInfo.InvariantCulture),
            new OpenApiResponse { Description = description });

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(schemeName, context.Document)] = []
        });
    }
}
