using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Journeys;

public sealed partial class JourneyServiceTests
{
    [Fact]
    public async Task ApparentlyCompleteHistoryCannotReplaceDifferentSavedRecordsWithEqualTotals()
    {
        await BeginBoundaryJourneyAsync();
        string path = Path.Combine(JournalDirectory, BoundaryJournal);
        string replacement = "{\"timestamp\":\"2026-07-01T00:06:30Z\",\"event\":\"Screenshot\"}";
        await File.WriteAllTextAsync(path, BasicJournal("2026-07-01T00:05:00Z", replacement) + "\n");
        JourneyServiceResult resumed = await CreateService().InitializeActiveAsync("F123", true);
        Assert.Equal(2, Assert.Single(resumed.Journey!.VisitedSystems).Counts.Screenshots);
    }

    [Theory]
    [InlineData("screenshots")]
    [InlineData("departure")]
    [InlineData("older-touchdown")]
    [InlineData("refresh")]
    [InlineData("later-visit")]
    [InlineData("missing-arrival")]
    [InlineData("conclude")]
    [InlineData("unknown-coverage")]
    [InlineData("older-body-count")]
    [InlineData("newer-body-count")]
    [InlineData("equal-body-count")]
    public async Task IndependentPendingRecordsMergeWithoutLosingActivityOrVisitLifecycle(string scenario)
    {
        await WriteJournalAsync(
            BoundaryJournal,
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            BasicJournal("2026-07-01T00:05:00Z") + "\n"
        );
        bool reject = false;
        var store = new JourneyStore(
            DataDirectory,
            (path, token) =>
                reject
                    ? Task.FromException<FileStream>(new IOException("Unavailable"))
                    : FileMutationLease.AcquireAsync(path, token)
        );
        JourneyService pending = BoundaryService([JournalDirectory], store);
        JourneyJournalSystemSearchResult start = await pending.FindLatestStartAsync("F123", true, 42);
        JourneyDocument saved = (
            await pending.BeginAsync(new("F123", "Test", true, "Records", "", start.Entry!))
        ).Journey!;
        reject = true;
        JournalEventEnvelope[] local = scenario switch
        {
            "older-body-count" =>
            [
                Parse("{\"timestamp\":\"2026-07-01T00:06:00Z\",\"event\":\"FSSDiscoveryScan\",\"BodyCount\":100}") with
                {
                    SourceRecordIdentity = "J1:1",
                },
            ],
            "equal-body-count" =>
            [
                Parse("{\"timestamp\":\"2026-07-01T00:07:00Z\",\"event\":\"FSSDiscoveryScan\",\"BodyCount\":100}") with
                {
                    SourceRecordIdentity = "J1:1",
                },
            ],
            "newer-body-count" =>
            [
                Parse("{\"timestamp\":\"2026-07-01T00:07:00Z\",\"event\":\"FSSDiscoveryScan\",\"BodyCount\":110}") with
                {
                    SourceRecordIdentity = "J1:1",
                },
            ],
            "missing-arrival" =>
            [
                Parse(
                    "{\"timestamp\":\"2026-07-01T00:06:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sirius\",\"SystemAddress\":44,\"StarPos\":[5,0,0]}"
                ) with
                {
                    SourceRecordIdentity = "J1:1",
                },
                Parse("{\"timestamp\":\"2026-07-01T00:06:01Z\",\"event\":\"Screenshot\"}") with
                {
                    SourceRecordIdentity = "J1:2",
                },
            ],
            _ => [Parse(BoundaryShot) with { SourceRecordIdentity = "J1:1" }],
        };
        await Assert.ThrowsAsync<IOException>(() => pending.ApplyLiveAsync(local));
        reject = false;
        JourneyService other = CreateService();
        await other.InitializeActiveAsync("F123", true);
        JournalEventEnvelope second = scenario switch
        {
            "older-body-count" or "equal-body-count" => Parse(
                "{\"timestamp\":\"2026-07-01T00:07:00Z\",\"event\":\"FSSAllBodiesFound\",\"Count\":110}"
            ),
            "newer-body-count" => Parse(
                "{\"timestamp\":\"2026-07-01T00:06:00Z\",\"event\":\"FSSAllBodiesFound\",\"Count\":100}"
            ),
            "later-visit" or "missing-arrival" => Parse(
                "{\"timestamp\":\"2026-07-01T00:07:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Lave\",\"SystemAddress\":43,\"StarPos\":[10,0,0]}"
            ),
            "screenshots" => Parse("{\"timestamp\":\"2026-07-01T00:06:30Z\",\"event\":\"Screenshot\"}"),
            "departure" => Parse(
                "{\"timestamp\":\"2026-07-01T00:07:00Z\",\"event\":\"StartJump\",\"JumpType\":\"Hyperspace\"}"
            ),
            _ => Parse("{\"timestamp\":\"2026-07-01T00:07:00Z\",\"event\":\"Touchdown\",\"Body\":\"Sol A 1\"}"),
        };
        second = second with { SourceRecordIdentity = "J2:1" };
        JournalEventEnvelope music = Parse("{\"timestamp\":\"2026-07-01T00:08:00Z\",\"event\":\"Music\"}") with
        {
            SourceRecordIdentity = "J2:2",
        };
        await other.ApplyLiveAsync([second, music]);
        if (scenario == "unknown-coverage")
        {
            JourneyDocument covered = (
                await new JourneyStore(DataDirectory).LoadAsync("F123", saved.FileName)
            ).Journey!;
            await new JourneyStore(DataDirectory).SaveAsync(
                covered with
                {
                    WatermarkRecords = null,
                    WatermarkRecordsComplete = null,
                    RecordTrackingStart = null,
                    RecordedJournalRecords = null,
                }
            );
            await Assert.ThrowsAsync<IOException>(() => pending.ApplyLiveAsync([music]));
            Assert.Equal(1, pending.ActiveJourney!.CurrentSystem!.Counts.Screenshots);
            JourneyDocument unchanged = (
                await new JourneyStore(DataDirectory).LoadAsync("F123", saved.FileName)
            ).Journey!;
            Assert.Equal(0, Assert.Single(unchanged.VisitedSystems).Counts.Screenshots);
            await new JourneyStore(DataDirectory).SaveAsync(covered);
            await pending.ApplyLiveAsync([]);
        }
        else if (scenario == "conclude")
        {
            JourneyDocument? concluded = await pending.ConcludeActiveAsync(
                "Test",
                new DateTimeOffset(2026, 7, 1, 0, 9, 0, TimeSpan.Zero)
            );
            Assert.False(concluded!.IsActive);
        }
        else if (scenario == "refresh")
        {
            await pending.InitializeActiveAsync("F123", true);
        }
        else
        {
            await pending.ApplyLiveAsync([music]);
        }
        await pending.ApplyLiveAsync([second, music]);
        JourneyDocument stored = (await new JourneyStore(DataDirectory).LoadAsync("F123", saved.FileName)).Journey!;
        if (scenario is "later-visit" or "missing-arrival")
        {
            Assert.Equal(
                scenario == "later-visit" ? ["Sol", "Lave"] : ["Sol", "Sirius", "Lave"],
                stored.VisitedSystems.Select(visit => visit.StarSystem.Name)
            );
            Assert.Equal(
                scenario == "later-visit" ? [1, 0] : [0, 1, 0],
                stored.VisitedSystems.Select(visit => visit.Counts.Screenshots)
            );
            Assert.Equal(
                new DateTimeOffset(2026, 7, 1, 0, scenario == "later-visit" ? 7 : 6, 0, TimeSpan.Zero),
                stored.VisitedSystems[0].Departed
            );
            Assert.Equal("Lave", stored.CurrentSystem!.StarSystem.Name);
            return;
        }
        JourneySystemVisit visit = Assert.Single(stored.VisitedSystems);
        int expectedScreenshots = scenario == "screenshots" ? 2 : 1;
        if (scenario is "older-body-count" or "newer-body-count" or "equal-body-count")
        {
            expectedScreenshots = 0;
        }
        Assert.Equal(expectedScreenshots, visit.Counts.Screenshots);
        if (scenario is "older-body-count" or "newer-body-count" or "equal-body-count")
        {
            Assert.Equal(110, visit.Counts.BodyCount);
        }
        Assert.Equal(
            scenario is "older-touchdown" or "refresh" or "conclude" or "unknown-coverage" ? 1 : 0,
            visit.Counts.Touchdowns
        );
        Assert.Equal(
            scenario == "departure" ? new DateTimeOffset(2026, 7, 1, 0, 7, 0, TimeSpan.Zero) : (DateTimeOffset?)null,
            visit.Departed
        );
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 8, 0, TimeSpan.Zero), stored.Watermark);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KnownLiveRecordsAtAnUnknownLegacyBoundaryAreNotRepeatedAfterTruncation(bool failCatchUpSave)
    {
        JourneyDocument saved = await BeginBoundaryJourneyAsync();
        await new JourneyStore(DataDirectory).SaveAsync(
            saved with
            {
                WatermarkRecords = null,
                WatermarkRecordsComplete = null,
                RecordTrackingStart = null,
                RecordedJournalRecords = null,
            }
        );
        string path = Path.Combine(JournalDirectory, BoundaryJournal);
        int leases = 0;
        bool failed = false;
        var store = new JourneyStore(
            DataDirectory,
            (file, token) =>
            {
                if (failCatchUpSave && ++leases == 2)
                {
                    failed = true;
                    return Task.FromException<FileStream>(new IOException("Injected catch-up write failure"));
                }
                return FileMutationLease.AcquireAsync(file, token);
            }
        );
        JourneyService service = BoundaryService(
            [JournalDirectory, Path.Combine(temporaryDirectory, "unavailable")],
            store
        );
        JourneyServiceResult initialized = await service.InitializeActiveAsync("F123", true);
        if (failCatchUpSave)
        {
            Assert.True(failed);
            Assert.Contains(
                initialized.Errors,
                error => error.Contains("Injected catch-up write failure", StringComparison.Ordinal)
            );
        }
        await service.SaveMetadataAsync(service.ActiveJourney!, "Renamed legacy Journey", "");
        var monitor = new JournalDirectoryMonitor(JournalDirectory, "F123");
        await monitor.PollAsync();
        await File.AppendAllTextAsync(path, BoundaryShot + "\n  \n");
        await service.ApplyLiveAsync((await monitor.PollAsync()).JournalEvents);
        Assert.Equal(2, service.ActiveJourney!.CurrentSystem!.Counts.Screenshots);
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            stream.SetLength(stream.Length - 3);
        }
        await service.ApplyLiveAsync((await monitor.PollAsync()).JournalEvents);
        Assert.Equal(2, service.ActiveJourney.CurrentSystem.Counts.Screenshots);
    }

    [Fact]
    public async Task IncompleteHistoryRecognizesThePreviousPhysicalReceiptFormat()
    {
        JourneyDocument saved = await BeginBoundaryJourneyAsync();
        var monitor = new JournalDirectoryMonitor(JournalDirectory, "F123");
        JournalEventEnvelope shot = (await monitor.PollAsync()).JournalEvents.Single(entry =>
            entry.EventName == "Screenshot"
        );
        // Previous on-disk contract: original byte identity followed by the raw JSON SHA-256.
        string physical =
            shot.SourceRecordIdentity
            + ":"
            + Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(BoundaryShot))
            );
        await new JourneyStore(DataDirectory).SaveAsync(
            saved with
            {
                WatermarkRecords = new HashSet<string> { physical },
                WatermarkRecordsComplete = null,
                RecordTrackingStart = null,
                RecordedJournalRecords = null,
            }
        );
        JourneyService service = BoundaryService([JournalDirectory, Path.Combine(temporaryDirectory, "unavailable")]);
        JourneyServiceResult resumed = await service.InitializeActiveAsync("F123", true);
        Assert.Equal(1, Assert.Single(resumed.Journey!.VisitedSystems).Counts.Screenshots);
    }
}
