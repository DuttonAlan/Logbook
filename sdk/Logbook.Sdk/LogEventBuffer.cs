// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Sdk;

/// <summary>
///     The bounded buffer between the host application and the sender, limited by number of events and by memory. When a
///     limit is reached, the oldest events are dropped.
/// </summary>
/// <remarks>
///     The sender only looks at events and removes them once the server has accepted them, so a batch that is being sent
///     or retried stays in the buffer: it counts towards the limits and is the first to be dropped. A
///     <c>Channel&lt;T&gt;</c> cannot do this, because an event that was read from it is gone. The lock is only held for
///     short operations without any I/O, so adding an event does not make the host application wait.
/// </remarks>
internal sealed class LogEventBuffer
{
    private readonly object _lock = new();
    private readonly Queue<BufferedEvent> _events = new();
    private readonly int _maxCount;
    private readonly long _maxBytes;
    private readonly int _batchCount;
    private readonly long _batchBytes;
    private readonly TimeProvider _timeProvider;

    private long _bytes;
    private long _lastSequence;
    private long _droppedCount;
    private int _unreportedFullCount;
    private TaskCompletionSource<bool>? _signal;

    /// <summary>
    ///     Creates the buffer.
    /// </summary>
    /// <param name="maxCount">Maximum number of events.</param>
    /// <param name="maxBytes">Maximum memory of the events in bytes.</param>
    /// <param name="batchCount">Number of events at which a batch is due without waiting.</param>
    /// <param name="batchBytes">Memory of the events at which a batch is due without waiting.</param>
    /// <param name="timeProvider">The clock to measure how long events have waited.</param>
    public LogEventBuffer(int maxCount, long maxBytes, int batchCount, long batchBytes, TimeProvider timeProvider)
    {
        _maxCount = maxCount;
        _maxBytes = maxBytes;

        // A batch must be able to become due before the limits start to drop events.
        _batchCount = Math.Min(batchCount, maxCount);
        _batchBytes = Math.Min(batchBytes, maxBytes);
        _timeProvider = timeProvider;
    }

    /// <summary>
    ///     The number of events in the buffer.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _events.Count;
            }
        }
    }

    /// <summary>
    ///     The memory of the events in the buffer in bytes.
    /// </summary>
    public long Bytes
    {
        get
        {
            lock (_lock)
            {
                return _bytes;
            }
        }
    }

    /// <summary>
    ///     The number of events dropped so far.
    /// </summary>
    public long DroppedCount
    {
        get
        {
            lock (_lock)
            {
                return _droppedCount;
            }
        }
    }

    /// <summary>
    ///     Adds an event, dropping the oldest events if a limit is reached. An event that alone is larger than the memory
    ///     limit is dropped itself, as it could never fit and should not cost the others their place.
    /// </summary>
    /// <param name="logEvent">The event.</param>
    public void Add(LogbookEvent logEvent)
    {
        var bytes = logEvent.GetMemoryBytes();
        var enqueuedAt = _timeProvider.GetTimestamp();

        lock (_lock)
        {
            if (bytes > _maxBytes)
            {
                _droppedCount++;
                _unreportedFullCount++;
                return;
            }

            while (_events.Count >= _maxCount || _bytes + bytes > _maxBytes)
            {
                _bytes -= _events.Dequeue().Bytes;
                _droppedCount++;
                _unreportedFullCount++;
            }

            _events.Enqueue(new BufferedEvent(logEvent, ++_lastSequence, bytes, enqueuedAt));
            _bytes += bytes;

            // The sender needs to know when its timer has to start, and when it does not have to wait any longer.
            if (_signal is not null && (_events.Count == 1 || IsBatchFull()))
            {
                // Continuations run asynchronously, so the sender never runs on the thread of the host application.
                _signal.TrySetResult(true);
                _signal = null;
            }
        }
    }

    /// <summary>
    ///     Checks whether a batch should be sent now: a batch is full, or the oldest event has waited long enough.
    /// </summary>
    /// <param name="flushInterval">Maximum time an event waits before it is sent.</param>
    /// <param name="wait">If no batch is due, the time until one is due, or null while the buffer is empty.</param>
    /// <param name="signal">If no batch is due, completes when the answer may have changed before <paramref name="wait"/> ends.</param>
    /// <returns><see langword="true"/> if a batch is due.</returns>
    public bool IsBatchDue(TimeSpan flushInterval, out TimeSpan? wait, out Task signal)
    {
        lock (_lock)
        {
            wait = null;

            if (_events.Count > 0)
            {
                var remaining = flushInterval - _timeProvider.GetElapsedTime(_events.Peek().EnqueuedAt);

                if (IsBatchFull() || remaining <= TimeSpan.Zero)
                {
                    signal = Task.CompletedTask;
                    return true;
                }

                wait = remaining;
            }

            _signal ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            signal = _signal.Task;

            return false;
        }
    }

    /// <summary>
    ///     Returns the oldest events without removing them.
    /// </summary>
    /// <param name="maxCount">Maximum number of events to return.</param>
    /// <returns>The events, oldest first.</returns>
    public IReadOnlyList<BufferedEvent> Peek(int maxCount)
    {
        lock (_lock)
        {
            var events = new List<BufferedEvent>(Math.Min(maxCount, _events.Count));

            foreach (var bufferedEvent in _events)
            {
                if (events.Count == maxCount)
                {
                    break;
                }

                events.Add(bufferedEvent);
            }

            return events;
        }
    }

    /// <summary>
    ///     Removes events that were delivered. Those that were dropped while they were being sent are already gone.
    /// </summary>
    /// <param name="sequence">The sequence number of the last event to remove.</param>
    /// <param name="notDelivered">How many of them were left out of the request and count as dropped.</param>
    public void RemoveThrough(long sequence, int notDelivered = 0)
    {
        lock (_lock)
        {
            _droppedCount += Math.Min(notDelivered, RemoveThroughCore(sequence));
        }
    }

    /// <summary>
    ///     Removes events that cannot be delivered and counts them as dropped.
    /// </summary>
    /// <param name="sequence">The sequence number of the last event to drop.</param>
    /// <returns>The number of events dropped.</returns>
    public int DropThrough(long sequence)
    {
        lock (_lock)
        {
            var removed = RemoveThroughCore(sequence);
            _droppedCount += removed;

            return removed;
        }
    }

    /// <summary>
    ///     Returns how many events the limits of the buffer have dropped since the last call. Reporting this is left to
    ///     the sender, so the thread of the host application only ever adds to a number.
    /// </summary>
    /// <returns>The number of events.</returns>
    public int TakeUnreportedFullCount()
    {
        lock (_lock)
        {
            var count = _unreportedFullCount;
            _unreportedFullCount = 0;

            return count;
        }
    }

    private int RemoveThroughCore(long sequence)
    {
        var removed = 0;

        while (_events.Count > 0 && _events.Peek().Sequence <= sequence)
        {
            _bytes -= _events.Dequeue().Bytes;
            removed++;
        }

        return removed;
    }

    private bool IsBatchFull() => _events.Count >= _batchCount || _bytes >= _batchBytes;
}
