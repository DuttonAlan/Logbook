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

    // Far above any sensible value, and well within what timers accept.
    private static readonly TimeSpan MaxFlushInterval = TimeSpan.FromDays(1);

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

    /// <summary>
    ///     Maximum memory the events in the buffer may use, in bytes, counting strings at two bytes per character. When
    ///     the buffer is full, the oldest events are dropped.
    /// </summary>
    /// <remarks>
    ///     <see cref="MaxQueueSize"/> alone does not protect the host application, because a single event can be hundreds
    ///     of kilobytes.
    /// </remarks>
    public long MaxQueueBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>
    ///     Maximum size of the JSON of one request before compression, in bytes. Must not be larger than the setting
    ///     <c>LogIngestion:MaxRequestBytes</c> of the server, which applies before and after decompression.
    /// </summary>
    public int MaxRequestBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    ///     Checks whether the client can work with these options. It does not throw, because the client must not take
    ///     the host application down for a wrong setting.
    /// </summary>
    /// <returns><see langword="true"/> if all values are usable.</returns>
    internal bool IsValid()
    {
        return Endpoint is { IsAbsoluteUri: true, Scheme: "http" or "https" }
            && !string.IsNullOrWhiteSpace(ApiKey)
            && BatchSize > 0
            && FlushInterval > TimeSpan.Zero
            && FlushInterval <= MaxFlushInterval
            && MaxQueueSize > 0
            && MaxQueueBytes > 0
            && MaxRequestBytes > 0;
    }
}
