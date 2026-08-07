using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Exobiology;

public sealed class ExobiologyStateTests
{
    private const string AleoidaVariant = "$Codex_Ent_Aleoids_01_B_Name;";
    private const string AleoidaSpecies = "$Codex_Ent_Aleoids_01_Name;";
    private const string AleoidaGenus = "$Codex_Ent_Aleoids_Genus_Name;";

    private static readonly ExobiologyReference Aleoida = new(
        2310101,
        AleoidaVariant,
        AleoidaSpecies,
        "Aleoida Arcus - Yellow",
        7_252_500);

    [Fact]
    public void ThreeSamplesTrackActiveStateAndFirstFootfallReward()
    {
        var state = CreateState();
        state.UpdateStatus(new EliteStatus
        {
            Flags = StatusFlags.HasLatLong,
            Latitude = 12.5,
            Longitude = -45.25,
            BodyName = "Test A 1",
        });
        ApplyFirstFootfall(state);

        Assert.True(state.Apply(Event(Organic("Log"))));
        Assert.NotNull(state.ScanOne);
        Assert.Null(state.ScanTwo);
        Assert.Equal(new SurfaceLocation(12.5, -45.25), state.ScanOne.Location);
        Assert.Equal(150, state.ScanOne.Radius);

        Assert.True(state.Apply(Event(Organic("Sample"))));
        Assert.NotNull(state.ScanTwo);

        Assert.True(state.Apply(Event(Organic("Analyse"))));
        var snapshot = state.CreateSnapshot();
        Assert.Null(snapshot.LastOrganicScan);
        Assert.Null(snapshot.ScanOne);
        Assert.Null(snapshot.ScanTwo);
        Assert.Equal(36_262_500, snapshot.OrganicRewards);
        Assert.Equal(
            "123456_7_2310101_7252500_True",
            Assert.Single(snapshot.ScannedBioEntryIds));
    }

    [Fact]
    public void SwitchingSpeciesAbandonsPriorActiveSamples()
    {
        var other = new ExobiologyReference(
            2320201,
            "$Codex_Ent_Bacterial_01_A_Name;",
            "$Codex_Ent_Bacterial_01_Name;",
            "Bacterium",
            1_000_000);
        var state = CreateState(other);
        state.Apply(Event(Organic("Log")));
        state.Apply(Event(Organic("Sample")));

        state.Apply(Event(Organic(
            "Log",
            other.VariantName,
            other.SpeciesName,
            "$Codex_Ent_Bacterial_Genus_Name;")));

        Assert.Equal(other.SpeciesName, state.ScanOne?.Species);
        Assert.Null(state.ScanTwo);
    }

    [Fact]
    public void SwitchingBodyOnNewOrganicAbandonsPriorActiveSamples()
    {
        var state = CreateState();
        state.UpdateStatus(new EliteStatus
        {
            Flags = StatusFlags.HasLatLong,
            Latitude = 1,
            Longitude = 2,
            BodyName = "Test System 1",
        });
        Assert.True(state.Apply(Event(Organic("Log", bodyId: 7))));
        Assert.Equal("Test System 1", state.ScanOne?.Body);

        state.UpdateStatus(new EliteStatus
        {
            Flags = StatusFlags.HasLatLong,
            Latitude = 3,
            Longitude = 4,
            BodyName = "Test System 2",
        });
        Assert.True(state.Apply(Event(Organic("Log", bodyId: 8))));

        Assert.NotNull(state.ScanOne);
        Assert.Equal("Test System 2", state.ScanOne.Body);
        Assert.Null(state.ScanTwo);
        Assert.Equal("123456|8|" + AleoidaSpecies, state.LastOrganicScan);
    }

