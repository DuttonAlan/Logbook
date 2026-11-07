// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Logbook.AspNetCore;

/// <summary>
///     Puts the <see cref="RequestTrackingMiddleware"/> in front of the pipeline, so that adding the package is enough.
/// </summary>
/// <param name="recorder">Tells whether requests are to be tracked.</param>
internal sealed class RequestTrackingStartupFilter(SpanRecorder recorder) : IStartupFilter
{
    /// <inheritdoc/>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            if (recorder.IsEnabled && recorder.Options.TrackRequests)
            {
                app.UseMiddleware<RequestTrackingMiddleware>(recorder);
            }

            next(app);
        };
    }
}
