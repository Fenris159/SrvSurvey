using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Journeys;

public sealed partial class JourneyServiceTests
{
    [Fact]
    public async Task ShiftedPartialJournalRetainsSameSecondVisitOrderAcrossRestart()
    {
        const string shot = "{\"timestamp\":\"2026-07-01T00:06:00Z\",\"event\":\"Screenshot\"}";
        const string arrival =
            "{\"timestamp\":\"2026-07-01T00:06:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sirius\",\"SystemAddress\":44,\"StarPos\":[5,0,0]}";
        const string music = "{\"timestamp\":\"2026-07-01T00:08:00Z\",\"event\":\"Music\"}";
        await WriteJournalAsync(
            BoundaryJournal,
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            BasicJournal("2026-07-01T00:05:00Z", shot + "\n" + music) + "\n"
        );
        JourneyService partial = CreateService();
        JourneyJournalSystemSearchResult start = await partial.FindLatestStartAsync("F123", true, 42);
        await partial.BeginAsync(new("F123", "Test", true, "Same-second arrival", "", start.Entry!));
        Assert.Equal(1, partial.ActiveJourney!.CurrentSystem!.Counts.Screenshots);
        await File.WriteAllTextAsync(
            Path.Combine(JournalDirectory, BoundaryJournal),
            BasicJournal("2026-07-01T00:05:00Z", arrival + "\n" + shot + "\n" + music) + "\n"
        );
        string[] sources = [JournalDirectory, Path.Combine(temporaryDirectory, "missing-source")];
        JourneyService corrected = BoundaryService(sources);
        JourneyServiceResult result = await corrected.InitializeActiveAsync("F123", true);
        Assert.Equal(["Sol", "Sirius"], result.Journey!.VisitedSystems.Select(visit => visit.StarSystem.Name));
        Assert.Equal([0, 1], result.Journey.VisitedSystems.Select(visit => visit.Counts.Screenshots));
        JourneyServiceResult restarted = await BoundaryService(sources).InitializeActiveAsync("F123", true);
        Assert.Equal([0, 1], restarted.Journey!.VisitedSystems.Select(visit => visit.Counts.Screenshots));
        Assert.Equal("Sirius", restarted.Journey.CurrentSystem!.StarSystem.Name);
    }

