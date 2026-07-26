using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Exobiology;

public sealed class SurfaceSurveyJournalTrackerTests : IDisposable
{
    private const string AleoidaVariant = "$Codex_Ent_Aleoids_01_B_Name;";
    private const string AleoidaSpecies = "$Codex_Ent_Aleoids_01_Name;";
    private const string AleoidaGenus = "$Codex_Ent_Aleoids_Genus_Name;";
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-surface-tracker-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ThreeSamplesBecomeLegacyCompletedSurfaceHistory()
    {
        var (tracker, store) = CreateTracker();
        var session = Session();

        await ApplyAtAsync(tracker, session, 1, 2, Organic("Log"));
        await ApplyAtAsync(tracker, session, 2, 3, Organic("Sample"));
        var result = await ApplyAtAsync(
            tracker,
            session,
            3,
            4,
            Organic("Analyse"));

        Assert.Equal(3, result.MutationCount);
        var loaded = await store.LoadBodyAsync(BodyContext());
        Assert.Equal(3, loaded.Snapshot!.BioScans.Count);
        Assert.All(
            loaded.Snapshot.BioScans,
            scan => Assert.Equal("Complete", scan.Status));
        Assert.Equal(
            [
                new SurfaceCoordinate(3, 4),
                new SurfaceCoordinate(1, 2),
                new SurfaceCoordinate(2, 3),
            ],
            loaded.Snapshot.BioScans.Select(scan => scan.Location));
    }

    [Fact]
    public async Task SwitchingSpeciesPreservesPriorSamplesAsBookmarks()
    {
        var other = new ExobiologyReference(
            2320201,
            "$Codex_Ent_Bacterial_01_A_Name;",
            "$Codex_Ent_Bacterial_01_Name;",
            "Bacterium",
            1_000_000);
        var (tracker, store) = CreateTracker(other);
        var session = Session();
        var options = new SurfaceSurveyTrackingOptions(false, false);

        await ApplyAtAsync(tracker, session, 1, 2, Organic("Log"), options);
        await ApplyAtAsync(tracker, session, 2, 3, Organic("Sample"), options);
        await ApplyAtAsync(
            tracker,
            session,
            4,
            5,
            Organic(
                "Log",
                other.VariantName,
                other.SpeciesName,
                "$Codex_Ent_Bacterial_Genus_Name;"),
            options);

        var loaded = await store.LoadBodyAsync(BodyContext());
        Assert.Equal(2, loaded.Snapshot!.Bookmarks[AleoidaGenus].Count);
    }

    [Fact]
    public async Task SamplingRemovesOnlyNearbyMatchingTrackerByDefault()
    {
        var (tracker, store) = CreateTracker();
        await store.AddBookmarkAsync(
            BodyContext(),
            AleoidaGenus,
            new SurfaceCoordinate(0, 0));
        await store.AddBookmarkAsync(
            BodyContext(),
            AleoidaGenus,
            new SurfaceCoordinate(0, 20));

        await ApplyAtAsync(
            tracker,
            Session(),
            0,
            1,
            Organic("Log"));

        var loaded = await store.LoadBodyAsync(BodyContext());
        Assert.Equal(
            new SurfaceCoordinate(0, 20),
            Assert.Single(loaded.Snapshot!.Bookmarks[AleoidaGenus]));
    }

