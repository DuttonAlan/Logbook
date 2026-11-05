// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.IO.Compression;

using Logbook.Core.Application.LogEvents;

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Logbook.Api.LogEvents;

/// <summary>
///     Prepares the body of a request with Log Events: decompresses gzip and enforces the maximum size.
/// </summary>
/// <remarks>
///     A resource filter runs after authorization and before the body is read, so a request without a valid API Key
///     costs no reading or decompressing. The errors are <see cref="BadHttpRequestException"/>s, which
///     <c>ProblemDetailsExceptionHandler</c> maps to their status code.
/// </remarks>
/// <param name="options">The limits for received Log Events.</param>
public sealed class LogRequestBodyFilter(IOptions<LogIngestionOptions> options) : IAsyncResourceFilter
{
    private const string Gzip = "gzip";
    private const string Identity = "identity";

    /// <inheritdoc/>
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var request = context.HttpContext.Request;
        var maxBytes = options.Value.MaxRequestBytes;

        if (request.ContentLength > maxBytes)
        {
            throw new BadHttpRequestException(
                $"The request body must not be larger than {maxBytes} bytes.",
                StatusCodes.Status413PayloadTooLarge);
        }

        // Replaces the default limit of the server, which would otherwise win if a higher limit is configured.
        if (context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeLimit)
        {
            sizeLimit.MaxRequestBodySize = maxBytes;
        }

        var originalBody = request.Body;
        var isGzip = IsGzip(request);

        // With gzip, the limit applies to the bytes received as well as to the decompressed bytes.
        var limitedBody = new SizeLimitedBodyStream(originalBody, maxBytes);
        await using var gzip = isGzip ? new GZipStream(limitedBody, CompressionMode.Decompress, leaveOpen: true) : null;

        request.Body = isGzip ? new SizeLimitedBodyStream(gzip!, maxBytes) : limitedBody;

        try
        {
            await next();
        }
        finally
        {
            request.Body = originalBody;
        }
    }

    private static bool IsGzip(HttpRequest request)
    {
        var encoding = request.Headers[HeaderNames.ContentEncoding].ToString();

        if (encoding.Equals(Gzip, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (encoding.Length == 0 || encoding.Equals(Identity, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        throw new BadHttpRequestException(
            "The content encoding is not supported. Use gzip or no compression.",
            StatusCodes.Status415UnsupportedMediaType);
    }
}
