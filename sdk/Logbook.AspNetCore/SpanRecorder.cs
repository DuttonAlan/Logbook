// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Diagnostics;

using Logbook.Sdk;

using Microsoft.AspNetCore.Http;

namespace Logbook.AspNetCore;

/// <summary>
///     Turns what the application does into spans for the client. It never throws.
/// </summary>
/// <remarks>
///     This runs on the threads of the application, so it does nothing but read a few values and add the span to the buffer.
/// </remarks>
/// <param name="client">The client to send with.</param>
/// <param name="options">What to track.</param>
/// <param name="environment">The Environment of the spans.</param>
/// <param name="timeProvider">The clock for the timestamps and durations.</param>
internal sealed class SpanRecorder(
    LogbookClient client,
    LogbookTelemetryOptions options,
    string? environment,
    TimeProvider timeProvider)
{
    // The status for a request that ended because the client went away, as nginx has made common. It has no status of its
    // own, and it is not a failure of the application.
    private const int ClientClosedRequest = 499;

    // A PathString throws without the leading slash, and an empty one would match every request.
    private readonly PathString[] _excludedPaths =
    [
        .. options.ExcludedRequestPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Select(path => new PathString(path.StartsWith('/') ? path : "/" + path))
    ];

    /// <summary>
    ///     What to track.
    /// </summary>
    public LogbookTelemetryOptions Options { get; } = options;

    /// <summary>
    ///     Whether anything can be sent.
    /// </summary>
    public bool IsEnabled => client.IsEnabled;

    /// <summary>
    ///     The clock for the timestamps and durations.
    /// </summary>
    public TimeProvider TimeProvider { get; } = timeProvider;

    /// <summary>
    ///     Checks whether a request is to be tracked.
    /// </summary>
    /// <param name="path">The path of the request.</param>
    /// <returns><see langword="true"/> if it is to be tracked.</returns>
    public bool ShouldTrackRequest(PathString path)
    {
        foreach (var excluded in _excludedPaths)
        {
            if (path.StartsWithSegments(excluded))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Records a finished request.
    /// </summary>
    /// <param name="request">What was known when it started.</param>
    /// <param name="context">The context of the request.</param>
    /// <param name="threw">Whether an exception left the pipeline.</param>
    public void RecordRequest(in RequestStart request, HttpContext context, bool threw)
    {
        try
        {
            var duration = TimeProvider.GetElapsedTime(request.StartedAt);
            var wasAborted = threw && context.RequestAborted.IsCancellationRequested;
            var statusCode = wasAborted
                ? ClientClosedRequest
                : threw && !context.Response.HasStarted ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;

            var isW3C = request.Activity?.IdFormat == ActivityIdFormat.W3C;

            client.Enqueue(new LogbookSpan(
                request.Timestamp,
                LogbookSpanKind.Request,
                $"{request.Method} {GetRoute(context) ?? "(no route)"}",
                duration.TotalMilliseconds,
                wasAborted || (!threw && statusCode < StatusCodes.Status500InternalServerError),
                isW3C ? request.Activity!.TraceId.ToHexString() : null,
                isW3C ? request.Activity!.SpanId.ToHexString() : null,
                isW3C ? ToParentId(request.Activity!.ParentSpanId) : null,
                environment,
                statusCode,
                data: request.Path));
        }
#pragma warning disable CA1031 // The SDK must never throw into the host application.
        catch (Exception)
#pragma warning restore CA1031
        {
            // One span is not worth failing a request for.
        }
    }

    /// <summary>
    ///     Records a finished call to a dependency.
    /// </summary>
    /// <param name="activity">The activity of the call.</param>
    /// <param name="name">The name.</param>
    /// <param name="dependencyType">The type of the dependency.</param>
    /// <param name="success">Whether the call succeeded.</param>
    /// <param name="statusCode">The status code of the call.</param>
    /// <param name="target">What was called.</param>
    /// <param name="data">The details of the call.</param>
    public void RecordDependency(
        Activity activity,
        string name,
        string dependencyType,
        bool success,
        int? statusCode,
        string? target,
        string? data)
    {
        client.Enqueue(new LogbookSpan(
            new DateTimeOffset(activity.StartTimeUtc, TimeSpan.Zero),
            LogbookSpanKind.Dependency,
            name,
            activity.Duration.TotalMilliseconds,
            success,
            activity.TraceId.ToHexString(),
            activity.SpanId.ToHexString(),
            ToParentId(activity.ParentSpanId),
            environment,
            statusCode,
            dependencyType,
            target,
            data));
    }

    private static string? GetRoute(HttpContext context)
    {
        // The exception handler removes the endpoint, or replaces it when it runs the pipeline again, but keeps the original.
        var endpoint = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Endpoint ?? context.GetEndpoint();
        var route = (endpoint as Microsoft.AspNetCore.Routing.RouteEndpoint)?.RoutePattern.RawText;

        if (route is null)
        {
            return null;
        }

        return route.StartsWith('/') ? route : "/" + route;
    }

    // A span without a parent has the Id of all zeros.
    private static string? ToParentId(ActivitySpanId parentSpanId)
    {
        return parentSpanId == default ? null : parentSpanId.ToHexString();
    }
}
