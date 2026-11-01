using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Tests.Network;

[Trait("Category", "JournalUploadScenario")]
public sealed class VoxStellarPublisherTests
{
    // Worker scheduling may be delayed by instrumentation; shutdown deadlines are asserted separately.
    private static readonly TimeSpan WorkerSchedulingTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task CompletionWaitsForAcceptedWorkAndStopsAcceptingNewWork()
    {
        var handler = new BlockingHandler();
        using var client = new HttpClient(handler);
        using var publisher = new VoxStellarPublisher("1.0.0", "test-key", client, batchInterval: TimeSpan.Zero);
        var request = new VoxStellarApplyRequest
        {
            JournalEvents = [Parse("""{"event":"Scan","BodyName":"Test A 1"}""")],
            CommanderName = "Test Cmdr",
            Enabled = true,
            AllowPublishing = true,
        };
        await publisher.ApplyAsync(request);
        await handler.FirstRequestStarted.Task.WaitAsync(WorkerSchedulingTimeout);
        Task completion = publisher.CompleteAsync();
        try
        {
            Assert.False(completion.IsCompleted);
            Assert.Empty((await publisher.ApplyAsync(request)).QueuedEventNames);
        }
        finally
        {
            handler.ReleaseFirstRequest.TrySetResult();
            await completion.WaitAsync(WorkerSchedulingTimeout);
        }
        Assert.Equal(1, handler.CallCount);
        await publisher.CompleteAsync();
    }