    [Fact]
    public void StatusBodyChangeWithoutNewOrganicKeepsStaleActiveSample()
    {
        var state = CreateState();
        state.UpdateStatus(new EliteStatus
        {
            Flags = StatusFlags.HasLatLong,
            Latitude = 1,
            Longitude = 2,
            BodyName = "Test System 1",
        });
        Assert.True(state.Apply(Event(Organic("Log", bodyId: 7))));

        state.UpdateStatus(new EliteStatus
        {
            Flags = StatusFlags.HasLatLong,
            Latitude = 5,
            Longitude = 6,
            BodyName = "Test System 2",
        });
        state.Apply(Event(
            """{"event":"ApproachBody","Body":"Test System 2","SystemAddress":123456}"""));

        // Legacy keeps the active sample and surfaces a stale-body warning in UI.
        Assert.NotNull(state.ScanOne);
        Assert.Equal("Test System 1", state.ScanOne.Body);
        Assert.Equal("123456|7|" + AleoidaSpecies, state.LastOrganicScan);
    }

    [Fact]
    public void StatusComputesDistanceRemainingFromNearestActiveSample()
    {
        var state = CreateState();
        state.UpdateStatus(new EliteStatus
        {
            Flags = StatusFlags.HasLatLong,
            Latitude = 0,
            Longitude = 0,
            PlanetRadius = 1_000,
        });
        state.Apply(Event(Organic("Log")));

        state.UpdateStatus(new EliteStatus
        {
            Flags = StatusFlags.HasLatLong,
            Latitude = 0,
            Longitude = 1,
            PlanetRadius = 1_000,
        });

        Assert.Equal(17.453, state.NearestActiveSampleDistance!.Value, 3);
        Assert.Equal(150, state.RequiredSampleDistance);
        Assert.Equal(132.547, state.RemainingSampleDistance!.Value, 3);
    }

    [Fact]
    public void SaleRemovesOnlyMatchingUnclaimedSpeciesAndRecalculates()
    {
        var radicoida = new ExobiologyReference(
            2460101,
            ExobiologyState.RadicoidaUnicaSpecies,
            ExobiologyState.RadicoidaUnicaSpecies,
            "Radicoida Unica",
            119_037);
        var state = CreateState(radicoida);
        Complete(state, Organic("Log"), Organic("Sample"), Organic("Analyse"));
        Complete(
            state,
            Organic(
                "Log",
                radicoida.VariantName,
                radicoida.SpeciesName,
                "$Codex_Ent_Ingensradices_Genus_Name;"),
            Organic(
                "Sample",
                radicoida.VariantName,
                radicoida.SpeciesName,
                "$Codex_Ent_Ingensradices_Genus_Name;"),
            Organic(
                "Analyse",
                radicoida.VariantName,
                radicoida.SpeciesName,
                "$Codex_Ent_Ingensradices_Genus_Name;"));
        Assert.Equal(2, state.UnclaimedScanCount);
        Assert.Equal(1, state.CountRadicoidaUnica);

        state.Apply(Event(
            $$"""
            {"event":"SellOrganicData","BioData":[{"Species":"{{radicoida.SpeciesName}}","Value":119037,"Bonus":0}]}
            """));

        Assert.Equal(1, state.UnclaimedScanCount);
        Assert.Equal(7_252_500, state.OrganicRewards);
        Assert.Equal(0, state.CountRadicoidaUnica);
    }

    [Fact]
    public void DeathClearsUnclaimedAndActiveScansButPreservesCareerCounter()
    {
        var seed = new ExobiologySnapshot(
            "123|7|species",
            Sample(Aleoida),
            null,
            7_252_500,
            ["123_7_2310101_7252500_False"],
            4);
        var state = new ExobiologyState(
            new ExobiologyReferenceCatalog([Aleoida]),
            seed);

        state.Apply(Event("{\"event\":\"Died\"}"));

        Assert.Equal(0, state.OrganicRewards);
        Assert.Equal(0, state.UnclaimedScanCount);
        Assert.Null(state.ScanOne);
        Assert.Null(state.LastOrganicScan);
        Assert.Equal(4, state.CountRadicoidaUnica);
    }

    [Fact]
    public void ManualClearMatchesLegacyRewardResetWithoutClearingActiveScan()
    {
        var seed = new ExobiologySnapshot(
            "123|7|species",
            Sample(Aleoida),
            null,
            7_252_500,
            ["123_7_2310101_7252500_False"],
            0);
        var state = new ExobiologyState(
            new ExobiologyReferenceCatalog([Aleoida]),
            seed);

        state.ClearUnclaimedRewards();

        Assert.Equal(0, state.OrganicRewards);
        Assert.Equal(0, state.UnclaimedScanCount);
        Assert.NotNull(state.ScanOne);
        Assert.NotNull(state.LastOrganicScan);
    }