    [Fact]
    public async Task TouchdownAndSrvLifecycleRetainNavigationMarkers()
    {
        var (tracker, store) = CreateTracker();
        var session = Session();

        await tracker.ApplyAsync(
            session,
            [Event(
                """
                {"event":"Touchdown","StarSystem":"Test System","SystemAddress":42,"Body":"Test System 1 a","BodyID":7,"Latitude":1,"Longitude":2}
                """)],
            Status(1, 2));
        await tracker.ApplyAsync(
            session,
            [Event("{\"event\":\"Disembark\",\"SRV\":true}")],
            Status(3, 4));

        Assert.Equal(new SurfaceCoordinate(1, 2), tracker.ShipLocation);
        Assert.False(tracker.HasShipDeparted);
        Assert.Equal(new SurfaceCoordinate(3, 4), tracker.SrvLocation);
        Assert.Equal(
            new SurfaceCoordinate(1, 2),
            (await store.LoadBodyAsync(BodyContext())).Snapshot!.LastTouchdown);

        await tracker.ApplyAsync(
            session,
            [Event("{\"event\":\"Liftoff\"}")],
            Status(3, 4));
        Assert.Equal(new SurfaceCoordinate(1, 2), tracker.ShipLocation);
        Assert.True(tracker.HasShipDeparted);

        await tracker.ApplyAsync(
            session,
            [Event(
                """
                {"event":"Touchdown","StarSystem":"Test System","SystemAddress":42,"Body":"Test System 1 a","BodyID":7,"Latitude":5,"Longitude":6}
                """)],
            Status(5, 6));
        Assert.False(tracker.HasShipDeparted);

        await tracker.ApplyAsync(
            session,
            [Event("{\"event\":\"Embark\",\"SRV\":true}")],
            Status(3, 4));
        Assert.Null(tracker.SrvLocation);

        await tracker.ApplyAsync(
            session,
            [Event("{\"event\":\"LeaveBody\"}")],
            Status(3, 4));
        Assert.Null(tracker.ShipLocation);
    }

    [Theory]
    [InlineData("LeaveBody")]
    [InlineData("StartJump")]
    [InlineData("SupercruiseEntry")]
    [InlineData("FSDJump")]
    [InlineData("CarrierJump")]
    [InlineData("Shutdown")]
    [InlineData("Died")]
    [InlineData("Resurrect")]
    public async Task SessionDepartureClearsOnlyVehicleLocations(
        string eventName)
    {
        var (tracker, store) = CreateTracker();
        var session = Session();
        await tracker.ApplyAsync(
            session,
            [Event(
                """
                {"event":"Touchdown","StarSystem":"Test System","SystemAddress":42,"Body":"Test System 1 a","BodyID":7,"Latitude":1,"Longitude":2}
                """)],
            Status(1, 2));
        await tracker.ApplyAsync(
            session,
            [Event("{\"event\":\"Liftoff\"}")],
            Status(1, 2));

        var result = await tracker.ApplyAsync(
            session,
            [Event($$"""{"event":"{{eventName}}"}""")],
            Status(1, 2));

        Assert.Equal(1, result.MutationCount);
        Assert.Null(tracker.ShipLocation);
        Assert.Null(tracker.SrvLocation);
        Assert.False(tracker.HasShipDeparted);
        Assert.Equal(
            new SurfaceCoordinate(1, 2),
            (await store.LoadBodyAsync(BodyContext())).Snapshot!.LastTouchdown);
    }

    [Fact]
    public async Task MainMenuClearsVehicleLocations()
    {
        var (tracker, _) = CreateTracker();
        await tracker.ApplyAsync(
            Session(),
            [Event("{\"event\":\"Liftoff\"}")],
            Status(1, 2));

        var result = await tracker.ApplyAsync(
            Session(),
            [Event("{\"event\":\"Music\",\"MusicTrack\":\"MainMenu\"}")],
            Status(1, 2));

        Assert.Equal(1, result.MutationCount);
        Assert.False(tracker.HasShipDeparted);
    }

    [Fact]
    public async Task CompositionScanAddsGenusTrackerAtReportedCoordinates()
    {
        var (tracker, store) = CreateTracker();

        var result = await tracker.ApplyAsync(
            Session(),
            [Event(CodexEntry(latitude: 5, longitude: 6))],
            Status(1, 2));

        Assert.Equal(1, result.MutationCount);
        var loaded = await store.LoadBodyAsync(BodyContext());
        Assert.Equal(
            new SurfaceCoordinate(5, 6),
            Assert.Single(loaded.Snapshot!.Bookmarks[AleoidaGenus]));
    }

