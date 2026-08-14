using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class BoxelSurveyStatsStateTests
{
    private const long SystemAAddress = 2001;
    private const long SystemBAddress = 2002;
    private const long SystemCAddress = 2003;
    private const string SystemA = "Praea Euq IL-P c5-0";
    private const string SystemB = "Wregoe BU-Y b2-0";
    private const string SystemA4 = "Praea Euq IL-P c5-4";

    [Fact]
    public void MapsAllNineteenPlanetClassesFromScan()
    {
        var state = new BoxelSurveyStatsState();
        Jump(state, SystemA, SystemAAddress);
        var bodyId = 1;
        foreach (var pair in BoxelPlanetClassifierTests.JournalPlanetClasses)
        {
            var planetClass = Assert.IsType<string>(pair[0]);
            var classified = Assert.IsType<BoxelPlanetClass>(pair[1]);
            ScanPlanet(state, SystemAAddress, bodyId, planetClass);
            bodyId++;
            Assert.True(state.TryGet(Prefix(SystemA), out var snapshot));
            Assert.Equal(1, snapshot.CountsOf(classified).Count);
        }

        Assert.Equal(19, bodyId - 1);
    }

    [Fact]
    public void HeliumFromNamelessScanAttachesToAlreadyOpenedSystemNotLastJump()
    {
        var state = new BoxelSurveyStatsState();
        Jump(state, SystemB, SystemBAddress);
        Jump(state, SystemA, SystemAAddress);
        Assert.True(state.Apply(Parse(
            $$"""
            {"timestamp":"2026-07-10T12:10:00Z","event":"Scan","SystemAddress":{{SystemBAddress}},"BodyID":3,"BodyName":"Wregoe BU-Y b2-0 A","PlanetClass":"Sudarsky class I gas giant","MassEM":20.1,"AtmosphereType":"Helium","AtmosphereComposition":[{"Name":"Helium","Percent":28.5}]}
            """)));

        Assert.True(state.TryGet(Prefix(SystemB), out var boxelB));
        Assert.True(state.TryGet(Prefix(SystemA), out var boxelA));
        Assert.Equal(28.5, boxelB.MinHeliumPercent);
        Assert.Equal(28.5, boxelB.MaxHeliumPercent);
        Assert.Null(boxelA.MinHeliumPercent);
        var systemB = Assert.Single(boxelB.Systems);
        Assert.Equal(SystemB, systemB.GeneratedName);
        Assert.Equal(28.5, systemB.MinHeliumPercent);
    }

    [Fact]
    public void NamelessScanForUnknownAddressIsIgnored()
    {
        var state = new BoxelSurveyStatsState();
        Jump(state, SystemA, SystemAAddress);
        Assert.False(state.Apply(Parse(
            $$"""
            {"timestamp":"2026-07-10T12:10:00Z","event":"Scan","SystemAddress":{{SystemCAddress}},"BodyID":1,"PlanetClass":"Earthlike body","MassEM":1,"AtmosphereComposition":[{"Name":"Helium","Percent":12}]}
            """)));

        Assert.Equal(1, state.BoxelCount);
        Assert.True(state.TryGet(Prefix(SystemA), out var snapshot));
        Assert.Null(snapshot.MinHeliumPercent);
        Assert.Equal(0, snapshot.CountsOf(BoxelPlanetClass.Earthlike).Count);
    }

    [Fact]
    public void EmptySnapshotDoesNotWipeFiveBodySystem()
    {
        var state = new BoxelSurveyStatsState();
        Jump(state, SystemA, SystemAAddress, "2026-07-10T12:00:00Z");
        var bodies = Enumerable.Range(1, 5)
            .Select(id => PlanetBody(
                id,
                "Icy body",
                mass: id,
                helium: 10 + id,
                scanValue: 100 * id,
                mappedValue: 200 * id,
                currentValue: 100 * id))
            .ToArray();
        Assert.True(state.IngestSnapshot(
            Snapshot(SystemA, SystemAAddress, bodies, expectedBodyCount: 8)));

        Jump(state, SystemB, SystemBAddress, "2026-07-10T12:05:00Z");
        Jump(state, SystemA, SystemAAddress, "2026-07-10T12:10:00Z");
        Assert.True(state.IngestSnapshot(
            Snapshot(SystemA, SystemAAddress, [], expectedBodyCount: 0)));

        Assert.True(state.TryGet(Prefix(SystemA), out var snapshot));
        Assert.Equal(5, snapshot.CountsOf(BoxelPlanetClass.Icy).Count);
        Assert.Equal(11, snapshot.MinHeliumPercent);
        Assert.Equal(15, snapshot.MaxHeliumPercent);
        Assert.Equal(1500, snapshot.CurrentValue);
        Assert.Equal(3000, snapshot.MappedPotentialValue);
        Assert.Equal(8, snapshot.FssDiscoveryBodyCountSum);
        Assert.Equal(1, snapshot.Visited);
    }

    [Fact]
    public void SolColoniaAndPermitNamesDoNotOpenCubes()
    {
        var state = new BoxelSurveyStatsState();
        Assert.False(state.Apply(Parse(
            """{"timestamp":"2026-07-10T12:00:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":10477373803,"StarPos":[0,0,0]}""")));
        Assert.False(state.Apply(Parse(
            """{"timestamp":"2026-07-10T12:01:00Z","event":"Location","StarSystem":"Colonia","SystemAddress":3238296097059}""")));
        Assert.False(state.Apply(Parse(
            """{"timestamp":"2026-07-10T12:02:00Z","event":"CarrierJump","StarSystem":"Shinrarta Dezhra","SystemAddress":3932277478106}""")));
        Assert.False(state.Apply(Parse(
            """{"timestamp":"2026-07-10T12:03:00Z","event":"FSDJump","SystemAddress":10477373803}""")));
        Assert.Equal(0, state.BoxelCount);
        Assert.Null(state.Current);
    }

    [Fact]
    public void NamelessScanSaaAndNavBeaconAttachByAddressAfterGatedJump()
    {
        var state = new BoxelSurveyStatsState();
        Jump(state, SystemA, SystemAAddress);
        Assert.True(state.Apply(Parse(
            $$"""
            {"event":"Scan","SystemAddress":{{SystemAAddress}},"BodyID":4,"PlanetClass":"Rocky body","MassEM":0.2,"Landable":true,"AtmosphereType":"Nitrogen"}
            """)));
        Assert.True(state.Apply(Parse(
            $$"""
            {"event":"SAAScanComplete","SystemAddress":{{SystemAAddress}},"BodyID":4,"ProbesUsed":3,"EfficiencyTarget":5}
            """)));
        Assert.True(state.Apply(Parse(
            $$"""
            {"event":"NavBeaconScan","SystemAddress":{{SystemAAddress}},"NumBodies":6}
            """)));

        Assert.True(state.TryGet(Prefix(SystemA), out var snapshot));
        Assert.Equal(1, snapshot.CountsOf(BoxelPlanetClass.Rocky).Count);
        Assert.Equal(1, snapshot.CountsOf(BoxelPlanetClass.Rocky).Atmospheric);
        Assert.Equal(1, snapshot.NavBeaconCount);
        Assert.Equal(0, snapshot.FssCompleteCount);
        var body = Assert.Single(Assert.Single(snapshot.Systems).Bodies);
        Assert.True(body.DssComplete);
        Assert.True(body.CurrentValue > body.ScanValue);
    }

    [Fact]
    public void ImpliedPopulationIsMaxN2PlusOne()
    {
        var state = new BoxelSurveyStatsState();
        Jump(state, SystemA, SystemAAddress);
        Jump(state, SystemA4, 2014);
        Assert.True(state.TryGet(Prefix(SystemA), out var snapshot));
        Assert.Equal(2, snapshot.Visited);
        Assert.Equal(5, snapshot.ImpliedPopulation);
    }

    [Fact]
    public void SameGeneratedNameReplacesAndDifferentNamesStayDistinct()
    {
        var state = new BoxelSurveyStatsState();
        Jump(state, SystemA, SystemAAddress);
        Jump(state, SystemA, 9001);
        Jump(state, SystemA4, 2014);
        Assert.True(state.TryGet(Prefix(SystemA), out var snapshot));
        Assert.Equal(2, snapshot.Visited);
        Assert.Contains(snapshot.Systems, system => system.SystemAddress == 9001);
        Assert.DoesNotContain(snapshot.Systems, system => system.SystemAddress == SystemAAddress);
    }

    [Fact]
    public void FullyScannedIsFssOnlyUnlessNavBeaconSettingIsOn()
    {
        var state = new BoxelSurveyStatsState();
        Jump(state, SystemA, SystemAAddress);
        state.Apply(Parse(
            $$"""{"event":"FSSAllBodiesFound","SystemName":"{{SystemA}}","SystemAddress":{{SystemAAddress}},"Count":4}"""));
        Jump(state, SystemA4, 2014);
        state.Apply(Parse(
            $$"""{"event":"NavBeaconScan","SystemAddress":2014}"""));

        Assert.True(state.TryGet(Prefix(SystemA), out var snapshot));
        Assert.Equal(1, snapshot.FssCompleteCount);
        Assert.Equal(1, snapshot.NavBeaconCount);
        Assert.Equal(2, snapshot.Visited);

        state.TreatNavBeaconAsFullyScanned = true;
        Assert.True(state.TryGet(Prefix(SystemA), out snapshot));
        Assert.Equal(2, snapshot.FssCompleteCount);
    }

    [Fact]
    public void HonkBodyCountIsMonotonicAndIgnoresSnapshotFssBodyCount()
    {
        var state = new BoxelSurveyStatsState();
        Jump(state, SystemA, SystemAAddress);
        state.Apply(Parse(
            $$"""{"event":"FSSDiscoveryScan","SystemAddress":{{SystemAAddress}},"BodyCount":12}"""));
        state.Apply(Parse(
            $$"""{"event":"FSSDiscoveryScan","SystemAddress":{{SystemAAddress}},"BodyCount":7}"""));
        var planet = PlanetBody(1, "Rocky body", scanValue: 10, mappedValue: 20, currentValue: 10);
        state.IngestSnapshot(
            Snapshot(
                SystemA,
                SystemAAddress,
                [planet],
                expectedBodyCount: 4,
                fssBodyCount: 1));

        Assert.True(state.TryGet(Prefix(SystemA), out var snapshot));
        Assert.Equal(12, snapshot.FssDiscoveryBodyCountSum);
    }

    [Fact]
    public void StarsAreExcludedFromValueAndHistogram()
    {
        var state = new BoxelSurveyStatsState();
        Jump(state, SystemA, SystemAAddress);
        Assert.True(state.Apply(Parse(
            $$"""
            {"event":"Scan","SystemAddress":{{SystemAAddress}},"BodyID":0,"StarType":"K","StellarMass":0.8}
            """)));
        ScanPlanet(state, SystemAAddress, 1, "Water world", mass: 1);

        Assert.True(state.TryGet(Prefix(SystemA), out var snapshot));
        Assert.Equal(0, snapshot.CountsOf(BoxelPlanetClass.Unknown).Count);
        Assert.Equal(1, snapshot.CountsOf(BoxelPlanetClass.WaterWorld).Count);
        var expected = ExplorationValueCalculator.Calculate(
            new ExplorationValueRequest
            {
                BodyClass = "Water world",
                Mass = 1,
                IsFirstDiscoverer = true,
                IsFirstMapped = true,
                IsOdyssey = true,
            });
        Assert.Equal(expected, snapshot.ScanValue);
        Assert.Equal(expected, snapshot.CurrentValue);
        Assert.True(snapshot.MappedPotentialValue > snapshot.CurrentValue);
    }

    [Fact]
    public void SecondScanOfSameBodyDoesNotDoubleCount()
    {
        var state = new BoxelSurveyStatsState();
        Jump(state, SystemA, SystemAAddress);
        ScanPlanet(state, SystemAAddress, 5, "Earthlike body", mass: 1);
        ScanPlanet(state, SystemAAddress, 5, "Earthlike body", mass: 1);
        Assert.True(state.TryGet(Prefix(SystemA), out var snapshot));
        Assert.Equal(1, snapshot.CountsOf(BoxelPlanetClass.Earthlike).Count);
        Assert.Single(Assert.Single(snapshot.Systems).Bodies);
    }

    [Fact]
    public void SnapshotMergesTerraformableFlagWithoutDuplicatingBody()
    {
        var state = new BoxelSurveyStatsState();
        Jump(state, SystemA, SystemAAddress);
        ScanPlanet(state, SystemAAddress, 2, "Water world", mass: 1);
        var tf = PlanetBody(
            2,
            "Water world",
            terraformable: true,
            mass: 1,
            scanValue: 50,
            mappedValue: 80,
            currentValue: 50);
        state.IngestSnapshot(Snapshot(SystemA, SystemAAddress, [tf]));

        Assert.True(state.TryGet(Prefix(SystemA), out var snapshot));
        Assert.Equal(1, snapshot.CountsOf(BoxelPlanetClass.WaterWorld).Count);
        Assert.Equal(1, snapshot.CountsOf(BoxelPlanetClass.WaterWorld).Terraformable);
    }

    [Fact]
    public void SaaBeforeScanCreatesStubThenScanFillsClass()
    {
        var state = new BoxelSurveyStatsState();
        Jump(state, SystemA, SystemAAddress);
        Assert.True(state.Apply(Parse(
            $$"""
            {"event":"SAAScanComplete","SystemAddress":{{SystemAAddress}},"BodyID":8,"ProbesUsed":4,"EfficiencyTarget":6}
            """)));
        Assert.True(state.TryGet(Prefix(SystemA), out var before));
        Assert.Equal(0, before.Visited == 0 ? 0 : before.CountsOf(BoxelPlanetClass.HighMetalContent).Count);
        Assert.Single(Assert.Single(before.Systems).Bodies);

        ScanPlanet(state, SystemAAddress, 8, "High metal content body", mass: 0.4);
        Assert.True(state.TryGet(Prefix(SystemA), out var after));
        Assert.Equal(1, after.CountsOf(BoxelPlanetClass.HighMetalContent).Count);
        var body = Assert.Single(Assert.Single(after.Systems).Bodies);
        Assert.True(body.DssComplete);
        Assert.Equal(BoxelPlanetClass.HighMetalContent, body.Class);
        Assert.True(body.CurrentValue > 0);
    }

    [Fact]
    public void OdysseyDefaultsTrueAndFileheaderApplies()
    {
        var withDefault = new BoxelSurveyStatsState();
        Jump(withDefault, SystemA, SystemAAddress);
        ScanPlanet(withDefault, SystemAAddress, 1, "Rocky body", mass: 0.2, terraformable: true);
        Assert.True(withDefault.TryGet(Prefix(SystemA), out var odysseySnapshot));

        var horizons = new BoxelSurveyStatsState();
        Assert.True(horizons.Apply(Parse(
            """{"event":"Fileheader","Odyssey":false}""")));
        Jump(horizons, SystemA, SystemAAddress);
        ScanPlanet(horizons, SystemAAddress, 1, "Rocky body", mass: 0.2, terraformable: true);
        Assert.True(horizons.TryGet(Prefix(SystemA), out var horizonsSnapshot));

        Assert.True(odysseySnapshot.MappedPotentialValue > horizonsSnapshot.MappedPotentialValue);
    }

    [Fact]
    public void IngestSystemFileRecomputesValuesAndIgnoresReward()
    {
        var state = new BoxelSurveyStatsState();
        var expected = BoxelSurveyValueCalculator.Calculate(
            "Water world",
            terraformable: true,
            1,
            wasDiscovered: false,
            wasMapped: false,
            dssComplete: true,
            dssEfficiencyBonus: true,
            isOdyssey: true);
        var body = PlanetBody(
            1,
            "Water world",
            terraformable: true,
            mass: 1,
            scanValue: 999999,
            mappedValue: 999999,
            currentValue: 999999,
            dssComplete: true);
        Assert.True(state.IngestSystemFile(
            Snapshot(SystemA, SystemAAddress, [body], allBodiesFound: true),
            DateTimeOffset.Parse("2026-07-10T12:00:00Z")));

        Assert.True(state.TryGet(Prefix(SystemA), out var snapshot));
        Assert.Equal(expected.Scan, snapshot.ScanValue);
        Assert.Equal(expected.Current, snapshot.CurrentValue);
        Assert.Equal(expected.Mapped, snapshot.MappedPotentialValue);
        Assert.NotEqual(999999, snapshot.CurrentValue);
    }

    [Fact]
    public void RollupSumsPerPrefixImpliedPopulation()
    {
        var state = new BoxelSurveyStatsState();
        Jump(state, SystemA, SystemAAddress);
        Jump(state, SystemA4, 2014);
        Jump(state, SystemB, SystemBAddress);
        var rollup = state.Rollup([Prefix(SystemA), Prefix(SystemB)]);
        Assert.Equal(3, rollup.Visited);
        Assert.Equal(6, rollup.ImpliedPopulation);
    }

    [Fact]
    public void ImportDocumentRoundTripsHydratedSystems()
    {
        var source = new BoxelSurveyStatsState();
        Jump(source, SystemA, SystemAAddress);
        ScanPlanet(source, SystemAAddress, 1, "Ammonia world", mass: 0.8);
        Assert.True(source.TryCreateDocument(Prefix(SystemA), out var document));

        var restored = new BoxelSurveyStatsState();
        Assert.True(restored.ImportDocument(document));
        Assert.True(restored.HasLoadedDocument(Prefix(SystemA)));
        Assert.True(restored.TryGet(Prefix(SystemA), out var snapshot));
        Assert.Equal(1, snapshot.CountsOf(BoxelPlanetClass.AmmoniaWorld).Count);
    }

    private static void Jump(
        BoxelSurveyStatsState state,
        string starSystem,
        long address,
        string timestamp = "2026-07-10T12:00:00Z")
    {
        Assert.True(state.Apply(Parse(
            $$"""
            {"timestamp":"{{timestamp}}","event":"FSDJump","StarSystem":"{{starSystem}}","SystemAddress":{{address}},"StarPos":[1,2,3]}
            """)));
    }

    private static void ScanPlanet(
        BoxelSurveyStatsState state,
        long address,
        int bodyId,
        string planetClass,
        double mass = 1,
        bool terraformable = false)
    {
        var tf = terraformable ? "Terraformable" : "";
        Assert.True(state.Apply(Parse(
            $$"""
            {"event":"Scan","SystemAddress":{{address}},"BodyID":{{bodyId}},"PlanetClass":"{{planetClass}}","MassEM":{{mass.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"TerraformState":"{{tf}}","WasDiscovered":false,"WasMapped":false}
            """)));
    }

    private static SystemScanSnapshot Snapshot(
        string systemName,
        long address,
        IReadOnlyList<SystemScanBodySnapshot> bodies,
        int expectedBodyCount = 0,
        bool allBodiesFound = false,
        int fssBodyCount = 0)
    {
        return new SystemScanSnapshot(
            systemName,
            address,
            null,
            0,
            expectedBodyCount,
            expectedBodyCount > 0,
            allBodiesFound,
            fssBodyCount,
            bodies.Count,
            bodies.Count(body => body.IsDssComplete),
            bodies.Sum(body => (long)body.CurrentScanValue),
            0,
            0,
            null,
            null,
            bodies);
    }

    private static SystemScanBodySnapshot PlanetBody(
        int bodyId,
        string planetClass,
        bool terraformable = false,
        bool landable = false,
        double mass = 1,
        double? helium = null,
        int scanValue = 0,
        int mappedValue = 0,
        int currentValue = 0,
        bool dssComplete = false)
    {
        var composition = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (helium is not null)
        {
            composition["Helium"] = helium.Value;
        }

        return new SystemScanBodySnapshot(
            bodyId,
            $"Body {bodyId}",
            $"{bodyId}",
            SystemBodyKind.Planet,
            null,
            planetClass,
            landable,
            terraformable,
            true,
            dssComplete,
            false,
            false,
            null,
            false,
            false,
            null,
            mass,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            null,
            helium is null ? "None" : "Helium",
            null,
            0,
            0,
            0,
            0,
            scanValue,
            mappedValue,
            currentValue,
            0,
            composition,
            new Dictionary<string, double>(),
            [],
            [],
            [],
            []);
    }

    private static string Prefix(string generatedName)
    {
        Assert.True(BoxelAddress.TryParse(generatedName, out var boxel));
        return boxel!.Prefix;
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var journalEvent, out var error),
            error);
        return Assert.IsType<JournalEventEnvelope>(journalEvent);
    }
}
