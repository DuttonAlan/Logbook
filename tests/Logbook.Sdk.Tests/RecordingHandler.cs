// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text.Json;

namespace Logbook.Sdk.Tests;

/// <summary>
///     Stands in for the server: records every request and answers as the test decides, by default with <c>202</c>.
/// </summary>
/// <param name="timeProvider">The clock to record when requests arrive.</param>
/// <param name="respond">Decides the response, given the zero-based number of the request.</param>
internal sealed class RecordingHandler(
    TimeProvider timeProvider,
    Func<int, CancellationToken, Task<HttpResponseMessage>>? respond = null) : HttpMessageHandler
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    private readonly List<ReceivedRequest> _requests = [];
    private readonly SemaphoreSlim _received = new(0);

    /// <summary>
    ///     All requests so far.
    /// </summary>
    public IReadOnlyList<ReceivedRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>
    ///     Waits for the next request, and fails the test if it does not come. The limit is only there so that a bug ends
    ///     in a failure instead of a test that hangs.
    /// </summary>
    /// <returns>A task that completes when another request arrived.</returns>
    public async Task WaitForRequestAsync()
    {
        Assert.True(await _received.WaitAsync(WaitLimit, TestContext.Current.CancellationToken), "No request arrived.");
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var received = await ReceivedRequest.ReadAsync(request, timeProvider.GetUtcNow(), cancellationToken);
        int number;

        lock (_requests)
        {
            number = _requests.Count;
            _requests.Add(received);
        }

        _received.Release();

        if (respond is not null)
        {
            return await respond(number, cancellationToken);
        }

        return new HttpResponseMessage(HttpStatusCode.Accepted);
    }
}

/// <summary>
///     A request as the server would see it.
/// </summary>
/// <param name="Request">The request. Its content has been read.</param>
/// <param name="Body">The JSON of the body after decompression.</param>
/// <param name="ArrivedAt">The time of the clock when the request arrived.</param>
/// <param name="CurrentActivity">The Activity that was current on the sender while sending.</param>
internal sealed record ReceivedRequest(HttpRequestMessage Request, string Body, DateTimeOffset ArrivedAt, Activity? CurrentActivity)
{
    /// <summary>
    ///     The messages of the Log Events in the body.
    /// </summary>
    public IReadOnlyList<string> Messages
    {
        get
        {
            using var document = JsonDocument.Parse(Body);

            return [.. document.RootElement.EnumerateArray().Select(element => element.GetProperty("message").GetString()!)];
        }
    }

    /// <summary>
    ///     Reads a request.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="arrivedAt">The time of the clock.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The read request.</returns>
    public static async Task<ReceivedRequest> ReadAsync(HttpRequestMessage request, DateTimeOffset arrivedAt, CancellationToken cancellationToken)
    {
        await using var content = await request.Content!.ReadAsStreamAsync(cancellationToken);
        await using var gzip = new GZipStream(content, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);

        return new ReceivedRequest(request, await reader.ReadToEndAsync(cancellationToken), arrivedAt, Activity.Current);
    }
}
