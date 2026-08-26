using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SrvSurvey.Core.Network;
using System.IO.Compression;
using System.Net;
using Xunit;

namespace SrvSurvey.Core.Tests.Network;

public sealed class EddnOutboxTests
{
    [Fact]
    public async Task QueueIsPersistedBeforeSendingAndSurvivesRestart()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.path, "eddn-outbox-v1.json");
        var now = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        using (var first = outbox(
            path,
            EddnTransportTests.createTransport(_ => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK))),
            () => now))
        {
            first.setEnabled(true, discardPendingWhenDisabled: false);
            Assert.True(first.enqueue(queued(now)));
            Assert.True(Directory.Exists(storeFolder(path)));
            Assert.Equal(1, first.pendingCount);
            var persisted = await File.ReadAllTextAsync(
                Assert.Single(Directory.GetFiles(storeFolder(path), "*.json")));
            Assert.Contains("\"useTestSchemas\"", persisted);
            Assert.DoesNotContain("\"environment\"", persisted);
        }

        var calls = 0;
        using var restarted = outbox(
            path,
            EddnTransportTests.createTransport(_ =>
            {
                calls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }),
            () => now);
        Assert.Equal(1, restarted.pendingCount);
        restarted.setEnabled(true, discardPendingWhenDisabled: false);

        await restarted.processDue();

        Assert.Equal(1, calls);
        Assert.Equal(0, restarted.pendingCount);
        Assert.False(Directory.Exists(storeFolder(path)));
    }

    [Fact]
    public async Task MessageLimitLoadsRemainingValidFilesInLaterBatches()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.path, "eddn-outbox-v1.json");
        var store = storeFolder(path);
        Directory.CreateDirectory(store);
        var now = DateTimeOffset.UtcNow;
        writeQueued(store, queued(now.AddSeconds(-2), "First Port"));
        writeQueued(store, queued(now.AddSeconds(-1), "Second Port"));
        var logs = new List<string>();
        var calls = 0;

        using var queue = new EddnOutbox(
            path,
            EddnTransportTests.createTransport(_ =>
            {
                calls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }),
            logs.Add,
            () => now,
            automaticProcessing: false,
            maximumPendingMessages: 1);

        Assert.Equal(1, queue.pendingCount);
        Assert.Equal(2, Directory.GetFiles(store, "*.json").Length);
        Assert.Empty(Directory.GetFiles(store, "*.bad-*"));
        queue.setEnabled(true, discardPendingWhenDisabled: false);
        await queue.processDue();

        Assert.Equal(2, calls);
        Assert.Equal(0, queue.pendingCount);
        Assert.False(Directory.Exists(store));
        Assert.Contains(logs, line => line.Contains(
            "stopped loading pending uploads",
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task StorageLimitLeavesOversizedValidFileUnchanged()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.path, "eddn-outbox-v1.json");
        var store = storeFolder(path);
        Directory.CreateDirectory(store);
        writeQueued(store, queued(DateTimeOffset.UtcNow));
        var logs = new List<string>();
        var calls = 0;

        using var queue = new EddnOutbox(
            path,
            EddnTransportTests.createTransport(_ =>
            {
                calls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }),
            logs.Add,
            automaticProcessing: false,
            maximumStoreBytes: 1);

        Assert.Equal(0, queue.pendingCount);
        queue.setEnabled(true, discardPendingWhenDisabled: false);
        await queue.processDue();

        Assert.Equal(0, calls);
        Assert.Single(Directory.GetFiles(store, "*.json"));
        Assert.Empty(Directory.GetFiles(store, "*.bad-*"));
        Assert.Contains(logs, line => line.Contains(
            "stopped loading pending uploads",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("live")]
    [InlineData("beta")]
    [InlineData("dev")]
    public async Task LegacyQueueUsesTestSchemasOnLiveGateway(
        string legacyEnvironment)
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.path, "eddn-outbox-v1.json");
        File.WriteAllText(
            path,
            $$"""
            [{
              "id":"{{Guid.NewGuid()}}",
              "created":"2026-07-28T12:00:00Z",
              "nextAttempt":"2026-07-28T12:00:00Z",
              "attempts":0,
              "environment":"{{legacyEnvironment}}",
              "schemaRef":"https://eddn.edcd.io/schemas/dockinggranted/1",
              "header":{"uploaderID":"Test Cmdr"},
              "message":{"timestamp":"2026-07-28T12:00:00Z","event":"DockingGranted"}
            }]
            """);
        Uri? requestUri = null;
        string? schemaRef = null;
        var transport = EddnTransportTests.createTransport(async request =>
        {
            requestUri = request.RequestUri;
            var compressed = await request.Content!.ReadAsByteArrayAsync();
            using var input = new MemoryStream(compressed);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            schemaRef = JObject.Parse(await reader.ReadToEndAsync())
                .Value<string>("$schemaRef");
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var queue = outbox(
            path,
            transport,
            () => DateTimeOffset.Parse("2026-07-28T12:00:00Z"));
        queue.setEnabled(true, discardPendingWhenDisabled: false);

        await queue.processDue();

        Assert.Equal("https://live.example.test/upload/", requestUri?.ToString());
        Assert.Equal(
            "https://eddn.edcd.io/schemas/dockinggranted/1/test",
            schemaRef);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task TransientFailuresBackOffWithoutBlockingOtherMessages()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.path, "eddn-outbox-v1.json");
        var now = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        var calls = 0;
        var logs = new List<string>();
        using var queue = new EddnOutbox(
            path,
            EddnTransportTests.createTransport(_ =>
            {
                calls++;
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }),
            logs.Add,
            () => now,
            automaticProcessing: false);
        queue.setEnabled(true, discardPendingWhenDisabled: false);
        Assert.True(queue.enqueue(queued(now, "first")));
        Assert.True(queue.enqueue(queued(now, "second")));

        await queue.processDue();

        Assert.Equal(2, calls);
        Assert.Equal(2, queue.pendingCount);
        var saved = loadSaved(path);
        Assert.True(saved[0].nextAttempt >= now.AddMinutes(1));
        Assert.True(saved[1].nextAttempt >= now.AddMinutes(1));
        Assert.Equal(1, saved[0].attempts);
        Assert.Equal(1, saved[1].attempts);
        Assert.Contains(logs, line => line.Contains("will retry", StringComparison.Ordinal));

        await queue.processDue();
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task NewlyQueuedMessageCanProceedWhileEarlierMessageBacksOff()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.path, "eddn-outbox-v1.json");
        var now = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        var calls = 0;
        using var queue = outbox(
            path,
            EddnTransportTests.createTransport(_ =>
            {
                calls++;
                return Task.FromResult(new HttpResponseMessage(
                    calls == 1
                        ? HttpStatusCode.ServiceUnavailable
                        : HttpStatusCode.OK));
            }),
            () => now);
        queue.setEnabled(true, discardPendingWhenDisabled: false);
        Assert.True(queue.enqueue(queued(now, "First Port")));

        await queue.processDue();
        Assert.Equal(1, calls);
        Assert.Equal(1, queue.pendingCount);
        Assert.True(queue.enqueue(queued(now, "Second Port")));

        await queue.processDue();
        Assert.Equal(2, calls);

        now = now.AddMinutes(1);
        await queue.processDue();

        Assert.Equal(3, calls);
        Assert.Equal(0, queue.pendingCount);
    }

    [Fact]
    public async Task SuccessfulUploadsAreSummarizedOncePerFifteenMinuteWindow()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.path, "eddn-outbox-v1.json");
        var now = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        var logs = new List<string>();
        using var queue = new EddnOutbox(
            path,
            EddnTransportTests.createTransport(_ => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK))),
            logs.Add,
            () => now,
            automaticProcessing: false);
        queue.setEnabled(true, discardPendingWhenDisabled: false);
        Assert.True(queue.enqueue(queued(now, "First Port")));
        Assert.True(queue.enqueue(queued(now, "Second Port")));

        await queue.processDue();

        Assert.Empty(logs);

        now = now.AddMinutes(15);
        Assert.True(queue.enqueue(queued(now, "Third Port")));
        await queue.processDue();

        Assert.Equal(
            ["EDDN uploaded 2 journal messages in the previous 15-minute activity window using test schemas."],
            logs);
    }

    [Fact]
    public async Task SuspensionPreservesPendingMessagesUntilResumed()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.path, "eddn-outbox-v1.json");
        var now = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        var calls = 0;
        using var queue = outbox(
            path,
            EddnTransportTests.createTransport(_ =>
            {
                calls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }),
            () => now);
        queue.setEnabled(true, discardPendingWhenDisabled: false);
        Assert.True(queue.enqueue(queued(now)));

        queue.setSuspended(true);
        await queue.processDue();

        Assert.Equal(0, calls);
        Assert.Equal(1, queue.pendingCount);
        Assert.True(Directory.Exists(storeFolder(path)));
        Assert.False(queue.enqueue(queued(now, "Blocked Port")));

        queue.setSuspended(false);
        await queue.processDue();

        Assert.Equal(1, calls);
        Assert.Equal(0, queue.pendingCount);
        Assert.False(Directory.Exists(storeFolder(path)));
    }

    [Fact]
    public async Task SuspensionCancelsActiveUploadWithoutMutatingRetryState()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.path, "eddn-outbox-v1.json");
        var now = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        var handler = new CancelThenSucceedHandler();
        using var client = new HttpClient(handler);
        var transport = new EddnTransport(
            client,
            new Uri("https://live.example.test/upload/"));
        using var queue = outbox(path, transport, () => now);
        queue.setEnabled(true, discardPendingWhenDisabled: false);
        Assert.True(queue.enqueue(queued(now)));
        var processing = queue.processDue();
        await handler.Entered.WaitAsync(TimeSpan.FromSeconds(2));

        queue.setSuspended(true);
        await processing.WaitAsync(TimeSpan.FromSeconds(2));

        var saved = Assert.Single(loadSaved(path));
        Assert.Equal(0, saved.attempts);
        Assert.Equal(now, saved.nextAttempt);

        queue.setSuspended(false);
        await queue.processDue();

        Assert.Equal(2, handler.Calls);
        Assert.Equal(0, queue.pendingCount);
    }

    [Fact]
    public void OnlyOneProcessCanOwnAndRewriteAnOutbox()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.path, "eddn-outbox-v1.json");
        var now = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        var transport = EddnTransportTests.createTransport(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var first = outbox(path, transport, () => now);
        var second = outbox(path, transport, () => now);
        try
        {
            first.setEnabled(true, discardPendingWhenDisabled: false);
            second.setEnabled(true, discardPendingWhenDisabled: false);
            Assert.True(first.hasExclusiveOwnership);
            Assert.False(second.hasExclusiveOwnership);
            Assert.True(first.enqueue(queued(now, "First Port")));
            Assert.False(second.enqueue(queued(now, "Second Port")));
            Assert.Single(loadSaved(path));

            first.Dispose();
            second.setEnabled(true, discardPendingWhenDisabled: false);

            Assert.True(second.hasExclusiveOwnership);
            Assert.Equal(1, second.pendingCount);
            Assert.True(second.enqueue(queued(now, "Second Port")));
            Assert.Equal(2, loadSaved(path).Count);
        }
        finally
        {
            second.Dispose();
            first.Dispose();
        }
    }

    [Fact]
    public async Task OptOutFromNonOwnerCancelsOwnerAndDiscardsSharedQueue()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.path, "eddn-outbox-v1.json");
        var now = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        var handler = new CancelThenSucceedHandler();
        using var client = new HttpClient(handler);
        var transport = new EddnTransport(
            client,
            new Uri("https://live.example.test/upload/"));
        using var owner = outbox(path, transport, () => now);
        using var otherInstance = outbox(path, transport, () => now);
        owner.setEnabled(true, discardPendingWhenDisabled: false);
        otherInstance.setEnabled(true, discardPendingWhenDisabled: false);
        Assert.True(owner.enqueue(queued(now)));
        var processing = owner.processDue();
        await handler.Entered.WaitAsync(TimeSpan.FromSeconds(2));

        otherInstance.setEnabled(false, discardPendingWhenDisabled: true);

        await processing.WaitAsync(TimeSpan.FromSeconds(2));
        await waitUntil(
            () => owner.pendingCount == 0
                && !Directory.Exists(storeFolder(path)),
            TimeSpan.FromSeconds(2));
        owner.setEnabled(true, discardPendingWhenDisabled: false);

        Assert.False(owner.enqueue(queued(now, "Must Not Upload")));
        Assert.Equal(0, owner.pendingCount);

        otherInstance.setEnabled(true, discardPendingWhenDisabled: false);
        await waitUntil(
            () => owner.hasExclusiveOwnership
                || otherInstance.hasExclusiveOwnership,
            TimeSpan.FromSeconds(2));
        Assert.True(
            owner.enqueue(queued(now, "Sharing Restored"))
                || otherInstance.enqueue(queued(now, "Sharing Restored")));
    }

    [Fact]
    public void EnabledRestartClearsAnAbandonedSharedOptOutMarker()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.path, "eddn-outbox-v1.json");
        var now = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        var transport = EddnTransportTests.createTransport(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using (var previousProcess = outbox(path, transport, () => now))
        {
            previousProcess.setEnabled(
                false,
                discardPendingWhenDisabled: true);
        }

        using var restarted = outbox(path, transport, () => now);
        restarted.setEnabled(true, discardPendingWhenDisabled: false);

        Assert.True(restarted.enqueue(queued(now)));
        Assert.Equal(1, restarted.pendingCount);
    }

    [Fact]
    public void EnabledInstanceCannotOverrideAnActiveSharedOptOutLease()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.path, "eddn-outbox-v1.json");
        var now = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        var transport = EddnTransportTests.createTransport(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var disabledInstance = outbox(path, transport, () => now);
        using var enabledInstance = outbox(path, transport, () => now);
        disabledInstance.setEnabled(
            false,
            discardPendingWhenDisabled: true);

        enabledInstance.setEnabled(true, discardPendingWhenDisabled: false);

        Assert.False(enabledInstance.enqueue(queued(now)));
        Assert.Equal(0, enabledInstance.pendingCount);
    }

    [Fact]
    public void ExistingEnabledInstancePreservesACompletedSharedOptOut()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.path, "eddn-outbox-v1.json");
        var now = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        var transport = EddnTransportTests.createTransport(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var enabledInstance = outbox(path, transport, () => now);
        enabledInstance.setEnabled(true, discardPendingWhenDisabled: false);
        using (var disabledInstance = outbox(path, transport, () => now))
        {
            disabledInstance.setEnabled(
                false,
                discardPendingWhenDisabled: true);
        }

        enabledInstance.setEnabled(true, discardPendingWhenDisabled: false);

        Assert.False(enabledInstance.enqueue(queued(now)));
        Assert.Equal(0, enabledInstance.pendingCount);
    }

    [Fact]
    public void CorruptMessageFileIsQuarantinedWithoutDiscardingValidMessages()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.path, "eddn-outbox-v1.json");
        var messageFolder = storeFolder(path);
        Directory.CreateDirectory(messageFolder);
        File.WriteAllText(
            Path.Combine(messageFolder, "valid.json"),
            JsonConvert.SerializeObject(queued(
                DateTimeOffset.Parse("2026-07-28T12:00:00Z"))));
        File.WriteAllText(
            Path.Combine(messageFolder, "corrupt.json"),
            "{not-json");
        var logs = new List<string>();

        using var queue = new EddnOutbox(
            path,
            EddnTransportTests.createTransport(_ => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK))),
            logs.Add,
            automaticProcessing: false);

        Assert.Equal(1, queue.pendingCount);
        Assert.Single(Directory.GetFiles(messageFolder, "*.bad-*"));
        Assert.Contains(
            logs,
            line => line.Contains(
                "could not load a pending upload",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NullSchemaInPersistedQueueIsQuarantinedWithoutCrashing()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.path, "eddn-outbox-v1.json");
        File.WriteAllText(
            path,
            $$"""
            [{
              "id":"{{Guid.NewGuid()}}",
              "created":"2026-07-28T12:00:00Z",
              "nextAttempt":"2026-07-28T12:00:00Z",
              "attempts":0,
              "environment":"live",
              "schemaRef":null,
              "header":{"uploaderID":"Test Cmdr"},
              "message":{"timestamp":"2026-07-28T12:00:00Z","event":"DockingGranted"}
            }]
            """);
        var logs = new List<string>();

        using var queue = new EddnOutbox(
            path,
            EddnTransportTests.createTransport(_ => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK))),
            logs.Add,
            automaticProcessing: false);

        Assert.Equal(0, queue.pendingCount);
        Assert.False(Directory.Exists(storeFolder(path)));
        Assert.Single(Directory.GetFiles(folder.path, "*.bad-*"));
        Assert.Contains(
            logs,
            line => line.Contains("invalid", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(HttpStatusCode.UpgradeRequired)]
    public async Task PermanentGatewayRejectionIsDropped(HttpStatusCode statusCode)
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.path, "eddn-outbox-v1.json");
        var now = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        using var queue = outbox(
            path,
            EddnTransportTests.createTransport(_ => Task.FromResult(
                new HttpResponseMessage(statusCode))),
            () => now);
        queue.setEnabled(true, discardPendingWhenDisabled: false);
        Assert.True(queue.enqueue(queued(now)));

        await queue.processDue();

        Assert.Equal(0, queue.pendingCount);
        Assert.False(Directory.Exists(storeFolder(path)));
    }

    [Fact]
    public void DisablingSharingDeletesPendingUploads()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.path, "eddn-outbox-v1.json");
        var now = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        using var queue = outbox(
            path,
            EddnTransportTests.createTransport(_ => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK))),
            () => now);
        queue.setEnabled(true, discardPendingWhenDisabled: false);
        Assert.True(queue.enqueue(queued(now)));

        queue.setEnabled(false, discardPendingWhenDisabled: true);

        Assert.Equal(0, queue.pendingCount);
        Assert.False(Directory.Exists(storeFolder(path)));
    }

    [Fact]
    public async Task UploadLoggingNeverRunsWhileTheQueueLockIsHeld()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.path, "eddn-outbox-v1.json");
        var now = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        var callbackCouldInspectQueue = false;
        EddnOutbox? queue = null;
        queue = new EddnOutbox(
            path,
            EddnTransportTests.createTransport(_ => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK))),
            _ =>
            {
                callbackCouldInspectQueue = canInspectQueueFromAnotherThread(
                    queue!);
            },
            () => now,
            automaticProcessing: false);
        using (queue)
        {
            queue.setEnabled(true, discardPendingWhenDisabled: false);
            Assert.True(queue.enqueue(queued(now)));

            await queue.processDue();
            now = now.AddMinutes(15);
            Assert.True(queue.enqueue(queued(now)));
            await queue.processDue();

            Assert.True(callbackCouldInspectQueue);
        }
    }

    [Fact]
    public void DisableLoggingNeverRunsWhileTheQueueLockIsHeld()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.path, "eddn-outbox-v1.json");
        var now = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        var callbackCouldInspectQueue = false;
        EddnOutbox? queue = null;
        queue = new EddnOutbox(
            path,
            EddnTransportTests.createTransport(_ => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK))),
            _ =>
            {
                callbackCouldInspectQueue = canInspectQueueFromAnotherThread(
                    queue!);
            },
            () => now,
            automaticProcessing: false);
        using (queue)
        {
            queue.setEnabled(true, discardPendingWhenDisabled: false);
            Assert.True(queue.enqueue(queued(now)));

            queue.setEnabled(false, discardPendingWhenDisabled: true);

            Assert.True(callbackCouldInspectQueue);
        }
    }

    [Fact]
    public async Task DisposeDoesNotRaceAnActiveUpload()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.path, "eddn-outbox-v1.json");
        var now = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        var enteredTransport = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTransport = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = outbox(
            path,
            EddnTransportTests.createTransport(_ =>
            {
                enteredTransport.SetResult();
                return releaseTransport.Task;
            }),
            () => now);
        queue.setEnabled(true, discardPendingWhenDisabled: false);
        Assert.True(queue.enqueue(queued(now)));
        var processing = queue.processDue();
        await enteredTransport.Task;

        queue.Dispose();
        releaseTransport.SetResult(new HttpResponseMessage(HttpStatusCode.OK));

        await processing;
    }

    private static EddnOutbox outbox(
        string path,
        EddnTransport transport,
        Func<DateTimeOffset> clock)
    {
        return new EddnOutbox(
            path,
            transport,
            utcNow: clock,
            automaticProcessing: false);
    }

    private static EddnQueuedMessage queued(
        DateTimeOffset created,
        string stationName = "Test Port")
    {
        return new EddnQueuedMessage
        {
            id = Guid.NewGuid(),
            created = created,
            nextAttempt = created,
            schemaRef = "https://eddn.edcd.io/schemas/dockinggranted/1",
            header = EddnTransportTests.header(),
            message = new Newtonsoft.Json.Linq.JObject
            {
                ["timestamp"] = "2026-07-28T12:00:00Z",
                ["event"] = "DockingGranted",
                ["MarketID"] = 1,
                ["StationName"] = stationName,
            },
        };
    }

    private static void writeQueued(string store, EddnQueuedMessage message)
    {
        File.WriteAllText(
            Path.Combine(store, message.id.ToString("N") + ".json"),
            JsonConvert.SerializeObject(message));
    }

    private static List<EddnQueuedMessage> loadSaved(string path)
    {
        if (!Directory.Exists(storeFolder(path)))
        {
            return [];
        }

        return Directory.GetFiles(storeFolder(path), "*.json")
            .Select(File.ReadAllText)
            .Select(json => JsonConvert.DeserializeObject<EddnQueuedMessage>(json)!)
            .OrderBy(message => message.created)
            .ToList();
    }

    private static string storeFolder(string path)
    {
        return path + ".d";
    }

    private static async Task waitUntil(
        Func<bool> condition,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail("The expected cross-process state was not observed.");
            }

            await Task.Delay(20);
        }
    }

    private static bool canInspectQueueFromAnotherThread(EddnOutbox queue)
    {
        var inspected = false;
        var inspection = new Thread(() =>
        {
            _ = queue.pendingCount;
            inspected = true;
        })
        {
            IsBackground = true,
        };
        inspection.Start();
        return inspection.Join(TimeSpan.FromSeconds(2)) && inspected;
    }

    private sealed class TemporaryFolder : IDisposable
    {
        internal readonly string path = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-EddnTests-" + Guid.NewGuid().ToString("N"));

        internal TemporaryFolder()
        {
            Directory.CreateDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    private sealed class CancelThenSucceedHandler : HttpMessageHandler
    {
        private int calls;

        internal Task Entered => entered.Task;

        internal int Calls => calls;

        private readonly TaskCompletionSource entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.TrySetResult();
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}


