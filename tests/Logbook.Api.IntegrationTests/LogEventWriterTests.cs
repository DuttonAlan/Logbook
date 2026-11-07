// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Text.Json;

using Logbook.Core.Abstractions;
using Logbook.Core.Application.LogEvents;
using Logbook.Core.Application.LogEvents.Search;
using Logbook.Core.Application.LogEvents.Tail;
using Logbook.Core.Domain.Entities;
using Logbook.Infrastructure.Ingestion;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Npgsql;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks the queue and the background writer with a fake repository: what happens to Log Events when writing fails
///     or the host stops. Needs no database.
/// </summary>
public sealed class LogEventWriterTests
{
    private const string Secret = "secret-content-of-a-log-event";

    private readonly FakeRepository _repository = new();
    private readonly RecordingLogger<LogEventBatchWriter> _writerLog = new();
    private readonly RecordingLogger<LogEventWriterService> _serviceLog = new();

    [Fact]
    public void Queue_RejectsAWholeBatchWhenFull_AndHasRoomAgainAfterRelease()
    {
        var first = Events(2);
        var queue = CreateQueue(maxQueuedBytes: first.Sum(logEvent => logEvent.GetMemoryBytes()) + 1);

        Assert.True(queue.TryEnqueue(first));
        Assert.False(queue.TryEnqueue(Events(2)));
        Assert.Equal(2, queue.CountWaiting());

        queue.Release(first);

        Assert.True(queue.TryEnqueue(Events(2)));
    }

    [Fact]
    public void Queue_ChargesTheMemoryOfStrings_NotTheirSizeOnTheWire()
    {
        // 100,000 ASCII characters are 100 KB on the wire, but 200 KB as a string.
        var message = LogEvent.Create(Guid.NewGuid(), DateTime.UtcNow, LogEventLevel.Error, new string('x', LogEvent.MessageMaxLength));
        var properties = JsonDocument.Parse($$"""{ "text": "{{new string('x', 100_000)}}" }""").RootElement;
        var withProperties = LogEvent.Create(Guid.NewGuid(), DateTime.UtcNow, LogEventLevel.Error, string.Empty, properties: properties);

        Assert.True(message.GetMemoryBytes() >= 2 * message.GetContentBytes());
        Assert.True(withProperties.GetMemoryBytes() >= 2 * withProperties.GetContentBytes());

        // A limit between the wire size and the memory of one Log Event already rejects a second one.
        var queue = CreateQueue(maxQueuedBytes: 150_000);

        Assert.True(queue.TryEnqueue([withProperties]));
        Assert.False(queue.TryEnqueue(Events(1)));
    }

    [Fact]
    public void Queue_WhenEmpty_TakesABatchLargerThanTheLimit()
    {
        var queue = CreateQueue(maxQueuedBytes: 1);

        Assert.True(queue.TryEnqueue(Events(3)));
        Assert.False(queue.TryEnqueue(Events(1)));
    }

    [Fact]
    public async Task Writer_SplitsTheQueueIntoBatches_AndWritesEveryLogEventOnce()
    {
        var events = Events(5);
        var queue = CreateQueue();
        queue.TryEnqueue(events);

        await RunServiceUntilAsync(queue, () => _repository.Stored.Count == 5, batchSize: 2);

        Assert.Equal(events.Select(logEvent => logEvent.Id), _repository.Stored.Select(logEvent => logEvent.Id));
        Assert.All(_repository.BatchSizes, size => Assert.True(size <= 2));
    }

