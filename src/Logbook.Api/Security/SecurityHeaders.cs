// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Api.Security;

/// <summary>
///     Adds the security headers to every response.
/// </summary>
internal static class SecurityHeaders
{
    // Everything comes from this server, and it never runs inline scripts. Styles set by the scripts of the web interface
    // go through the DOM, which this does not restrict.
    private const string ContentSecurityPolicy =
        "default-src 'self'; base-uri 'self'; connect-src 'self'; form-action 'self'; frame-ancestors 'none'; "
        + "img-src 'self' data:; object-src 'none'; style-src 'self'";

    /// <summary>
    ///     Adds the middleware. It must come before the exception handler, whose response gets the headers as well.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The application, for chaining.</returns>
    public static IApplicationBuilder UseSecurityHeaders(this WebApplication app)
    {
        // Swagger UI needs inline scripts, and only exists in Development.
        var isDevelopment = app.Environment.IsDevelopment();

        return app.Use((context, next) =>
        {
            // When the response starts, as the exception handler removes the headers set so far.
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;

                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Permissions-Policy"] = "camera=(), geolocation=(), microphone=()";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers["Cross-Origin-Resource-Policy"] = "same-origin";

                if (!(isDevelopment && context.Request.Path.StartsWithSegments("/swagger")))
                {
                    headers.ContentSecurityPolicy = ContentSecurityPolicy;
                }

                return Task.CompletedTask;
            });

            return next();
        });
    }
}
