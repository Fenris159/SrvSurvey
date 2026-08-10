using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Journeys;

namespace SrvSurvey.Core.Tests.Journeys;

public sealed class JourneyJournalProcessorTests
{
    private const string Species = "$Codex_Ent_Aleoids_01_Name;";

    private static readonly ExobiologyReferenceCatalog Catalog = new(
    [
        new ExobiologyReference(
            1,
            "$Codex_Ent_Aleoids_01_Green_Name;",
            Species,
            "Aleoida Arcus - Green",
            7_252_500),
    ]);

    [Fact]
    public void TracksLegacyJourneyEventsAndCorrectMappingReward()
    {
        var processor = new JourneyJournalProcessor(CreateJourney(), Catalog, true);
        var events = new[]
        {
            Parse("""{"timestamp":"2026-07-01T00:00:01Z","event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}"""),
            Parse("""{"timestamp":"2026-07-01T00:00:02Z","event":"FSSDiscoveryScan","BodyCount":2}"""),
            Parse("""{"timestamp":"2026-07-01T00:00:03Z","event":"Scan","SystemAddress":42,"BodyID":0,"StarType":"G","StellarMass":1,"WasDiscovered":false}"""),
            Parse("""{"timestamp":"2026-07-01T00:00:04Z","event":"Scan","SystemAddress":42,"BodyID":1,"PlanetClass":"Earthlike body","MassEM":1,"WasDiscovered":false,"WasMapped":false}"""),
            Parse("""{"timestamp":"2026-07-01T00:00:05Z","event":"Scan","SystemAddress":42,"BodyID":1,"PlanetClass":"Earthlike body","MassEM":1}"""),
            Parse("""{"timestamp":"2026-07-01T00:00:06Z","event":"SAAScanComplete","SystemAddress":42,"BodyID":1,"ProbesUsed":5,"EfficiencyTarget":6}"""),
            Parse("""{"timestamp":"2026-07-01T00:00:07Z","event":"Touchdown","StarSystem":"Sol","Body":"Sol A 1"}"""),
            Parse("""{"timestamp":"2026-07-01T00:00:08Z","event":"Touchdown","StarSystem":"Sol","Body":"Sol A 1"}"""),
            Parse("""{"timestamp":"2026-07-01T00:00:09Z","event":"FSSBodySignals","Signals":[{"Type":"$SAA_SignalType_Biological;","Count":3}]}"""),
            Parse("""{"timestamp":"2026-07-01T00:00:10Z","event":"FSSSignalDiscovered","SignalType":"USS"}"""),
            Parse("""{"timestamp":"2026-07-01T00:00:11Z","event":"FSSSignalDiscovered","SignalType":"USS"}"""),
            Parse("""{"timestamp":"2026-07-01T00:00:12Z","event":"CodexEntry","EntryID":7,"IsNewEntry":true,"Name_Localised":"New thing","SubCategory_Localised":"Biology"}"""),
            Parse("""{"timestamp":"2026-07-01T00:00:13Z","event":"CodexEntry","EntryID":7,"IsNewEntry":false,"SubCategory_Localised":"Biology"}"""),
            Parse($$"""{"timestamp":"2026-07-01T00:00:14Z","event":"ScanOrganic","ScanType":"Log","Species":"{{Species}}"}"""),
            Parse($$"""{"timestamp":"2026-07-01T00:00:15Z","event":"ScanOrganic","ScanType":"Analyse","Species":"{{Species}}"}"""),
            Parse("""{"timestamp":"2026-07-01T00:00:16Z","event":"Screenshot"}"""),
            Parse("""{"timestamp":"2026-07-01T00:00:17Z","event":"StartJump","JumpType":"Hyperspace"}"""),
            Parse("""{"timestamp":"2026-07-01T00:00:18Z","event":"FSDJump","StarSystem":"Achenar","SystemAddress":43,"StarPos":[3,4,0]}"""),
        };

        var replay = processor.ApplyCatchUp(events);

        Assert.Equal(events.Length, replay.ProcessedEventCount);
        Assert.Equal(DateTimeOffset.Parse("2026-07-01T00:00:18Z"), replay.Journey.Watermark);
        Assert.Equal(2, replay.Journey.VisitedSystems.Count);
        var sol = replay.Journey.VisitedSystems[0];
        Assert.Equal(DateTimeOffset.Parse("2026-07-01T00:00:17Z"), sol.Departed);
        Assert.Equal(2, sol.Counts.BodyScans);
        Assert.Equal(1, sol.Counts.Stars);
        Assert.Equal(1, sol.Counts.DetailedSurfaceScans);
        Assert.Equal(2, sol.Counts.Touchdowns);
        Assert.Equal(2, sol.Counts.BodyCount);
        Assert.Equal(1, sol.Counts.Screenshots);
        Assert.Equal(1, sol.Counts.NewCodexEntries);
        Assert.Equal(1, sol.Counts.Organisms);
        Assert.Equal(7_252_500, sol.Counts.ExobiologyRewards);
        Assert.Equal(2, sol.LandedOn!["A1"]);
        Assert.Equal(3, sol.SurfaceSignals!["Biological"]);
        Assert.Equal(2, sol.FssSignals!["USS"]);
        Assert.Single(sol.CodexScanned!);
        Assert.Single(sol.CodexNew!);
        Assert.Equal(1, sol.SubCategories!["Biology"]);

        var starReward = ExplorationValueCalculator.Calculate(
            new ExplorationValueRequest
            {
                BodyClass = "G",
                IsTerraformable = false,
                Mass = 1,
                IsFirstDiscoverer = true,
                IsMapped = false,
                IsFirstMapped = true,
                IsOdyssey = true,
                WithEfficiencyBonus = false
            });
        var mappedPlanetReward = ExplorationValueCalculator.Calculate(
            new ExplorationValueRequest
            {
                BodyClass = "Earthlike body",
                IsTerraformable = false,
                Mass = 1,
                IsFirstDiscoverer = true,
                IsMapped = true,
                IsFirstMapped = true,
                IsOdyssey = true,
                WithEfficiencyBonus = true
            });
        Assert.Equal(
            starReward + mappedPlanetReward,
            sol.Counts.ExplorationRewards);
        Assert.Equal("Achenar", replay.Journey.CurrentSystem!.StarSystem.Name);
    }

