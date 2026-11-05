// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Builds request bodies for the log endpoint.
/// </summary>
internal static class LogEventBodies
{
    /// <summary>
    ///     A batch with one valid Log Event, optionally with extra JSON members.
    /// </summary>
    /// <param name="members">Extra members, e.g. <c>"category": "Shop"</c>.</param>
    /// <returns>The JSON of the batch.</returns>
    public static string Valid(string members = "")
    {
        return $$"""[{ "timestamp": "{{DateTimeOffset.UtcNow:O}}", "level": "Information", "message": "Hello"{{(members.Length > 0 ? ", " + members : string.Empty)}} }]""";
    }

    /// <summary>
    ///     Creates uncompressed JSON content.
    /// </summary>
    /// <param name="body">The JSON.</param>
    /// <returns>The content.</returns>
    public static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    /// <summary>
    ///     Creates JSON content compressed with gzip.
    /// </summary>
    /// <param name="body">The JSON.</param>
    /// <returns>The content.</returns>
    public static ByteArrayContent Gzip(string body)
    {
        using var buffer = new MemoryStream();

        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(body));
        }

        var content = new ByteArrayContent(buffer.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.ContentEncoding.Add("gzip");

        return content;
    }
}
