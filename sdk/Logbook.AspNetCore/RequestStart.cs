// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Diagnostics;

namespace Logbook.AspNetCore;

/// <summary>
///     What is known about a request when it starts. It is read then, because the pipeline can change it, e.g. when the
///     exception handler runs it again with another path.
/// </summary>
/// <param name="Activity">The activity of the request.</param>
/// <param name="Timestamp">Point in time when the request started.</param>
/// <param name="StartedAt">The timestamp of the clock when the request started, to measure its duration.</param>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path, without the query string, as that can contain secrets.</param>
internal readonly record struct RequestStart(
    Activity? Activity,
    DateTimeOffset Timestamp,
    long StartedAt,
    string Method,
    string Path);
