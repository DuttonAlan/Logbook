// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Sdk;

/// <summary>
///     Configuration of the Logbook client.
/// </summary>
/// <remarks>
///     The defaults are chosen so the client never blocks or slows down the host application: events wait in a bounded
///     in-memory buffer and are sent in batches in the background. When the buffer is full, the oldest events are dropped.
/// </remarks>
public sealed class LogbookOptions
{
    /// <summary>
    ///     Name of the configuration section, e.g. <c>Logbook</c> in <c>appsettings.json</c>.
    /// </summary>
    public const string SectionName = "Logbook";

    /// <summary>
    ///     Base address of the Logbook server, e.g. <c>https://logbook.example.com</c>.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    ///     API Key of the Application that sends the events.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    ///     Maximum number of events sent in one request.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    ///     Maximum time an event waits in the buffer before it is sent.
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Maximum number of events kept in memory. When the buffer is full, the oldest events are dropped.
    /// </summary>
    public int MaxQueueSize { get; set; } = 10_000;
}
