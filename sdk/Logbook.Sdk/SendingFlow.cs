// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Sdk;

/// <summary>
///     Tells whether the current code runs as part of the SDK sending events.
/// </summary>
/// <remarks>
///     Dependency tracking must not track these requests: each one would create an event to send, which would need another
///     request, for as long as the application runs. Comparing addresses would also hide requests of the application to the
///     same server. This flag is set on the flow of the send loop only, which does not flow from or to the application.
/// </remarks>
internal static class SendingFlow
{
    private static readonly AsyncLocal<bool> IsSending = new();

    /// <summary>
    ///     Whether the current code runs as part of the SDK sending events.
    /// </summary>
    public static bool IsCurrent => IsSending.Value;

    /// <summary>
    ///     Marks the current flow as sending events.
    /// </summary>
    public static void Mark() => IsSending.Value = true;
}