    [Fact]
    public async Task Writer_KeepsRetrying_WhileTheDatabaseIsUnavailable()
    {
        var events = Events(3);
        _repository.FailuresBeforeSuccess = 2;

        await CreateWriter().WriteAsync(events, TestContext.Current.CancellationToken);

        Assert.Equal(3, _repository.Stored.Count);
        Assert.Equal(2, _writerLog.Messages.Count(message => message.Contains("Trying again", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("22P05")]
    [InlineData("54000")]
    public async Task Writer_DropsOnlyTheLogEventThePostgresCannotStore(string sqlState)
    {
        var events = Events(9);
        _repository.Poison = (events[4].Id, sqlState);

        await CreateWriter().WriteAsync(events, TestContext.Current.CancellationToken);

        Assert.Equal(events.Where(logEvent => logEvent.Id != events[4].Id).Select(logEvent => logEvent.Id), _repository.Stored.Select(logEvent => logEvent.Id));
        Assert.Contains(events[4].Id.ToString(), Assert.Single(_writerLog.Messages), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Writer_TreatsADuplicateAsAlreadyStored()
    {
        var events = Events(3);
        _repository.Poison = (events[1].Id, PostgresErrorCodes.UniqueViolation);

        await CreateWriter().WriteAsync(events, TestContext.Current.CancellationToken);

        Assert.Equal(2, _repository.Stored.Count);
        Assert.Empty(_writerLog.Messages);
    }

    [Fact]
    public async Task Writer_RetriesAMissingPartition_InsteadOfDroppingTheLogEvent()
    {
        var events = Events(1);
        _repository.FailuresBeforeSuccess = 1;
        _repository.FailureSqlState = PostgresErrorCodes.CheckViolation;

        await CreateWriter().WriteAsync(events, TestContext.Current.CancellationToken);

        Assert.Single(_repository.Stored);
    }

    [Fact]
    public async Task Writer_NeverLogsTheExceptionOfPostgres_BecauseItCanContainTheRow()
    {
        _repository.FailuresBeforeSuccess = 1;

        await CreateWriter().WriteAsync(Events(1), TestContext.Current.CancellationToken);

        Assert.All(_writerLog.Messages, message => Assert.DoesNotContain(Secret, message, StringComparison.Ordinal));
        Assert.Empty(_writerLog.Exceptions);
    }

    [Fact]
    public async Task Service_OnShutdown_WritesTheQueue_EvenIfTheBatchWasStillCollecting()
    {
        var queue = CreateQueue();
        using var service = CreateService(queue, writeInterval: TimeSpan.FromHours(1));

        await service.StartAsync(TestContext.Current.CancellationToken);
        queue.TryEnqueue(Events(5));
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(5, _repository.Stored.Count);
        Assert.Empty(_serviceLog.Messages);
    }

    [Fact]
    public async Task Service_OnShutdown_WithAnUnavailableDatabase_ReportsTheLossInsteadOfHanging()
    {
        _repository.FailuresBeforeSuccess = int.MaxValue;

        var queue = CreateQueue();
        using var service = CreateService(queue);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await service.StartAsync(TestContext.Current.CancellationToken);
        queue.TryEnqueue(Events(5));
        await service.StopAsync(deadline.Token);

        Assert.Contains("5 Log Events", Assert.Single(_serviceLog.Messages), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Service_OnShutdown_WithAWriteThatHangs_LogsTheLossBeforeStopping()
    {
        // The host gives this service whatever is left of its timeout, which the web server may have used up.
        _repository.HangUntilCancelled = true;

        var queue = CreateQueue();
        using var service = CreateService(queue);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await service.StartAsync(TestContext.Current.CancellationToken);
        queue.TryEnqueue(Events(3));
        await service.StopAsync(deadline.Token);

        // Checked right after stopping, because the process can end at this point.
        Assert.Contains("3 Log Events", Assert.Single(_serviceLog.Messages), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Service_OnShutdown_WithNoTimeLeft_StillLogsTheLoss()
    {
        var queue = CreateQueue();
        using var service = CreateService(queue, writeInterval: TimeSpan.FromHours(1));

        await service.StartAsync(TestContext.Current.CancellationToken);
        queue.TryEnqueue(Events(2));
        await service.StopAsync(new CancellationToken(canceled: true));

        Assert.Contains("2 Log Events", Assert.Single(_serviceLog.Messages), StringComparison.Ordinal);
    }

    private static LogEventQueue CreateQueue(long maxQueuedBytes = 1024 * 1024)
    {
        return new LogEventQueue(
            Options.Create(new LogIngestionOptions { MaxQueuedBytes = maxQueuedBytes }),
            NullLogger<LogEventQueue>.Instance);
    }

    private static List<LogEvent> Events(int count)
    {
        return [.. Enumerable.Range(0, count).Select(_ => LogEvent.Create(Guid.NewGuid(), DateTime.UtcNow, LogEventLevel.Error, Secret))];
    }

    private LogEventBatchWriter CreateWriter()
    {
        var services = new ServiceCollection()
            .AddSingleton<ILogEventRepository>(_repository)
            .BuildServiceProvider();

        return new LogEventBatchWriter(services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, _writerLog);
    }

    private LogEventWriterService CreateService(
        LogEventQueue queue,
        int batchSize = 100,
        TimeSpan? writeInterval = null)
    {
        return new LogEventWriterService(
            queue,
            CreateWriter(),
            new LogEventFeed(TimeProvider.System),
            Options.Create(new LogIngestionOptions { WriteBatchSize = batchSize, WriteInterval = writeInterval ?? TimeSpan.FromMilliseconds(10) }),
            TimeProvider.System,
            _serviceLog);
    }

    private async Task RunServiceUntilAsync(LogEventQueue queue, Func<bool> condition, int batchSize)
    {
        using var service = CreateService(queue, batchSize);

        await service.StartAsync(TestContext.Current.CancellationToken);

        while (!condition())
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    private sealed class FakeRepository : ILogEventRepository
    {
        public bool HangUntilCancelled { get; set; }

        public int FailuresBeforeSuccess { get; set; }

        public string FailureSqlState { get; set; } = PostgresErrorCodes.CannotConnectNow;

        public (Guid Id, string SqlState)? Poison { get; set; }

        public List<LogEvent> Stored { get; } = [];

        public List<int> BatchSizes { get; } = [];

        public async Task AddRange(IReadOnlyCollection<LogEvent> logEvents, CancellationToken cancellationToken = default)
        {
            // Like the real repository, whose database calls fail on a cancelled token.
            cancellationToken.ThrowIfCancellationRequested();

            if (HangUntilCancelled)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (FailuresBeforeSuccess > 0)
            {
                FailuresBeforeSuccess--;

                throw new PostgresException(Secret, "ERROR", "ERROR", FailureSqlState);
            }

            if (Poison is { } poison && logEvents.Any(logEvent => logEvent.Id == poison.Id))
            {
                throw new PostgresException(Secret, "ERROR", "ERROR", poison.SqlState);
            }

            BatchSizes.Add(logEvents.Count);
            Stored.AddRange(logEvents);
        }

        public Task<IReadOnlyList<LogEvent>> Search(LogEventFilter filter, LogEventCursor? after, int take, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<LogEvent>> GetWritten(WrittenLogEvents written, LogEventFilter filter, int take, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<long> DeleteByApplication(Guid applicationId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<long> DeleteOlderThan(Guid applicationId, DateTime before, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<DateOnly?> DropOldestPartitionBefore(DateOnly day, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public List<Exception> Exceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));

            if (exception is not null)
            {
                Exceptions.Add(exception);
            }
        }
    }
}
