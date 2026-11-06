// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Collections.Concurrent;
using System.Threading.Channels;

using Logbook.Core.Abstractions;
using Logbook.Core.Application.LogEvents.Tail;
using Logbook.Core.Domain.Entities;

namespace Logbook.Infrastructure.Ingestion;

/// <summary>
///     Tells the followers of the live tail which Log Events <see cref="LogEventWriterService"/> has written.
/// </summary>
/// <remarks>
///     In memory, so a follower only hears about what this server wrote. A follower that falls behind loses its oldest
///     batches and is told so, as writing must never wait for it.
/// </remarks>
/// <param name="timeProvider">The clock for how long a follower waits.</param>
public sealed class LogEventFeed(TimeProvider timeProvider) : ILogEventFeed
{
    private const int MaxWaitingBatches = 32;

    private readonly ConcurrentDictionary<Subscription, byte> _subscriptions = new();

    /// <inheritdoc/>
    public ILogEventSubscription Subscribe()
    {
        var subscription = new Subscription(this, timeProvider);

        _subscriptions.TryAdd(subscription, 0);

        return subscription;
    }

    /// <summary>
    ///     Tells all followers about Log Events that were written.
    /// </summary>
    /// <param name="logEvents">The Log Events.</param>
    public void Publish(IReadOnlyCollection<LogEvent> logEvents)
    {
        ArgumentNullException.ThrowIfNull(logEvents);

        if (logEvents.Count == 0 || _subscriptions.IsEmpty)
        {
            return;
        }

        var written = new WrittenLogEvents(
            [.. logEvents.Select(logEvent => logEvent.Id)],
            logEvents.Min(logEvent => logEvent.Timestamp),
            logEvents.Max(logEvent => logEvent.Timestamp));

        foreach (var subscription in _subscriptions.Keys)
        {
            subscription.Write(written);
        }
    }

    private sealed class Subscription : ILogEventSubscription
    {
        private readonly LogEventFeed _feed;
        private readonly TimeProvider _timeProvider;
        private readonly Channel<WrittenLogEvents> _channel;

        private int _hasGap;

        public Subscription(LogEventFeed feed, TimeProvider timeProvider)
        {
            _feed = feed;
            _timeProvider = timeProvider;
            _channel = Channel.CreateBounded<WrittenLogEvents>(
                new BoundedChannelOptions(MaxWaitingBatches) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
                _ => Volatile.Write(ref _hasGap, 1));
        }

        public void Write(WrittenLogEvents written) => _channel.Writer.TryWrite(written);

        public async ValueTask<WrittenLogEvents?> ReadAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timer = new CancellationTokenSource(timeout, _timeProvider);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, cancellationToken);

            try
            {
                await _channel.Reader.WaitToReadAsync(wait.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            if (!_channel.Reader.TryRead(out var written))
            {
                return null;
            }

            return Interlocked.Exchange(ref _hasGap, 0) == 1 ? written with { FollowsGap = true } : written;
        }

        public void Dispose() => _feed._subscriptions.TryRemove(this, out _);
    }
}
