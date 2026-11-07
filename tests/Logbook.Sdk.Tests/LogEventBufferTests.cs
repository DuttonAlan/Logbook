// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Microsoft.Extensions.Time.Testing;

namespace Logbook.Sdk.Tests;

/// <summary>
///     Checks the limits of the buffer and when it reports a batch as due.
/// </summary>
public sealed class LogEventBufferTests
{
    private const long NoLimit = long.MaxValue / 4;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void Add_AtTheCountLimit_DropsTheOldestEvents()
    {
        var buffer = CreateBuffer(maxCount: 3);

        foreach (var message in new[] { "1", "2", "3", "4", "5" })
        {
            buffer.Add(Event(message));
        }

        Assert.Equal(["3", "4", "5"], Messages(buffer));
        Assert.Equal(2, buffer.DroppedCount);
    }

    [Fact]
    public void Add_AtTheByteLimit_DropsTheOldestEvents_AlthoughTheCountLimitIsFarAway()
    {
        var size = Event(new string('x', 1000)).GetMemoryBytes();
        var buffer = CreateBuffer(maxBytes: 3 * size);

        foreach (var first in new[] { 'a', 'b', 'c', 'd' })
        {
            buffer.Add(Event(first + new string('x', 999)));
        }

        Assert.Equal(3, buffer.Count);
        Assert.Equal(3 * size, buffer.Bytes);
        Assert.Equal(1, buffer.DroppedCount);
        Assert.Equal(['b', 'c', 'd'], Messages(buffer).Select(message => message[0]));
    }

    [Fact]
    public void GetMemoryBytes_CountsAllTextAtTwoBytesPerCharacter()
    {
        var empty = Event(string.Empty).GetMemoryBytes();

        var full = new LogbookEvent(
            DateTimeOffset.UnixEpoch,
            LogbookLevel.Error,
            message: "12345",
            messageTemplate: "123",
            category: "1234",
            environment: "12",
            exceptionJson: "{}",
            traceId: "1",
            spanId: "1",
            propertiesJson: "{}");

        Assert.Equal(empty + (20 * sizeof(char)), full.GetMemoryBytes());
    }

    [Fact]
    public void Add_AnEventLargerThanTheByteLimit_DropsOnlyThatEvent()
    {
        var buffer = CreateBuffer(maxBytes: 2000);
        buffer.Add(Event("kept"));

        buffer.Add(Event(new string('x', 2000)));

        Assert.Equal(["kept"], Messages(buffer));
        Assert.Equal(1, buffer.DroppedCount);
    }

    [Fact]
    public void Peek_DoesNotRemoveEvents_AndRemoveThroughDoes()
    {
        var buffer = CreateBuffer();
        buffer.Add(Event("1"));
        buffer.Add(Event("2"));
        buffer.Add(Event("3"));

        var peeked = buffer.Peek(2);

        Assert.Equal(["1", "2"], peeked.Select(item => item.Item.Message));
        Assert.Equal(3, buffer.Count);

        buffer.RemoveThrough(peeked[^1].Sequence);

        Assert.Equal(["3"], Messages(buffer));
        Assert.Equal(0, buffer.DroppedCount);
    }

    [Fact]
    public void AnEventBeingSent_CountsTowardsTheLimits_AndIsTheFirstToBeDropped()
    {
        var buffer = CreateBuffer(maxCount: 2);
        buffer.Add(Event("1"));
        buffer.Add(Event("2"));
        var beingSent = buffer.Peek(2);

        buffer.Add(Event("3"));

        Assert.Equal(["2", "3"], Messages(buffer));
        Assert.Equal(1, buffer.DroppedCount);

        // Its delivery must not remove the event that arrived in the meantime, nor count anything as dropped again.
        buffer.RemoveThrough(beingSent[^1].Sequence);

        Assert.Equal(["3"], Messages(buffer));
        Assert.Equal(1, buffer.DroppedCount);
    }

    [Fact]
    public void Bytes_ReturnToZero_WhenAllEventsAreGone()
    {
        var buffer = CreateBuffer(maxCount: 2);
        buffer.Add(Event("1"));
        buffer.Add(Event("2"));
        buffer.Add(Event("3"));

        buffer.DropThrough(buffer.Peek(2)[^1].Sequence);

        Assert.Equal(0, buffer.Count);
        Assert.Equal(0, buffer.Bytes);
        Assert.Equal(3, buffer.DroppedCount);
    }

