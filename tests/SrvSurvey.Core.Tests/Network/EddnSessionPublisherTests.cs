using Newtonsoft.Json.Linq;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Tests.Network;

public sealed class EddnSessionPublisherTests
{
    [Fact]
    public async Task DisablingSessionCancelsAnActiveCompanionRead()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new RecordingSink();
        using var session = CreateSession(
            sink,
            async (_, _, cancellationToken) =>
            {
                started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    cancelled.TrySetResult();
                    throw;
                }

                return new EddnCompanionReadResult(null, "unreachable");
            },
            journalDirectory: Path.GetTempPath());
        session.SetEnabled(true);
        session.SetEnabled(true);

        session.Apply(Request(Event(
            """{"timestamp":"2026-08-22T12:00:00Z","event":"Market","MarketID":42}""")),
            CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        session.SetEnabled(false);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await session.WaitForCompanionReadsAsync();

        Assert.Empty(sink.Messages);
    }

    [Fact]
    public async Task InvalidInputAndMissingCompanionDataFailWithinTheSession()
    {
        var logs = new List<string>();
        var sink = new RecordingSink();
        using var session = CreateSession(
            sink,
            (_, _, _) => Task.FromResult(
                new EddnCompanionReadResult(null, "file unavailable")),
            logs.Add);
        session.SetEnabled(true);
        session.SetSuspended(false);

        var invalid = session.Apply(
            Request(new JournalEventEnvelope(
                "Broken",
                null,
                "{not-json",
                default)),
            CancellationToken.None);
        var missingDirectory = session.Apply(
            Request(Event(
                """{"timestamp":"2026-08-22T12:00:00Z","event":"Market","MarketID":42}""")),
            CancellationToken.None);
        var mismatch = session.Apply(
            Request(Event(
                """{"timestamp":"2026-08-22T12:00:01Z","event":"LoadGame","Commander":"Other Cmdr"}""")),
            CancellationToken.None);

        Assert.Contains("Broken", Assert.Single(invalid.Warnings));
        Assert.Contains("directory", Assert.Single(missingDirectory.Warnings));
        Assert.Contains("Other Cmdr", Assert.Single(mismatch.Warnings));
        Assert.Contains(logs, line => line.Contains("Other Cmdr", StringComparison.Ordinal));
        Assert.Empty(sink.Messages);
        await session.WaitForCompanionReadsAsync();

        using var readFailure = CreateSession(
            sink,
            (_, _, _) => Task.FromResult(
                new EddnCompanionReadResult(null, "file unavailable")),
            logs.Add,
            Path.GetTempPath());
        readFailure.SetEnabled(true);
        readFailure.Apply(
            Request(Event(
                """{"timestamp":"2026-08-22T12:01:00Z","event":"Market","MarketID":42}""")),
            CancellationToken.None);
        await readFailure.WaitForCompanionReadsAsync();
        Assert.Contains(
            logs,
            line => line.Contains("file unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public void DisposeFlushesSignalsWithTheirCapturedSessionHeader()
    {
        var sink = new RecordingSink();
        var session = CreateSession(sink);
        session.SetEnabled(true);
        session.SetSuspended(true);
        session.SetSuspended(true);
        session.SetSuspended(false);
        session.Apply(Request(
            Event(
                """{"timestamp":"2026-08-22T12:00:00Z","event":"Location","StarSystem":"Origin","SystemAddress":123,"StarPos":[1,2,3]}"""),
            Event(
                """{"timestamp":"2026-08-22T12:00:01Z","event":"FSSSignalDiscovered","SystemAddress":123,"SignalName":"High Grade Emissions","SignalType":"USS","USSType":"$USS_Type_VeryValuableSalvage;","ThreatLevel":0}""")),
            CancellationToken.None);

        session.Dispose();
        session.Dispose();

        var queued = Assert.Single(
            sink.Messages,
            message => message.Prepared.eventName == "FSSSignalDiscovered");
        Assert.Equal("FSSSignalDiscovered", queued.Prepared.eventName);
        Assert.Equal("Test Cmdr", queued.Header.uploaderID);
        Assert.Equal(
            "Origin",
            queued.Prepared.message.Value<string>("StarSystem"));
    }

    private static EddnSessionPublisher CreateSession(
        RecordingSink sink,
        Func<string, JObject, CancellationToken,
            Task<EddnCompanionReadResult>>? companionReader = null,
        Action<string>? log = null,
        string? journalDirectory = null)
    {
        return new EddnSessionPublisher(
            sink,
            new UploadPayloadHeader("Test Cmdr", "4.1", "r1", "2.0.95"),
            journalDirectory,
            log,
            companionReader);
    }

    private static EddnApplyRequest Request(
        params JournalEventEnvelope[] events)
    {
        return new EddnApplyRequest
        {
            JournalEvents = events,
            Enabled = true,
            AllowPublishing = true,
            CommanderName = "Test Cmdr",
            FrontierId = "F123",
            GameVersion = "4.1",
            GameBuild = "r1",
        };
    }

    private static JournalEventEnvelope Event(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var result, out var error),
            error);
        return result!;
    }

    private sealed class RecordingSink : IEddnSessionSink
    {
        internal List<RecordedMessage> Messages { get; } = [];

        public bool TryBeginIngestion(out long generation)
        {
            generation = 1;
            return true;
        }

        public bool TryEnqueue(
            EddnPreparedMessage prepared,
            UploadPayloadHeader header,
            long expectedGeneration,
            string eventName,
            Action? rejected = null)
        {
            Messages.Add(new RecordedMessage(prepared, header.clone()));
            return true;
        }
    }

    private sealed record RecordedMessage(
        EddnPreparedMessage Prepared,
        UploadPayloadHeader Header);
}
