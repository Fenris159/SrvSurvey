using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Journeys;

public sealed partial class JourneyServiceTests
{
    [Fact]
    public void LateSameSecondContextIsOrderedBeforeMapping()
    {
        var original = new JourneyJournalProcessor(LegacyScanJourney(), new ExobiologyReferenceCatalog([]), true);
        string scan = ReviewScan.Replace("00:07:00", "00:08:00", StringComparison.Ordinal);
        Assert.False(original.Apply(TimelineEntry(scan, "same:2")));
        var resumed = new JourneyJournalProcessor(original.Journey, new ExobiologyReferenceCatalog([]), true);
        JourneyReplaySummary mapped = resumed.ApplyCatchUp([
            TimelineEntry(scan.Replace(":false", ":true", StringComparison.Ordinal), "same:1"),
            TimelineEntry(ReviewMapping, "same:3"),
        ]);
        Assert.Equal(1, mapped.Journey.CurrentSystem!.Counts.BodyScans);
        Assert.Equal(2_700_541, mapped.Journey.CurrentSystem.Counts.ExplorationRewards);
    }

    private const string OrderArrival =
        "{\"timestamp\":\"2026-07-01T00:06:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sirius\",\"SystemAddress\":44,\"StarPos\":[5,0,0]}";
    private const string OrderMusic = "{\"timestamp\":\"2026-07-01T00:08:00Z\",\"event\":\"Music\"}";
    private const string ReviewScan =
        "{\"timestamp\":\"2026-07-01T00:07:00Z\",\"event\":\"Scan\",\"SystemAddress\":42,\"BodyID\":4,\"PlanetClass\":\"High metal content body\",\"TerraformState\":\"Terraformable\",\"MassEM\":1,\"WasDiscovered\":false,\"WasMapped\":false}";
    private const string ReviewMapping =
        "{\"timestamp\":\"2026-07-01T00:10:00Z\",\"event\":\"SAAScanComplete\",\"SystemAddress\":42,\"BodyID\":4,\"ProbesUsed\":4,\"EfficiencyTarget\":6}";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyLiveScanContextPersistsWithoutRecountingStatistics(bool atBoundary)
    {
        JourneyDocument legacy = LegacyScanJourney();
        var store = new JourneyStore(DataDirectory);
        await store.SaveAsync(legacy);
        await new CommanderProfileStore(DataDirectory).SaveActiveJourneyAsync("F123", "Test", true, legacy.FileName);
        JourneyService service = BoundaryService([JournalDirectory]);
        await service.InitializeActiveAsync("F123", true);
        string json = atBoundary ? ReviewScan.Replace("00:07:00", "00:08:00", StringComparison.Ordinal) : ReviewScan;
        JourneyServiceResult primed = await service.ApplyLiveAsync([TimelineEntry(json, "legacy:7")]);
        Assert.Equal(0, primed.ProcessedEventCount);
        await service.ApplyLiveAsync([TimelineEntry(ReviewMapping, "legacy:10")]);
        JourneyDocument saved = (await store.LoadAsync("F123", legacy.FileName)).Journey!;
        Assert.Equal(1, saved.CurrentSystem!.Counts.BodyScans);
        Assert.Equal(1, saved.CurrentSystem.Counts.DetailedSurfaceScans);
        Assert.Equal(2_700_541, saved.CurrentSystem.Counts.ExplorationRewards);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordedScanRestoresContextAfterOlderBaselinePriming(bool replayNewerScan)
    {
        var processor = new JourneyJournalProcessor(LegacyScanJourney(), new ExobiologyReferenceCatalog([]), true);
        JournalEventEnvelope old = TimelineEntry(
            ReviewScan.Replace(":false", ":true", StringComparison.Ordinal),
            "context:7"
        );
        JournalEventEnvelope newer = TimelineEntry(
            ReviewScan.Replace("00:07:00", "00:09:00", StringComparison.Ordinal),
            "context:9"
        );
        processor.ApplyCatchUp([old]);
        Assert.True(processor.Apply(newer));
        var resumed = new JourneyJournalProcessor(processor.Journey, new ExobiologyReferenceCatalog([]), true);
        JournalEventEnvelope mapping = TimelineEntry(ReviewMapping, "context:10");
        JourneyReplaySummary mapped = resumed.ApplyCatchUp(replayNewerScan ? [old, newer, mapping] : [old, mapping]);
        Assert.Equal(1, mapped.Journey.CurrentSystem!.Counts.BodyScans);
        Assert.Equal(2_700_541, mapped.Journey.CurrentSystem.Counts.ExplorationRewards);
    }

    [Fact]
    public void LateActivityAtTheBaselineDepartureBelongsToTheClosedVisit()
    {
        JourneyDocument baseline = LegacyScanJourney();
        var processor = new JourneyJournalProcessor(
            baseline with
            {
                RecordTrackingStart = baseline.StartTime,
                RecordTrackingStartComplete = true,
                VisitedSystems = [baseline.VisitedSystems[0] with { Departed = baseline.Watermark }],
            },
            new ExobiologyReferenceCatalog([]),
            true
        );
        Assert.True(
            processor.Apply(
                TimelineEntry(BoundaryShot.Replace("00:06:00", "00:08:00", StringComparison.Ordinal), "boundary:0")
            )
        );
        Assert.Equal(1, Assert.Single(processor.Journey.VisitedSystems).Counts.Screenshots);
        Assert.Null(processor.Journey.CurrentSystem);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ConsistentPartialCopiesWithRepeatedSameSecondCapturesRemainUsable(
        bool partialFirst,
        bool savedPartial
    )
    {
        string copy = Path.Combine(temporaryDirectory, partialFirst ? "a-copy" : "z-copy");
        if (savedPartial)
        {
            await WriteJournalAsync(
                BoundaryJournal,
                new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                BasicJournal("2026-07-01T00:05:00Z", string.Join('\n', OrderArrival, BoundaryShot, OrderMusic)) + "\n"
            );
            JourneyService earlier = CreateService();
            JourneyJournalSystemSearchResult selected = await earlier.FindLatestStartAsync("F123", true, 42);
            await earlier.BeginAsync(new("F123", "Test", true, "Copies", "", selected.Entry!));
            Assert.Equal([0, 1], earlier.ActiveJourney!.VisitedSystems.Select(visit => visit.Counts.Screenshots));
        }
        Directory.CreateDirectory(copy);
        await WriteJournalAsync(
            BoundaryJournal,
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            BasicJournal(
                "2026-07-01T00:05:00Z",
                string.Join('\n', BoundaryShot, OrderArrival, BoundaryShot, OrderMusic)
            ) + "\n"
        );
        await File.WriteAllTextAsync(
            Path.Combine(copy, BoundaryJournal),
            BasicJournal("2026-07-01T00:05:00Z", string.Join('\n', OrderArrival, BoundaryShot, OrderMusic)) + "\n"
        );
        JourneyService service = BoundaryService(partialFirst ? [copy, JournalDirectory] : [JournalDirectory, copy]);
        JourneyJournalSystemSearchResult start = await service.FindLatestStartAsync("F123", true, 42);
        JourneyServiceResult begun = savedPartial
            ? await BoundaryService([JournalDirectory, copy, Path.Combine(temporaryDirectory, "missing")])
                .InitializeActiveAsync("F123", true)
            : await service.BeginAsync(new("F123", "Test", true, "Copies", "", start.Entry!));
        if (!savedPartial)
        {
            Assert.Empty(begun.Errors);
        }
        Assert.Equal(["Sol", "Sirius"], begun.Journey!.VisitedSystems.Select(visit => visit.StarSystem.Name));
        Assert.Equal([1, 1], begun.Journey.VisitedSystems.Select(visit => visit.Counts.Screenshots));
    }

    [Fact]
    public async Task CompleteReplayRetainsOrderingLearnedBeforeTheSourcesBecamePartial()
    {
        await WriteJournalAsync(
            BoundaryJournal,
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            BasicJournal("2026-07-01T00:05:00Z", string.Join('\n', OrderArrival, BoundaryShot, OrderMusic)) + "\n"
        );
        JourneyService original = CreateService();
        JourneyJournalSystemSearchResult start = await original.FindLatestStartAsync("F123", true, 42);
        await original.BeginAsync(new("F123", "Test", true, "Order", "", start.Entry!));
        string copy = Path.Combine(temporaryDirectory, "z-copy");
        Directory.CreateDirectory(copy);
        await File.WriteAllTextAsync(
            Path.Combine(JournalDirectory, BoundaryJournal),
            BasicJournal("2026-07-01T00:05:00Z", BoundaryShot + "\n" + OrderMusic) + "\n"
        );
        await File.WriteAllTextAsync(
            Path.Combine(copy, BoundaryJournal),
            BasicJournal("2026-07-01T00:05:00Z", OrderArrival + "\n" + OrderMusic) + "\n"
        );
        JourneyServiceResult rebuilt = await BoundaryService([JournalDirectory, copy])
            .InitializeActiveAsync("F123", true);
        Assert.Equal([0, 1], rebuilt.Journey!.VisitedSystems.Select(visit => visit.Counts.Screenshots));
        Assert.Equal("Sirius", rebuilt.Journey.CurrentSystem!.StarSystem.Name);
    }

    [Fact]
    public async Task ShiftedMonitorCopyDoesNotLinkItsRootToAnotherSourcesTail()
    {
        await WriteJournalAsync(
            BoundaryJournal,
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            BasicJournal("2026-07-01T00:05:00Z") + "\n"
        );
        JourneyService service = CreateService();
        JourneyJournalSystemSearchResult start = await service.FindLatestStartAsync("F123", true, 42);
        await service.BeginAsync(new("F123", "Test", true, "Roots", "", start.Entry!));
        string file = "Journal.2026-07-01T000800.01.log";
        string content =
            BasicJournal("2026-07-01T00:08:00Z")
                .Replace("00:00:00", "00:08:00", StringComparison.Ordinal)
                .Replace("00:00:01", "00:08:00", StringComparison.Ordinal) + "\n";
        await WriteJournalAsync(file, new DateTime(2026, 7, 1, 0, 8, 0, DateTimeKind.Utc), content);
        await service.ApplyLiveAsync(
            (await new JournalDirectoryMonitor(JournalDirectory, "F123").PollAsync()).JournalEvents
        );
        await service.ApplyLiveAsync(
            (await new JournalDirectoryMonitor(JournalDirectory, "F123").PollAsync()).JournalEvents
        );
        string copy = Path.Combine(temporaryDirectory, "shifted");
        Directory.CreateDirectory(copy);
        await File.WriteAllTextAsync(Path.Combine(copy, file), new string('\n', 1000) + content);
        await service.ApplyLiveAsync((await new JournalDirectoryMonitor(copy, "F123").PollAsync()).JournalEvents);
        await service.ApplyLiveAsync([
            TimelineEntry(BoundaryShot.Replace("00:06:00", "00:09:00", StringComparison.Ordinal), "root:9"),
        ]);
        Assert.Equal(1, service.ActiveJourney!.CurrentSystem!.Counts.Screenshots);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingObservationReachesAFileConcludedByAnotherWriter(bool edit)
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
        JourneyDocument begun = (
            await pending.BeginAsync(new("F123", "Test", true, "Pending", "", start.Entry!))
        ).Journey!;
        reject = true;
        await Assert.ThrowsAsync<IOException>(() =>
            pending.ApplyLiveAsync([
                TimelineEntry(BoundaryShot.Replace("00:06:00", "00:07:00", StringComparison.Ordinal), "pending:7"),
            ])
        );
        JourneyService other = CreateService();
        await other.InitializeActiveAsync("F123", true);
        JourneyDocument completed = (
            await other.ConcludeActiveAsync("Test", new DateTimeOffset(2026, 7, 1, 0, 9, 0, TimeSpan.Zero))
        )!;
        reject = false;
        if (edit)
        {
            await pending.InitializeActiveAsync("F456", true);
            await pending.SaveMetadataAsync(completed, "Completed edit", "");
        }
        else
        {
            Assert.Null((await pending.InitializeActiveAsync("F123", true)).Journey);
        }
        JourneyDocument result = (await store.LoadAsync("F123", begun.FileName)).Journey!;
        Assert.False(result.IsActive);
        Assert.Equal(completed.EndTime, result.EndTime);
        Assert.Equal(1, Assert.Single(result.VisitedSystems).Counts.Screenshots);
        Assert.Null(
            (await new CommanderProfileStore(DataDirectory).LoadAsync("F123", true)).Data!.ActiveJourneyFileName
        );
    }

    private JourneyDocument LegacyScanJourney()
    {
        var start = new DateTimeOffset(2026, 7, 1, 0, 5, 0, TimeSpan.Zero);
        var visit = new JourneySystemVisit(
            new("Sol", 42, new GalacticCoordinate(0, 0, 0)),
            start,
            null,
            JourneyCounts.Empty with
            {
                BodyScans = 1,
                ExplorationRewards = 449_200,
            },
            null,
            null,
            new HashSet<int> { 4 },
            null,
            null,
            null,
            null
        );
        return new(
            "legacy",
            Path.Combine(DataDirectory, "journey", "F123", "legacy.json"),
            "F123",
            "Test",
            "Legacy",
            "",
            BoundaryJournal,
            start,
            null,
            start.AddMinutes(3),
            [visit]
        );
    }
}