    [Fact]
    public async Task UploadsAllowConnectionReuse()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        using var publisher = new VoxStellarPublisher("1.0.0", "test-key", client, batchInterval: TimeSpan.Zero);
        await publisher.ApplyAsync(
            new VoxStellarApplyRequest
            {
                JournalEvents = [Parse("""{"event":"Scan","BodyID":1}""")],
                CommanderName = "Test Cmdr",
                Enabled = true,
                AllowPublishing = true,
            }
        );
        RecordedRequest request = await handler.Request.Task.WaitAsync(WorkerSchedulingTimeout);
        Assert.NotEqual(true, request.ConnectionClose);
    }

    [Fact]
    public async Task SendsOnlySupportedLiveEventsWithExpectedEnvelopeAndSignature()
    {
        const string sharedKey = "test-shared-key";
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        using var publisher = new VoxStellarPublisher("2.1.3.0", sharedKey, client, batchInterval: TimeSpan.Zero);
        publisher.SetEnabled(true);

        VoxStellarPublicationResult result = await publisher.ApplyAsync(
            new VoxStellarApplyRequest
            {
                JournalEvents =
                [
                    Parse("""{"timestamp":"2026-08-13T22:00:00Z","event":"Scan","BodyName":"Test A 1","BodyID":1}"""),
                    Parse("""{"timestamp":"2026-08-13T22:00:01Z","event":"Docked","StationName":"Test Port"}"""),
                ],
                CommanderName = "Test Cmdr",
                Enabled = true,
                AllowPublishing = true,
            }
        );

        RecordedRequest request = await handler.Request.Task.WaitAsync(WorkerSchedulingTimeout);
        Assert.Equal(["Scan"], result.QueuedEventNames);
        Assert.Empty(result.Warnings);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(WellKnownUris.VoxStellarWebhook, request.Uri);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("SrvSurvey-XP/2.1.3.0", request.UserAgent);

        using var document = JsonDocument.Parse(request.Body);
        Assert.Equal("Test Cmdr", document.RootElement.GetProperty("commander").GetString());
        Assert.Equal("Scan", document.RootElement.GetProperty("data").GetProperty("event").GetString());
        Assert.Equal(ExpectedSignature(sharedKey, request.Body), request.Signature);
        Assert.Equal(request.Signature.ToLowerInvariant(), request.Signature);
    }

    /// <summary>A foreign identity boundary stops later events from being assigned to the requested commander.</summary>
    [Theory]
    [InlineData("{\"event\":\"LoadGame\",\"Commander\":\"Other\",\"FID\":\"F123\"}")]
    [InlineData("{\"event\":\"LoadGame\",\"Commander\":\"Test Cmdr\",\"FID\":\"F999\"}")]
    [InlineData("{\"event\":\"Commander\",\"Name\":\"Other\"}")]
    [InlineData("{\"event\":\"Commander\",\"Name\":42}")]
    public async Task ForeignCommanderStopsTheCapturedBatch(string boundary)
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var publisher = new VoxStellarPublisher("2.1.3.0", "test-key", client, batchInterval: TimeSpan.Zero);
        try
        {
            VoxStellarPublicationResult result = await publisher.ApplyAsync(
                new VoxStellarApplyRequest
                {
                    CommanderName = "Test Cmdr",
                    FrontierId = "F123",
                    Enabled = true,
                    AllowPublishing = true,
                    JournalEvents =
                    [
                        Parse("{\"event\":\"LoadGame\",\"Commander\":\"Test Cmdr\",\"FID\":\"F123\"}"),
                        Parse("{\"event\":\"Scan\",\"BodyName\":\"Own Body\"}"),
                        Parse(boundary),
                        Parse("{\"event\":\"Scan\",\"BodyName\":\"Foreign Body\"}"),
                    ],
                }
            );
            Assert.Equal(["Scan"], result.QueuedEventNames);
            await publisher.CompleteAsync().WaitAsync(WorkerSchedulingTimeout);
        }
        finally
        {
            publisher.Dispose();
        }
        RecordedRequest request = Assert.Single(handler.Requests);
        Assert.Contains("Own Body", Encoding.UTF8.GetString(request.Body), StringComparison.Ordinal);
        Assert.DoesNotContain("Foreign Body", Encoding.UTF8.GetString(request.Body), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BootstrapAndDisabledUpdatesNeverReachTheWebhook()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        using var publisher = new VoxStellarPublisher("1.0.0", "test-key", client, batchInterval: TimeSpan.Zero);
        JournalEventEnvelope scan = Parse("""{"event":"Scan","BodyName":"Test A 1"}""");

        VoxStellarPublicationResult bootstrap = await publisher.ApplyAsync(
            new VoxStellarApplyRequest
            {
                JournalEvents = [scan],
                CommanderName = "Test Cmdr",
                Enabled = true,
                AllowPublishing = false,
            }
        );
        VoxStellarPublicationResult disabled = await publisher.ApplyAsync(
            new VoxStellarApplyRequest
            {
                JournalEvents = [scan],
                CommanderName = "Test Cmdr",
                Enabled = false,
                AllowPublishing = true,
            }
        );

        await publisher.CompleteAsync();
        Assert.Empty(bootstrap.QueuedEventNames);
        Assert.Empty(disabled.QueuedEventNames);
        Assert.False(handler.Request.Task.IsCompleted);
    }

    [Fact]
    public async Task DisablingConsentDropsQueuedWorkThatHasNotStarted()
    {
        var handler = new BlockingHandler();
        using var client = new HttpClient(handler);
        using var publisher = new VoxStellarPublisher("1.0.0", "test-key", client, batchInterval: TimeSpan.Zero);
        publisher.SetEnabled(true);
        await publisher.ApplyAsync(
            new VoxStellarApplyRequest
            {
                JournalEvents =
                [
                    Parse("""{"event":"Scan","BodyName":"Test A 1"}"""),
                    Parse("""{"event":"ScanOrganic","Body":"Test A 1"}"""),
                ],
                CommanderName = "Test Cmdr",
                Enabled = true,
                AllowPublishing = true,
            }
        );
        await handler.FirstRequestStarted.Task.WaitAsync(WorkerSchedulingTimeout);

        publisher.SetEnabled(false);
        handler.ReleaseFirstRequest.TrySetResult();
        await publisher.CompleteAsync();

        Assert.Equal(1, handler.CallCount);
    }

    /// <summary>Preserves the transport-start consent fence with immediate and explicitly timed batched uploads.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisablingConsentWaitsUntilAuthorizedTransportHasStarted(bool useDefaultBatchInterval)
    {
        var clock = new BatchClock();
        var handler = new TransportStartBlockingHandler();
        using var client = new HttpClient(handler);
        using var publisher = new VoxStellarPublisher(
            "1.0.0",
            "test-key",
            client,
            timeProvider: clock,
            batchInterval: useDefaultBatchInterval ? null : TimeSpan.Zero
        );
        publisher.SetEnabled(true);

        await publisher.ApplyAsync(
            new VoxStellarApplyRequest
            {
                JournalEvents = [Parse("""{"event":"Scan","BodyName":"Test A 1"}""")],
                CommanderName = "Test Cmdr",
                Enabled = true,
                AllowPublishing = true,
            }
        );
        Task? disableTask = null;
        Task? batchTick = null;
        try
        {
            if (useDefaultBatchInterval)
            {
                ManualTimer timer = await clock.NextTimerAsync();
                batchTick = Task.Factory.StartNew(
                    timer.Fire,
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default
                );
            }

            await handler.TransportStartEntered.Task.WaitAsync(WorkerSchedulingTimeout);

            var disableStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            disableTask = Task.Factory.StartNew(
                () =>
                {
                    disableStarted.TrySetResult();
                    publisher.SetEnabled(false);
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            );
            await disableStarted.Task.WaitAsync(WorkerSchedulingTimeout);
            await Task.Delay(50);
            Assert.False(disableTask.IsCompleted);
        }
        finally
        {
            handler.AllowTransportStart.Set();
            if (disableTask is not null)
            {
                await disableTask.WaitAsync(WorkerSchedulingTimeout);
            }
            if (batchTick is not null)
            {
                await batchTick.WaitAsync(WorkerSchedulingTimeout);
            }
        }
    }

    [Fact]
    public async Task MissingBuildKeyReportsConfigurationWithoutSending()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        using var publisher = new VoxStellarPublisher("1.0.0", sharedKey: null, client);

        VoxStellarPublicationResult result = await publisher.ApplyAsync(
            new VoxStellarApplyRequest
            {
                JournalEvents = [Parse("""{"event":"FSDJump","StarSystem":"Test A"}""")],
                CommanderName = "Test Cmdr",
                Enabled = true,
                AllowPublishing = true,
            }
        );

        Assert.False(publisher.IsConfigured);
        Assert.Empty(result.QueuedEventNames);
        Assert.Contains(result.Warnings, warning => warning.Contains("signing key", StringComparison.Ordinal));
        Assert.False(handler.Request.Task.IsCompleted);
    }

    [Fact]
    public async Task TimerCoalescesSeparateUpdatesAndBoundsBatchesWithoutChangingEnvelopes()
    {
        var clock = new BatchClock();
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var summaries = Channel.CreateUnbounded<string>();
        using var publisher = new VoxStellarPublisher(
            "1.0.0",
            "test-key",
            client,
            log: message => summaries.Writer.TryWrite(message),
            timeProvider: clock
        );
        await publisher.ApplyAsync(CreateRequest([Parse("""{"event":"Scan","BodyID":0}""")], "First Cmdr"));
        ManualTimer firstTimer = await clock.NextTimerAsync();
        Assert.Empty(handler.Requests);
        await publisher.ApplyAsync(
            CreateRequest(
                Enumerable.Range(1, 104).Select(id => Parse($$"""{"event":"Scan","BodyID":{{id}}}""")).ToArray(),
                "Second Cmdr"
            )
        );
        Assert.Empty(handler.Requests);
        firstTimer.Fire();
        string firstSummary = await summaries.Reader.ReadAsync().AsTask().WaitAsync(WorkerSchedulingTimeout);
        Assert.Contains("100 accepted", firstSummary, StringComparison.Ordinal);
        Assert.Equal(100, handler.Requests.Count);
        ManualTimer secondTimer = await clock.NextTimerAsync();
        secondTimer.Fire();
        string secondSummary = await summaries.Reader.ReadAsync().AsTask().WaitAsync(WorkerSchedulingTimeout);
        Assert.Contains("5 accepted", secondSummary, StringComparison.Ordinal);
        Assert.Equal(105, handler.Requests.Count);
        RecordedRequest[] requests = handler.Requests.ToArray();
        for (int index = 0; index < requests.Length; index++)
        {
            using var body = JsonDocument.Parse(requests[index].Body);
            Assert.Equal(index, body.RootElement.GetProperty("data").GetProperty("BodyID").GetInt32());
            Assert.Equal(
                index == 0 ? "First Cmdr" : "Second Cmdr",
                body.RootElement.GetProperty("commander").GetString()
            );
            Assert.Equal(ExpectedSignature("test-key", requests[index].Body), requests[index].Signature);
        }
    }

    [Fact]
    public async Task RejectionsAndFailuresProduceOneSummaryAndDoNotStopLaterEvents()
    {
        var clock = new BatchClock();
        var handler = new OutcomeHandler(HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, null, HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var summary = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var publisher = new VoxStellarPublisher(
            "1.0.0",
            "test-key",
            client,
            log: message => summary.TrySetResult(message),
            timeProvider: clock
        );
        await publisher.ApplyAsync(CreateRequest(Enumerable.Repeat(Parse("""{"event":"Scan"}"""), 4).ToArray()));
        (await clock.NextTimerAsync()).Fire();
        string message = await summary.Task.WaitAsync(WorkerSchedulingTimeout);
        Assert.Equal(4, handler.CallCount);
        Assert.Contains("1 accepted, 2 rejected, 1 failed", message, StringComparison.Ordinal);
        Assert.Contains("HTTP 400: 2", message, StringComparison.Ordinal);
        Assert.Contains("HttpRequestException: 1", message, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisablingAndReenablingConsentInvalidatesTheWaitingBatch()
    {
        var clock = new BatchClock();
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var summary = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var publisher = new VoxStellarPublisher(
            "1.0.0",
            "test-key",
            client,
            log: message => summary.TrySetResult(message),
            timeProvider: clock
        );
        await publisher.ApplyAsync(CreateRequest([Parse("""{"event":"Scan","BodyID":1}""")]));
        ManualTimer timer = await clock.NextTimerAsync();
        publisher.SetEnabled(false);
        await publisher.ApplyAsync(CreateRequest([Parse("""{"event":"Scan","BodyID":2}""")]));
        timer.Fire();
        Assert.Contains("1 accepted", await summary.Task.WaitAsync(WorkerSchedulingTimeout), StringComparison.Ordinal);
        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Equal(2, body.RootElement.GetProperty("data").GetProperty("BodyID").GetInt32());
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 0)]
    public async Task ShutdownFlushesWithoutTheTimerAndRespectsRevokedConsent(bool enabled, int expectedRequests)
    {
        var clock = new BatchClock();
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var publisher = new VoxStellarPublisher("1.0.0", "test-key", client, timeProvider: clock);
        try
        {
            await publisher.ApplyAsync(
                CreateRequest([Parse("""{"event":"Scan"}"""), Parse("""{"event":"FSDJump"}""")])
            );
            _ = await clock.NextTimerAsync();
            publisher.SetEnabled(enabled);
            publisher.Dispose();
            publisher.Dispose();
            Assert.Equal(expectedRequests, handler.Requests.Count);
            Assert.Empty((await publisher.ApplyAsync(CreateRequest([Parse("""{"event":"Scan"}""")]))).QueuedEventNames);
        }
        finally
        {
            publisher.Dispose();
        }
    }

    [Fact]
    public async Task ThrowingLogSinkDoesNotStopUploads()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        using var publisher = new VoxStellarPublisher(
            "1.0.0",
            "test-key",
            client,
            log: _ => throw new InvalidOperationException("test logging failure"),
            batchInterval: TimeSpan.Zero
        );
        await publisher.ApplyAsync(CreateRequest([Parse("""{"event":"Scan"}""")]));
        _ = await handler.Request.Task.WaitAsync(WorkerSchedulingTimeout);
        publisher.Dispose();
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AFullQueueProducesOneWarningForTheDroppedEvents()
    {
        var clock = new BatchClock();
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        using var publisher = new VoxStellarPublisher("1.0.0", "test-key", client, timeProvider: clock);
        JournalEventEnvelope scan = Parse("""{"event":"Scan"}""");
        await publisher.ApplyAsync(CreateRequest([scan]));
        _ = await clock.NextTimerAsync();
        VoxStellarPublicationResult result = await publisher.ApplyAsync(
            CreateRequest(Enumerable.Repeat(scan, 5000).ToArray())
        );
        Assert.Equal(4096, result.QueuedEventNames.Count);
        Assert.Contains("904 event(s)", Assert.Single(result.Warnings), StringComparison.Ordinal);
        publisher.SetEnabled(false);
        publisher.Dispose();
        Assert.Empty(handler.Requests);
    }

    /// <summary>Shutdown requests cancellation within its bounded deadline for an unresponsive upload.</summary>
    [Fact]
    public async Task ShutdownCancelsAnUnresponsiveUploadWithinItsDeadline()
    {
        var handler = new CancellationHandler();
        using var client = new HttpClient(handler);
        using var publisher = new VoxStellarPublisher("1.0.0", "test-key", client, batchInterval: TimeSpan.Zero);
        await publisher.ApplyAsync(CreateRequest([Parse("""{"event":"Scan"}"""), Parse("""{"event":"FSDJump"}""")]));
        await handler.Started.Task.WaitAsync(WorkerSchedulingTimeout);
        await Task.Run(publisher.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(handler.RequestCancellation.IsCancellationRequested);
        Assert.Equal(1, handler.CallCount);
    }

    /// <summary>Accepted outcomes are reported even when cancellation unwinds after bounded shutdown returns.</summary>
    [Fact]
    public async Task ShutdownRetainsCompletedOutcomesFromAnInterruptedBatch()
    {
        var clock = new BatchClock();
        var cancellationCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new CancellationHandler(
            acceptFirstRequest: true,
            cancellationCompletion: cancellationCompletion.Task
        );
        using var client = new HttpClient(handler);
        var logs = new ConcurrentQueue<string>();
        var summaryLogged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var publisher = new VoxStellarPublisher(
            "1.0.0",
            "test-key",
            client,
            log: message =>
            {
                logs.Enqueue(message);
                summaryLogged.TrySetResult();
            },
            timeProvider: clock
        );
        try
        {
            await publisher.ApplyAsync(
                CreateRequest([Parse("""{"event":"Scan"}"""), Parse("""{"event":"FSDJump"}""")])
            );
            (await clock.NextTimerAsync()).Fire();
            await handler.Started.Task.WaitAsync(WorkerSchedulingTimeout);
            await Task.Run(publisher.Dispose).WaitAsync(WorkerSchedulingTimeout);
            Assert.True(handler.RequestCancellation.IsCancellationRequested);
            Assert.Equal(2, handler.CallCount);
            Assert.Empty(logs);
            cancellationCompletion.TrySetResult();
            await summaryLogged.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("1 accepted", Assert.Single(logs), StringComparison.Ordinal);
        }
        finally
        {
            cancellationCompletion.TrySetResult();
        }
    }

    /// <summary>Simulates cancellation whose transport continuation can resume after the bounded shutdown deadline.</summary>
    private sealed class CancellationHandler(bool acceptFirstRequest = false, Task? cancellationCompletion = null)
        : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken RequestCancellation { get; private set; }
        public int CallCount { get; private set; }

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            if (acceptFirstRequest && CallCount == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            RequestCancellation = cancellationToken;
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (cancellationCompletion is not null)
                {
                    await cancellationCompletion.ConfigureAwait(false);
                }
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    [Fact]
    public void ShutdownFlushesWithoutPumpingTheConstructionContext()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new NonPumpingContext());
        try
        {
            using var publisher = new VoxStellarPublisher("1.0.0", "test-key", client);
            Assert.True(
                publisher
                    .ApplyAsync(CreateRequest([Parse("""{"event":"Scan"}"""), Parse("""{"event":"FSDJump"}""")]))
                    .IsCompletedSuccessfully
            );
            publisher.Dispose();
            Assert.Equal(2, handler.Requests.Count);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public async Task RevokingAFullBacklogLeavesRoomForFreshEventsWithoutExtraIntervals()
    {
        var clock = new BatchClock();
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var summary = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var publisher = new VoxStellarPublisher(
            "1.0.0",
            "test-key",
            client,
            log: message => summary.TrySetResult(message),
            timeProvider: clock
        );
        JournalEventEnvelope old = Parse("""{"event":"Scan","BodyID":1}""");
        await publisher.ApplyAsync(CreateRequest([old]));
        ManualTimer timer = await clock.NextTimerAsync();
        VoxStellarPublicationResult oldBacklog = await publisher.ApplyAsync(
            CreateRequest(Enumerable.Repeat(old, 4096).ToArray())
        );
        Assert.Equal(4096, oldBacklog.QueuedEventNames.Count);
        publisher.SetEnabled(false);
        VoxStellarPublicationResult fresh = await publisher.ApplyAsync(
            CreateRequest([Parse("""{"event":"Scan","BodyID":2}""")])
        );
        Assert.Empty(fresh.Warnings);
        Assert.Single(fresh.QueuedEventNames);
        timer.Fire();
        Task winner = await Task.WhenAny(summary.Task, clock.SecondTimerCreated.Task)
            .WaitAsync(WorkerSchedulingTimeout);
        Assert.Same(summary.Task, winner);
        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Equal(2, body.RootElement.GetProperty("data").GetProperty("BodyID").GetInt32());
    }

    private sealed class NonPumpingContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // A blocked UI thread cannot dispatch queued worker continuations during synchronous disposal.
        }
    }

    [Fact]
    public void NegativeBatchIntervalIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new VoxStellarPublisher("1.0.0", "test-key", batchInterval: TimeSpan.FromSeconds(-1))
        );
    }

    private static VoxStellarApplyRequest CreateRequest(
        IReadOnlyList<JournalEventEnvelope> events,
        string commander = "Test Cmdr"
    ) =>
        new()
        {
            JournalEvents = events,
            CommanderName = commander,
            Enabled = true,
            AllowPublishing = true,
        };

    private sealed class BatchClock : TimeProvider
    {
        private readonly Channel<ManualTimer> timers = Channel.CreateUnbounded<ManualTimer>();
        private int timerCount;
        public TaskCompletionSource SecondTimerCreated { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(VoxStellarPublisher.SendInterval, dueTime);
            var timer = new ManualTimer(callback, state);
            timers.Writer.TryWrite(timer);
            if (Interlocked.Increment(ref timerCount) == 2)
            {
                SecondTimerCreated.TrySetResult();
            }
            return timer;
        }

        // This observes worker scheduling, not elapsed batching time. Instrumented CI can
        // delay the worker; fake time still advances only when the test fires a timer.
        public Task<ManualTimer> NextTimerAsync() =>
            timers.Reader.ReadAsync().AsTask().WaitAsync(WorkerSchedulingTimeout);
    }

    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        private int disposed;

        public void Fire()
        {
            if (Volatile.Read(ref disposed) == 0)
            {
                callback(state);
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period) => Volatile.Read(ref disposed) == 0;

        public void Dispose() => Interlocked.Exchange(ref disposed, 1);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OutcomeHandler(params HttpStatusCode?[] outcomes) : HttpMessageHandler
    {
        private int callCount;
        public int CallCount => Volatile.Read(ref callCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            HttpStatusCode? status = outcomes[Interlocked.Increment(ref callCount) - 1];
            return status is { } code
                ? Task.FromResult(new HttpResponseMessage(code))
                : throw new HttpRequestException("sensitive transport details");
        }
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? journalEvent, out string? error),
            error
        );
        return journalEvent!;
    }

    private static string ExpectedSignature(string key, byte[] body)
    {
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), body)).ToLowerInvariant();
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        string ContentType,
        string UserAgent,
        string Signature,
        byte[] Body,
        bool? ConnectionClose
    );

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public ConcurrentQueue<RecordedRequest> Requests { get; } = new();
        public TaskCompletionSource<RecordedRequest> Request { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var recorded = new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Content!.Headers.ContentType!.MediaType!,
                request.Headers.UserAgent.ToString(),
                request.Headers.GetValues("Signature").Single(),
                await request.Content.ReadAsByteArrayAsync(cancellationToken),
                request.Headers.ConnectionClose
            );
            Requests.Enqueue(recorded);
            Request.TrySetResult(recorded);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        public TaskCompletionSource FirstRequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstRequest { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            FirstRequestStarted.TrySetResult();
            await ReleaseFirstRequest.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class TransportStartBlockingHandler : HttpMessageHandler
    {
        public TaskCompletionSource TransportStartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ManualResetEventSlim AllowTransportStart { get; } = new(false);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            TransportStartEntered.TrySetResult();
            AllowTransportStart.Wait(cancellationToken);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
