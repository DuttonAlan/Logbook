// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Diagnostics;

using Microsoft.AspNetCore.Http;

namespace Logbook.AspNetCore;

/// <summary>
///     Measures each request. It wraps the whole pipeline, so it also sees what the exception handler makes of a failure.
/// </summary>
/// <param name="next">The rest of the pipeline.</param>
/// <param name="recorder">Records the finished request.</param>
internal sealed class RequestTrackingMiddleware(RequestDelegate next, SpanRecorder recorder)
{
    /// <summary>
    ///     Runs the rest of the pipeline and records the request.
    /// </summary>
    /// <param name="context">The context of the request.</param>
    /// <returns>A Task that completes with the rest of the pipeline.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!recorder.ShouldTrackRequest(context.Request.Path))
        {
            await next(context);
            return;
        }

        var start = new RequestStart(
            Activity.Current,
            recorder.TimeProvider.GetUtcNow(),
            recorder.TimeProvider.GetTimestamp(),
            context.Request.Method,
            context.Request.PathBase + context.Request.Path);

        var threw = true;

        try
        {
            await next(context);

            threw = false;
        }
        finally
        {
            recorder.RecordRequest(start, context, threw);
        }
    }
}
