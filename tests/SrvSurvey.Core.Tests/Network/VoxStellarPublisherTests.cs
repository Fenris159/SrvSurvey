using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Tests.Network;

public sealed class VoxStellarPublisherTests
{
    [Fact]
    public async Task EventBurstProducesOneAcceptedSummary()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var logs = new ConcurrentQueue<string>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var publisher = new VoxStellarPublisher(
            "1.0.0",
            "test-key",
            client,
            log: message =>
            {
                logs.Enqueue(message);
                if (handler.Requests.Count == 3)
                {
                    completed.TrySetResult();
                }
            }
        );
        await publisher.ApplyAsync(
            new VoxStellarApplyRequest
            {
                JournalEvents =
                [
                    Parse("""{"event":"Scan","BodyID":1}"""),
                    Parse("""{"event":"Scan","BodyID":2}"""),
                    Parse("""{"event":"FSDJump","StarSystem":"Next"}"""),
                ],
                CommanderName = "Test Cmdr",
                Enabled = true,
                AllowPublishing = true,
            }
        );
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(3, handler.Requests.Count);
        Assert.Single(logs);
        Assert.Contains("3 accepted", logs.Single(), StringComparison.Ordinal);
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
        RecordedRequest request = await handler.Request.Task.WaitAsync(TimeSpan.FromSeconds(10));
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

        RecordedRequest request = await handler.Request.Task.WaitAsync(TimeSpan.FromSeconds(2));
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

        await Task.Delay(50);
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
        await handler.FirstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        publisher.SetEnabled(false);
        handler.ReleaseFirstRequest.TrySetResult();
        await Task.Delay(100);

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task DisablingConsentWaitsUntilAuthorizedTransportHasStarted()
    {
        var handler = new TransportStartBlockingHandler();
        using var client = new HttpClient(handler);
        using var publisher = new VoxStellarPublisher("1.0.0", "test-key", client, batchInterval: TimeSpan.Zero);
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
        await handler.TransportStartEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var disableStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task disableTask = Task.Factory.StartNew(
            () =>
            {
                disableStarted.TrySetResult();
                publisher.SetEnabled(false);
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default
        );
        try
        {
            await disableStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Task.Delay(50);
            Assert.False(disableTask.IsCompleted);
        }
        finally
        {
            handler.AllowTransportStart.Set();
        }

        await disableTask.WaitAsync(TimeSpan.FromSeconds(2));
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
        string firstSummary = await summaries.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains("100 accepted", firstSummary, StringComparison.Ordinal);
        Assert.Equal(100, handler.Requests.Count);
        ManualTimer secondTimer = await clock.NextTimerAsync();
        secondTimer.Fire();
        string secondSummary = await summaries.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
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
        string message = await summary.Task.WaitAsync(TimeSpan.FromSeconds(2));
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
        Assert.Contains("1 accepted", await summary.Task.WaitAsync(TimeSpan.FromSeconds(2)), StringComparison.Ordinal);
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
        _ = await handler.Request.Task.WaitAsync(TimeSpan.FromSeconds(2));
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

    [Fact]
    public async Task ShutdownCancelsAnUnresponsiveUploadWithinItsDeadline()
    {
        var handler = new CancellationHandler();
        using var client = new HttpClient(handler);
        using var publisher = new VoxStellarPublisher("1.0.0", "test-key", client, batchInterval: TimeSpan.Zero);
        await publisher.ApplyAsync(CreateRequest([Parse("""{"event":"Scan"}"""), Parse("""{"event":"FSDJump"}""")]));
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Run(publisher.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(handler.Cancelled.Task.IsCompleted);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ShutdownRetainsCompletedOutcomesFromAnInterruptedBatch()
    {
        var clock = new BatchClock();
        var handler = new CancellationHandler(acceptFirstRequest: true);
        using var client = new HttpClient(handler);
        var logs = new ConcurrentQueue<string>();
        using var publisher = new VoxStellarPublisher(
            "1.0.0",
            "test-key",
            client,
            log: logs.Enqueue,
            timeProvider: clock
        );
        await publisher.ApplyAsync(CreateRequest([Parse("""{"event":"Scan"}"""), Parse("""{"event":"FSDJump"}""")]));
        (await clock.NextTimerAsync()).Fire();
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Run(publisher.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(handler.Cancelled.Task.IsCompleted);
        Assert.Equal(2, handler.CallCount);
        Assert.Contains("1 accepted", Assert.Single(logs), StringComparison.Ordinal);
    }

    private sealed class CancellationHandler(bool acceptFirstRequest = false) : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount { get; private set; }

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
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                throw;
            }
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
            .WaitAsync(TimeSpan.FromSeconds(2));
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

        public Task<ManualTimer> NextTimerAsync() =>
            timers.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
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
