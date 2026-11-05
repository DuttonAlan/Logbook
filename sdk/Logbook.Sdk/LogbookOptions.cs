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
    private static readonly TimeSpan MaxWaitingTime = TimeSpan.FromDays(1);

    /// <summary>
    ///     Base address of the Logbook server, e.g. <c>https://logbook.example.com</c>.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    ///     API Key of the Application that sends the events.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    ///     The Environment of the events the <c>ILogger</c> provider creates, e.g. <c>Production</c>. Overrides the name
    ///     from <c>IHostEnvironment</c>.
    /// </summary>
    public string? Environment { get; set; }

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
    ///     Maximum size of one event in bytes: <c>message</c>, <c>messageTemplate</c>, <c>exception</c> and
    ///     <c>properties</c> together in UTF-8. A larger event has its long text cut and is marked as truncated, and is
    ///     dropped only if it is still too large. Must not be larger than the setting <c>LogIngestion:MaxEventBytes</c>
    ///     of the server, as one event it rejects costs its whole batch.
    /// </summary>
    public int MaxEventBytes { get; set; } = 256 * 1024;

    /// <summary>
    ///     Maximum number of characters of <c>message</c> and of <c>messageTemplate</c>. Longer text is cut and the event
    ///     is marked as truncated. Must not be larger than the limit of the server, 65,536 characters.
    /// </summary>
    public int MaxMessageLength { get; set; } = 64 * 1024;

    /// <summary>
    ///     Maximum age of an event when it is sent. An older event, e.g. after a long outage, is dropped. Must not be longer
    ///     than the setting <c>LogIngestion:MaxEventAge</c> of the server, as one event it rejects costs its whole batch.
    /// </summary>
    public TimeSpan MaxEventAge { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    ///     How far in the future the timestamp of an event may be when it is sent. An event beyond that is dropped. Must not
    ///     be longer than the setting <c>LogIngestion:MaxFutureOffset</c> of the server.
    /// </summary>
    public TimeSpan MaxFutureOffset { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    ///     Maximum time to wait for the server to answer one request. After that, the attempt counts as failed.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Maximum time disposing the client waits for the remaining events to be sent. This is the only time the client
    ///     makes the host application wait.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Finds a setting the client cannot work with. It does not throw, because the client must not take the host
    ///     application down for a wrong setting.
    /// </summary>
    /// <returns>The name of the first such setting, or null if all values are usable.</returns>
    internal string? FindInvalidSetting()
    {
        return this switch
        {
            { Endpoint: not { IsAbsoluteUri: true, Scheme: "http" or "https" } } => nameof(Endpoint),
            _ when string.IsNullOrWhiteSpace(ApiKey) => nameof(ApiKey),
            { BatchSize: <= 0 } => nameof(BatchSize),
            _ when !IsPositive(FlushInterval) => nameof(FlushInterval),
            { MaxQueueSize: <= 0 } => nameof(MaxQueueSize),
            { MaxQueueBytes: <= 0 } => nameof(MaxQueueBytes),
            { MaxRequestBytes: <= 0 } => nameof(MaxRequestBytes),
            { MaxEventBytes: <= 0 } => nameof(MaxEventBytes),
            { MaxMessageLength: <= 0 } => nameof(MaxMessageLength),
            _ when MaxEventAge <= TimeSpan.Zero => nameof(MaxEventAge),
            _ when MaxFutureOffset < TimeSpan.Zero => nameof(MaxFutureOffset),
            _ when !IsPositive(RequestTimeout) => nameof(RequestTimeout),
            _ when ShutdownTimeout < TimeSpan.Zero || ShutdownTimeout > MaxWaitingTime => nameof(ShutdownTimeout),
            _ => null
        };
    }

    private static bool IsPositive(TimeSpan value) => value > TimeSpan.Zero && value <= MaxWaitingTime;
}