    [Fact]
    public void FirstFootfallCorrectionRewritesCompatibilityEntryAndReward()
    {
        var seed = new ExobiologySnapshot(
            null,
            null,
            null,
            7_252_500,
            ["123456_7_2310101_7252500_False"],
            0);
        var state = new ExobiologyState(
            new ExobiologyReferenceCatalog([Aleoida]),
            seed);

        state.SetFirstFootfall(123456, 7, true);

        Assert.Equal(36_262_500, state.OrganicRewards);
        Assert.EndsWith("_True", Assert.Single(state.CreateSnapshot().ScannedBioEntryIds));
    }

    [Fact]
    public void CurrentBodyFirstFootfallToggleRequiresBodyContext()
    {
        var state = CreateState();
        Assert.False(state.ToggleCurrentBodyFirstFootfall());
        state.Apply(Event(
            "{\"event\":\"Scan\",\"SystemAddress\":123456,\"BodyID\":7,"
                + "\"BodyName\":\"Test A 1\",\"WasFootfalled\":true}"));

        Assert.True(state.ToggleCurrentBodyFirstFootfall());
        Assert.True(state.CurrentBodyFirstFootfall);
        Assert.True(state.ToggleCurrentBodyFirstFootfall());
        Assert.False(state.CurrentBodyFirstFootfall);
    }

    [Fact]
    public void FirstFootfallCorrectionWithoutOrganicScansAdvancesVersion()
    {
        var state = CreateState();
        state.Apply(Event(
            "{\"event\":\"Scan\",\"SystemAddress\":123456,\"BodyID\":7,"
                + "\"BodyName\":\"Test A 1\",\"WasFootfalled\":true}"));
        var before = state.Version;

        Assert.True(state.SetCurrentBodyFirstFootfall(true));

        Assert.Equal(before + 1, state.Version);
        Assert.True(state.CurrentBodyFirstFootfall);
        Assert.Empty(state.CreateSnapshot().ScannedBioEntryIds);
    }

    private static ExobiologyState CreateState(
        params ExobiologyReference[] additional)
    {
        return new ExobiologyState(
            new ExobiologyReferenceCatalog([Aleoida, .. additional]));
    }

    private static void ApplyFirstFootfall(ExobiologyState state)
    {
        state.Apply(Event(
            "{\"event\":\"Location\",\"SystemAddress\":123456,\"Population\":0}"));
        state.Apply(Event(
            "{\"event\":\"Scan\",\"SystemAddress\":123456,\"BodyID\":7,\"BodyName\":\"Test A 1\",\"WasFootfalled\":false}"));
        state.Apply(Event(
            "{\"event\":\"Disembark\",\"SystemAddress\":123456,\"BodyID\":7,\"OnPlanet\":true,\"OnStation\":false}"));
    }

    private static void Complete(
        ExobiologyState state,
        params string[] events)
    {
        foreach (var json in events)
        {
            state.Apply(Event(json));
        }
    }

    private static string Organic(
        string scanType,
        string variant = AleoidaVariant,
        string species = AleoidaSpecies,
        string genus = AleoidaGenus,
        int bodyId = 7)
    {
        return $$"""
        {"event":"ScanOrganic","ScanType":"{{scanType}}","Genus":"{{genus}}","Species":"{{species}}","Variant":"{{variant}}","SystemAddress":123456,"Body":{{bodyId}}}
        """;
    }

    private static BioSampleSnapshot Sample(ExobiologyReference reference)
    {
        return new BioSampleSnapshot(
            new SurfaceLocation(1, 2),
            150,
            AleoidaGenus,
            reference.SpeciesName,
            "Active",
            reference.EntryId,
            "Test A 1");
    }

    private static JournalEventEnvelope Event(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var journalEvent, out var error),
            error);
        return journalEvent!;
    }
}