    [Theory]
    [InlineData("activity")]
    [InlineData("closed-gap")]
    [InlineData("return")]
    [InlineData("departure")]
    [InlineData("location")]
    public async Task LearningAnEarlierArrivalReplaysAlreadyObservedHistory(string scenario)
    {
        await WriteJournalAsync(
            BoundaryJournal,
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            BasicJournal("2026-07-01T00:05:00Z") + "\n"
        );
        JourneyService complete = CreateService();
        JourneyJournalSystemSearchResult start = await complete.FindLatestStartAsync("F123", true, 42);
        JourneyDocument saved = (
            await complete.BeginAsync(new("F123", "Test", true, "Timeline", "", start.Entry!))
        ).Journey!;
        JourneyService partial = CreateService();
        await partial.InitializeActiveAsync("F123", true);
        JournalEventEnvelope arrival = TimelineEntry(
            "{\"timestamp\":\"2026-07-01T00:06:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sirius\",\"SystemAddress\":44,\"StarPos\":[5,0,0]}",
            "shared:1"
        );
        JournalEventEnvelope screenshot = TimelineEntry(
            "{\"timestamp\":\"2026-07-01T00:06:01Z\",\"event\":\"Screenshot\"}",
            "shared:2"
        );
        JournalEventEnvelope departure = TimelineEntry(
            "{\"timestamp\":\"2026-07-01T00:06:30Z\",\"event\":\"StartJump\",\"JumpType\":\"Hyperspace\"}",
            "shared:3"
        );
        JournalEventEnvelope onward = TimelineEntry(
            scenario == "return"
                ? "{\"timestamp\":\"2026-07-01T00:07:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sol\",\"SystemAddress\":42,\"StarPos\":[0,0,0]}"
                : "{\"timestamp\":\"2026-07-01T00:07:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Lave\",\"SystemAddress\":43,\"StarPos\":[10,0,0]}",
            "shared:4"
        );
        JournalEventEnvelope location = TimelineEntry(
            "{\"timestamp\":\"2026-07-01T00:07:00Z\",\"event\":\"Location\",\"StarSystem\":\"Sirius\",\"SystemAddress\":44,\"StarPos\":[5,0,0]}",
            "shared:5"
        );
        JournalEventEnvelope music = TimelineEntry(
            "{\"timestamp\":\"2026-07-01T00:08:00Z\",\"event\":\"Music\"}",
            "shared:6"
        );
        JournalEventEnvelope[] suffix = scenario switch
        {
            "activity" => [screenshot, music],
            "closed-gap" =>
            [
                TimelineEntry(
                    "{\"timestamp\":\"2026-07-01T00:05:30Z\",\"event\":\"StartJump\",\"JumpType\":\"Hyperspace\"}",
                    "shared:0"
                ),
                screenshot,
                music,
            ],
            "return" => [onward, music],
            "departure" => [departure, onward, music],
            _ => [location, music],
        };
        await partial.ApplyLiveAsync(suffix);
        if (scenario == "location")
        {
            await partial.RecordNoteAsync(
                partial.ActiveJourney!,
                44,
                new DateTimeOffset(2026, 7, 1, 0, 7, 0, TimeSpan.Zero)
            );
        }
        await complete.ApplyLiveAsync([arrival, .. suffix]);
        JourneyDocument result = (await new JourneyStore(DataDirectory).LoadAsync("F123", saved.FileName)).Journey!;
        string[] names = scenario switch
        {
            "return" => ["Sol", "Sirius", "Sol"],
            "departure" => ["Sol", "Sirius", "Lave"],
            _ => ["Sol", "Sirius"],
        };
        Assert.Equal(names, result.VisitedSystems.Select(visit => visit.StarSystem.Name));
        Assert.Equal(
            scenario is "activity" or "closed-gap" ? [0, 1] : Enumerable.Repeat(0, names.Length),
            result.VisitedSystems.Select(visit => visit.Counts.Screenshots)
        );
        Assert.Equal(names[^1], result.CurrentSystem!.StarSystem.Name);
        if (scenario == "departure")
        {
            Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 6, 30, TimeSpan.Zero), result.VisitedSystems[1].Departed);
        }
        if (scenario == "location")
        {
            Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 6, 0, TimeSpan.Zero), result.VisitedSystems[1].Arrived);
            Assert.Equal([0, 1], result.VisitedSystems.Select(visit => visit.Counts.Notes));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MappingUsesScanContextAtTheTimeOfMappingAcrossAWholeBatch(bool failSave)
    {
        const string scan =
            "{\"timestamp\":\"2026-07-01T00:05:30Z\",\"event\":\"Scan\",\"SystemAddress\":42,\"BodyID\":4,\"PlanetClass\":\"High metal content body\",\"TerraformState\":\"Terraformable\",\"MassEM\":1,\"WasDiscovered\":false,\"WasMapped\":false}";
        await WriteJournalAsync(
            BoundaryJournal,
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            BasicJournal("2026-07-01T00:05:00Z", scan) + "\n"
        );
        bool reject = false;
        var store = new JourneyStore(
            DataDirectory,
            (path, token) =>
                reject
                    ? Task.FromException<FileStream>(new IOException("Unavailable"))
                    : FileMutationLease.AcquireAsync(path, token)
        );
        JourneyService service = BoundaryService([JournalDirectory], store);
        JourneyJournalSystemSearchResult start = await service.FindLatestStartAsync("F123", true, 42);
        await service.BeginAsync(new("F123", "Test", true, "Mapping", "", start.Entry!));
        string[] events =
        [
            "{\"timestamp\":\"2026-07-01T00:06:00Z\",\"event\":\"SAAScanComplete\",\"SystemAddress\":42,\"BodyID\":4,\"ProbesUsed\":4,\"EfficiencyTarget\":6}",
            "{\"timestamp\":\"2026-07-01T00:07:00Z\",\"event\":\"StartJump\",\"JumpType\":\"Hyperspace\"}",
            "{\"timestamp\":\"2026-07-01T00:08:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sirius\",\"SystemAddress\":44,\"StarPos\":[5,0,0]}",
            "{\"timestamp\":\"2026-07-01T00:09:00Z\",\"event\":\"StartJump\",\"JumpType\":\"Hyperspace\"}",
            "{\"timestamp\":\"2026-07-01T00:10:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sol\",\"SystemAddress\":42,\"StarPos\":[0,0,0]}",
            "{\"timestamp\":\"2026-07-01T00:11:00Z\",\"event\":\"Scan\",\"SystemAddress\":42,\"BodyID\":4,\"PlanetClass\":\"High metal content body\",\"TerraformState\":\"Terraformable\",\"MassEM\":1,\"WasDiscovered\":true,\"WasMapped\":true}",
        ];
        JournalEventEnvelope[] batch = events
            .Select((json, index) => TimelineEntry(json, "mapping:" + index))
            .ToArray();
        if (failSave)
        {
            reject = true;
            await Assert.ThrowsAsync<IOException>(() => service.ApplyLiveAsync(batch));
            reject = false;
            await service.ApplyLiveAsync([]);
        }
        else
        {
            await service.ApplyLiveAsync(batch);
        }
        // Established HMC, terraformable, one-Earth-mass reference values.
        Assert.Equal(
            [2_700_541L, 0L, 172_769L],
            service.ActiveJourney!.VisitedSystems.Select(visit => visit.Counts.ExplorationRewards)
        );
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("missing")]
    [InlineData("malformed")]
    public async Task PendingFailureDoesNotBlockAnotherProfileOrUnavailableSelectionRecovery(string scenario)
    {
        JourneyDocument original = await BeginBoundaryJourneyAsync();
        string originalBytes = await File.ReadAllTextAsync(original.FilePath);
        bool reject = false;
        var store = new JourneyStore(
            DataDirectory,
            (path, token) =>
                reject && Path.GetFileName(Path.GetDirectoryName(path)) == "F123"
                    ? Task.FromException<FileStream>(new IOException("Unavailable A"))
                    : FileMutationLease.AcquireAsync(path, token)
        );
        JourneyService service = BoundaryService([JournalDirectory], store);
        await service.InitializeActiveAsync("F123", true);
        reject = true;
        await Assert.ThrowsAsync<IOException>(() =>
            service.ApplyLiveAsync([
                TimelineEntry("{\"timestamp\":\"2026-07-01T00:07:00Z\",\"event\":\"Screenshot\"}", "A:1"),
            ])
        );
        if (scenario == "switch")
        {
            await WriteJournalAsync(
                "Journal.2026-07-02T000000.01.log",
                new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc),
                BasicJournal("2026-07-02T00:05:00Z")
                    .Replace("F123", "F456", StringComparison.Ordinal)
                    .Replace("Sol", "Lave", StringComparison.Ordinal)
                    .Replace("42", "43", StringComparison.Ordinal)
            );
            JourneyService other = CreateService();
            JourneyJournalSystemSearchResult start = await other.FindLatestStartAsync("F456", true, 43);
            JourneyDocument healthy = (
                await other.BeginAsync(new("F456", "Other", true, "Healthy", "", start.Entry!))
            ).Journey!;
            JourneyServiceResult switched = await service.InitializeActiveAsync("F456", true);
            Assert.Equal(healthy.FileName, switched.Journey!.FileName);
            Assert.Equal("F456", service.ActiveJourney!.FrontierId);
        }
        else
        {
            reject = false;
            if (scenario == "missing")
            {
                File.Delete(original.FilePath);
            }
            else
            {
                await File.WriteAllTextAsync(original.FilePath, "{");
            }
            JourneyServiceResult unavailable = await service.InitializeActiveAsync("F123", true);
            Assert.Null(unavailable.Journey);
            Assert.NotEmpty(unavailable.Errors);
            if (scenario == "missing")
            {
                Assert.Null(
                    (await new CommanderProfileStore(DataDirectory).LoadAsync("F123", true)).Data!.ActiveJourneyFileName
                );
            }
            else
            {
                Assert.Equal(original.FileName, unavailable.Recovery!.FileName);
            }
            await File.WriteAllTextAsync(original.FilePath, originalBytes);
            await new CommanderProfileStore(DataDirectory).SaveActiveJourneyAsync(
                "F123",
                "Test",
                true,
                original.FileName
            );
        }
        reject = false;
        JourneyServiceResult resumed = await service.InitializeActiveAsync("F123", true);
        Assert.Equal(2, resumed.Journey!.CurrentSystem!.Counts.Screenshots);
    }

    [Fact]
    public async Task FailedBeginCatchUpKeepsItsRecordingSelectionAndRecovers()
    {
        await WriteJournalAsync(
            BoundaryJournal,
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            BasicJournal("2026-07-01T00:05:00Z") + "\n"
        );
        bool failed = false;
        string folder = Path.Combine(DataDirectory, "journey", "F123");
        var store = new JourneyStore(
            DataDirectory,
            (path, token) =>
            {
                if (!failed && Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.json").Any())
                {
                    failed = true;
                    return Task.FromException<FileStream>(new IOException("Injected begin catch-up failure"));
                }
                return FileMutationLease.AcquireAsync(path, token);
            }
        );
        JourneyService service = BoundaryService([JournalDirectory], store);
        JourneyJournalSystemSearchResult start = await service.FindLatestStartAsync("F123", true, 42);
        JourneyServiceResult begun = await service.BeginAsync(
            new("F123", "Test", true, "Begin retry", "", start.Entry!)
        );
        Assert.Contains(
            begun.Errors,
            error => error.Contains("Injected begin catch-up failure", StringComparison.Ordinal)
        );
        string selected = (await new CommanderProfileStore(DataDirectory).LoadAsync("F123", true))
            .Data!
            .ActiveJourneyFileName!;
        Assert.Equal(begun.Journey!.FileName, selected);
        Assert.Equal(selected, service.ActiveJourney!.FileName);
        await service.ApplyLiveAsync([
            TimelineEntry("{\"timestamp\":\"2026-07-01T00:07:00Z\",\"event\":\"Screenshot\"}", "begin:1"),
        ]);
        JourneyServiceResult resumed = await service.InitializeActiveAsync("F123", true);
        Assert.Equal(selected, resumed.Journey!.FileName);
        Assert.Equal(1, resumed.Journey.CurrentSystem!.Counts.Screenshots);
    }

    [Fact]
    public void ReceiptlessCatchUpDoesNotRepeatAnUnknownCurrentBoundary()
    {
        var start = new DateTimeOffset(2026, 7, 1, 0, 5, 0, TimeSpan.Zero);
        var visit = new JourneySystemVisit(
            new("Sol", 42, new GalacticCoordinate(0, 0, 0)),
            start,
            null,
            JourneyCounts.Empty,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );
        var document = new JourneyDocument(
            "receiptless",
            "receiptless.json",
            "F123",
            "Test",
            "Receiptless",
            "",
            BoundaryJournal,
            start,
            null,
            start,
            [visit]
        );
        var processor = new JourneyJournalProcessor(document, new ExobiologyReferenceCatalog([]), true);
        JournalEventEnvelope screenshot = Parse(BoundaryShot);
        Assert.True(processor.Apply(screenshot));
        JourneyReplaySummary repeated = processor.ApplyCatchUp([screenshot]);
        Assert.Equal(0, repeated.ProcessedEventCount);
        Assert.Equal(1, repeated.IgnoredEventCount);
        Assert.Equal(1, repeated.Journey.CurrentSystem!.Counts.Screenshots);
    }

    private static JournalEventEnvelope TimelineEntry(string json, string identity) =>
        Parse(json) with
        {
            SourceRecordIdentity = identity,
        };
}