    [Fact]
    public void CatchUpPrimesOldScanForLaterMappingWithoutRecounting()
    {
        var scanReward = ExplorationValueCalculator.Calculate(
            new ExplorationValueRequest
            {
                BodyClass = "Water world",
                IsTerraformable = true,
                Mass = 1.2,
                IsFirstDiscoverer = true,
                IsMapped = false,
                IsFirstMapped = true,
                IsOdyssey = true,
                WithEfficiencyBonus = false
            });
        var visit = CreateVisit() with
        {
            BodiesScanned = new HashSet<int> { 4 },
            Counts = JourneyCounts.Empty with
            {
                BodyScans = 1,
                ExplorationRewards = scanReward,
            },
        };
        var journey = CreateJourney([visit]) with
        {
            Watermark = DateTimeOffset.Parse("2026-07-01T00:01:00Z"),
        };
        var processor = new JourneyJournalProcessor(journey, Catalog, true);

        var result = processor.ApplyCatchUp(
        [
            Parse("""{"timestamp":"2026-07-01T00:00:00Z","event":"Fileheader","Odyssey":true}"""),
            Parse("""{"timestamp":"2026-07-01T00:01:00Z","event":"Scan","SystemAddress":42,"BodyID":4,"PlanetClass":"Water world","TerraformState":"Terraformable","MassEM":1.2,"WasDiscovered":false,"WasMapped":false}"""),
            Parse("""{"timestamp":"2026-07-01T00:02:00Z","event":"SAAScanComplete","SystemAddress":42,"BodyID":4,"ProbesUsed":4,"EfficiencyTarget":6}"""),
        ]);

        var counts = result.Journey.CurrentSystem!.Counts;
        var mappedReward = ExplorationValueCalculator.Calculate(
            new ExplorationValueRequest
            {
                BodyClass = "Water world",
                IsTerraformable = true,
                Mass = 1.2,
                IsFirstDiscoverer = true,
                IsMapped = true,
                IsFirstMapped = true,
                IsOdyssey = true,
                WithEfficiencyBonus = true
            });
        Assert.Equal(1, result.ProcessedEventCount);
        Assert.Equal(2, result.IgnoredEventCount);
        Assert.Equal(1, counts.BodyScans);
        Assert.Equal(1, counts.DetailedSurfaceScans);
        Assert.Equal(mappedReward, counts.ExplorationRewards);
    }

    [Fact]
    public void LiveProcessingAcceptsEqualWatermarkAndRejectsOlderEvents()
    {
        var visit = CreateVisit();
        var journey = CreateJourney([visit]) with
        {
            Watermark = DateTimeOffset.Parse("2026-07-01T00:01:00Z"),
        };
        var processor = new JourneyJournalProcessor(journey, Catalog, true);

        var older = processor.Apply(Parse(
            """{"timestamp":"2026-07-01T00:00:59Z","event":"Screenshot"}"""));
        var equal = processor.Apply(Parse(
            """{"timestamp":"2026-07-01T00:01:00Z","event":"Screenshot"}"""));

        Assert.False(older);
        Assert.True(equal);
        Assert.Equal(1, processor.Journey.CurrentSystem!.Counts.Screenshots);
    }

    [Fact]
    public void UnknownAndMalformedArrivalsStillAdvanceWatermarkSafely()
    {
        var processor = new JourneyJournalProcessor(CreateJourney(), Catalog, true);

        Assert.True(processor.Apply(Parse(
            """{"timestamp":"2026-07-01T00:00:01Z","event":"FutureEvent"}""")));
        Assert.True(processor.Apply(Parse(
            """{"timestamp":"2026-07-01T00:00:02Z","event":"Location","StarSystem":"Missing position","SystemAddress":42}""")));

        Assert.Empty(processor.Journey.VisitedSystems);
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-01T00:00:02Z"),
            processor.Journey.Watermark);
    }

    private static JourneyDocument CreateJourney(
        IReadOnlyList<JourneySystemVisit>? visits = null)
    {
        return new JourneyDocument(
            "20260701_000000",
            "journey.json",
            "F123",
            "Drew",
            "Test journey",
            string.Empty,
            "Journal.2026-07-01T000000.01.log",
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            null,
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            visits ?? []);
    }

    private static JourneySystemVisit CreateVisit()
    {
        return new JourneySystemVisit(
            new JourneySystemReference(
                "Sol",
                42,
                new SrvSurvey.Core.Search.GalacticCoordinate(0, 0, 0)),
            DateTimeOffset.Parse("2026-07-01T00:00:01Z"),
            null,
            JourneyCounts.Empty,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var journalEvent, out var error),
            error);
        return journalEvent!;
    }
}