    [Fact]
    public async Task CompositionTrackingHonorsAnalyzedAndFixedSignalFilters()
    {
        var (tracker, store) = CreateTracker();
        var options = new SurfaceSurveyTrackingOptions(
            false,
            false,
            AutoTrackCompositionScans: true,
            SkipAnalyzedCompositionScans: true,
            new HashSet<string>(StringComparer.Ordinal) { AleoidaSpecies });

        var result = await tracker.ApplyAsync(
            Session(),
            [
                Event(CodexEntry(latitude: 5, longitude: 6)),
                Event(CodexEntry(
                    latitude: 7,
                    longitude: 8,
                    nearestDestination: "$Fixed_Event_Life_Cloud;")),
            ],
            Status(1, 2),
            options);

        Assert.Equal(0, result.MutationCount);
        Assert.Empty((await store.LoadBodyAsync(BodyContext()))
            .Snapshot!.Bookmarks);
    }

    [Fact]
    public async Task MissingSurfaceContextIsNonFatalAndReported()
    {
        var (tracker, _) = CreateTracker();

        var result = await tracker.ApplyAsync(
            Session(),
            [Event(Organic("Log"))],
            new EliteStatus());

        Assert.Equal(0, result.MutationCount);
        Assert.Single(result.Warnings);
    }

    private (SurfaceSurveyJournalTracker Tracker, SystemSurfaceStore Store)
        CreateTracker(params ExobiologyReference[] additional)
    {
        var reference = new ExobiologyReference(
            2310101,
            AleoidaVariant,
            AleoidaSpecies,
            "Aleoida Arcus - Yellow",
            7_252_500,
            HudCategory: "Biology");
        var catalog = new ExobiologyReferenceCatalog([reference, .. additional]);
        var store = new SystemSurfaceStore(temporaryDirectory);
        return (new SurfaceSurveyJournalTracker(store, catalog), store);
    }

    private static async Task<SurfaceSurveyJournalUpdateResult> ApplyAtAsync(
        SurfaceSurveyJournalTracker tracker,
        SurfaceSurveySessionContext session,
        double latitude,
        double longitude,
        string json,
        SurfaceSurveyTrackingOptions? options = null)
    {
        return await tracker.ApplyAsync(
            session,
            [Event(json)],
            Status(latitude, longitude),
            options);
    }

    private static EliteStatus Status(double latitude, double longitude)
    {
        return new EliteStatus
        {
            Flags = StatusFlags.HasLatLong | StatusFlags.InMainShip,
            Latitude = latitude,
            Longitude = longitude,
            PlanetRadius = 1_000,
            BodyName = "Test System 1 a",
        };
    }

    private static string Organic(
        string scanType,
        string variant = AleoidaVariant,
        string species = AleoidaSpecies,
        string genus = AleoidaGenus)
    {
        return $$"""
        {"event":"ScanOrganic","ScanType":"{{scanType}}","Genus":"{{genus}}","Species":"{{species}}","Variant":"{{variant}}","SystemAddress":42,"Body":7}
        """;
    }

    private static string CodexEntry(
        double latitude,
        double longitude,
        string? nearestDestination = null)
    {
        var destination = nearestDestination is null
            ? string.Empty
            : $",\"NearestDestination\":\"{nearestDestination}\"";
        return $$"""
        {"event":"CodexEntry","SubCategory":"$Codex_SubCategory_Organic_Structures;","EntryID":"2310101","SystemAddress":42,"BodyID":7,"Latitude":{{latitude}},"Longitude":{{longitude}}{{destination}}}
        """;
    }

    private static SurfaceSurveySessionContext Session()
    {
        return new SurfaceSurveySessionContext(
            "F123",
            "Drew",
            "Test System",
            42,
            null);
    }

    private static SystemSurfaceContext BodyContext()
    {
        return new SystemSurfaceContext(
            "F123",
            "Drew",
            "Test System",
            42,
            null,
            7,
            "Test System 1 a",
            1_000);
    }

    private static JournalEventEnvelope Event(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var journalEvent, out var error),
            error);
        return journalEvent!;
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