    [Fact]
    public void Add_FromManyThreads_KeepsTheLimitsAndCountsEveryEvent()
    {
        const int Threads = 8;
        const int EventsPerThread = 20_000;
        const int MaxCount = 100;

        var buffer = CreateBuffer(maxCount: MaxCount);

        Parallel.For(0, Threads, new ParallelOptions { MaxDegreeOfParallelism = Threads }, thread =>
        {
            for (var index = 0; index < EventsPerThread; index++)
            {
                buffer.Add(Event("x"));
            }
        });

        Assert.Equal(MaxCount, buffer.Count);
        Assert.Equal(MaxCount * Event("x").GetMemoryBytes(), buffer.Bytes);
        Assert.Equal((Threads * EventsPerThread) - MaxCount, buffer.DroppedCount);

        var sequences = buffer.Peek(MaxCount).Select(item => item.Sequence).ToArray();
        Assert.Equal(sequences.Order(), sequences);
    }

    [Fact]
    public void IsBatchDue_OnAnEmptyBuffer_WaitsForTheSignalWithoutATimer()
    {
        var buffer = CreateBuffer();

        Assert.False(buffer.IsBatchDue(Interval, out var wait, out var signal));

        Assert.Null(wait);
        Assert.False(signal.IsCompleted);

        buffer.Add(Event("first"));

        Assert.True(signal.IsCompleted);
    }

    [Fact]
    public void IsBatchDue_WithAPartialBatch_BecomesDueWhenTheOldestEventHasWaitedTheInterval()
    {
        var buffer = CreateBuffer();
        buffer.Add(Event("1"));
        _time.Advance(TimeSpan.FromSeconds(1.5));
        buffer.Add(Event("2"));

        Assert.False(buffer.IsBatchDue(Interval, out var wait, out _));
        Assert.Equal(TimeSpan.FromSeconds(0.5), wait);

        _time.Advance(TimeSpan.FromSeconds(0.5));

        Assert.True(buffer.IsBatchDue(Interval, out _, out _));
    }

    [Fact]
    public void IsBatchDue_BecomesDueAndSignals_WhenTheBatchCountIsReached()
    {
        var buffer = CreateBuffer(batchCount: 2);
        buffer.Add(Event("1"));

        Assert.False(buffer.IsBatchDue(Interval, out _, out var signal));
        Assert.False(signal.IsCompleted);

        buffer.Add(Event("2"));

        Assert.True(signal.IsCompleted);
        Assert.True(buffer.IsBatchDue(Interval, out _, out _));
    }

    [Fact]
    public void IsBatchDue_BecomesDueAndSignals_WhenTheBatchBytesAreReached()
    {
        var size = Event("x").GetMemoryBytes();
        var buffer = CreateBuffer(batchBytes: 2 * size);
        buffer.Add(Event("x"));

        Assert.False(buffer.IsBatchDue(Interval, out _, out var signal));

        buffer.Add(Event("x"));

        Assert.True(signal.IsCompleted);
        Assert.True(buffer.IsBatchDue(Interval, out _, out _));
    }

    [Fact]
    public void ABatchLargerThanTheBuffer_IsDueBeforeTheLimitDropsEvents()
    {
        var buffer = CreateBuffer(maxCount: 2, batchCount: 100);
        buffer.Add(Event("1"));
        buffer.Add(Event("2"));

        Assert.True(buffer.IsBatchDue(Interval, out _, out _));
        Assert.Equal(0, buffer.DroppedCount);
    }

    private static LogbookEvent Event(string message) => new(DateTimeOffset.UnixEpoch, LogbookLevel.Information, message);

    private static IEnumerable<string> Messages(TelemetryBuffer<LogbookEvent> buffer)
    {
        return buffer.Peek(int.MaxValue).Select(item => item.Item.Message);
    }

    private TelemetryBuffer<LogbookEvent> CreateBuffer(
        int maxCount = int.MaxValue,
        long maxBytes = NoLimit,
        int batchCount = int.MaxValue,
        long batchBytes = NoLimit)
    {
        return new TelemetryBuffer<LogbookEvent>(maxCount, maxBytes, batchCount, batchBytes, _time);
    }
}
