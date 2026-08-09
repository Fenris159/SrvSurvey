using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Exploration;

public sealed class SystemScanStateTests
{
    [Fact]
    public void ExplicitFirstFootfallCorrectionRequiresAndUpdatesCurrentBody()
    {
        var state = new SystemScanState();
        Assert.False(state.SetCurrentBodyFirstFootfall(true));
        state.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42}"""));
        state.Apply(Parse(
            """{"event":"Disembark","SystemAddress":42,"Body":"Test 1","BodyID":1,"OnPlanet":true,"OnStation":false}"""));

        Assert.True(state.SetCurrentBodyFirstFootfall(true));

        Assert.True(Assert.Single(state.CreateSnapshot().Bodies).IsFirstFootfall);
    }

    [Fact]
    public void ApplyBuildsReusableSystemAndBodyScanState()
    {
        var state = new SystemScanState();

        state.Apply(Parse("""{"event":"Fileheader","Odyssey":true}"""));
        state.Apply(Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42,"Population":0,"StarPos":[1,2,3]}"""));
        state.Apply(Parse("""{"event":"FSSDiscoveryScan","SystemName":"Test","SystemAddress":42,"BodyCount":2,"NonBodyCount":4}"""));
        state.Apply(Parse("""{"event":"FSSSignalDiscovered","SystemAddress":42,"SignalName":"Port","SignalType":"Outpost"}"""));
        state.Apply(Parse("""{"event":"FSSSignalDiscovered","SystemAddress":42,"SignalName":"Beacon","SignalType":"NavBeacon"}"""));
        state.Apply(Parse("""{"event":"FSSSignalDiscovered","SystemAddress":42,"SignalName":"Cloud","SignalType":"Codex"}"""));
        state.Apply(Parse("""{"event":"Scan","ScanType":"Detailed","StarSystem":"Test","SystemAddress":42,"BodyName":"Test A","BodyID":0,"DistanceFromArrivalLS":0,"StarType":"K","StellarMass":1,"WasDiscovered":true,"WasMapped":false}"""));
        state.Apply(Parse(PlanetScan));
        state.Apply(Parse("""{"event":"SAASignalsFound","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2},{"Type":"$SAA_SignalType_Geological;","Count":1}],"Genuses":[{"Genus":"$Genus_A;"},{"Genus":"$Genus_B;"}]}"""));
        state.Apply(Parse("""{"event":"ScanOrganic","ScanType":"Analyse","SystemAddress":42,"Body":1,"Genus":"$Genus_A;"}"""));
        state.Apply(Parse("""{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":100,"Name_Localised":"Fumarole","SubCategory":"$Codex_SubCategory_Geology_and_Anomalies;"}"""));
        state.Apply(Parse("""{"event":"SAAScanComplete","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"ProbesUsed":4,"EfficiencyTarget":6}"""));
        state.Apply(Parse("""{"event":"Disembark","SystemAddress":42,"Body":"Test 1","BodyID":1,"OnPlanet":true,"OnStation":false}"""));
        state.Apply(Parse("""{"event":"FSSAllBodiesFound","SystemName":"Test","SystemAddress":42,"Count":2}"""));

        var snapshot = state.CreateSnapshot();
        Assert.Equal("Test", snapshot.SystemName);
        Assert.Equal(42, snapshot.SystemAddress);
        Assert.Equal(new GalacticCoordinate(1, 2, 3), snapshot.StarPosition);
        Assert.Equal(2, snapshot.ExpectedBodyCount);
        Assert.True(snapshot.HasDiscoveryScan);
        Assert.True(snapshot.AllBodiesFound);
        Assert.True(snapshot.IsFssComplete);
        Assert.Equal(2, snapshot.FssBodyCount);
        Assert.Equal(2, snapshot.ScannedBodyCount);
        Assert.Equal(1, snapshot.DssCompletedBodyCount);
        Assert.Equal(2, snapshot.NonBodySignalCount);
        Assert.Equal(1, snapshot.LastDetailedBodyId);
        Assert.Equal(1, snapshot.CurrentBodyId);
        Assert.Equal(1, snapshot.BiologicalSignalsRemaining);

        var planet = Assert.Single(snapshot.Bodies, body => body.BodyId == 1);
        Assert.Equal(SystemBodyKind.LandablePlanet, planet.Kind);
        Assert.True(planet.IsTerraformable);
        Assert.True(planet.IsDssComplete);
        Assert.True(planet.IsFirstFootfall);
        Assert.Equal(2, planet.BiologicalSignalCount);
        Assert.Equal(1, planet.AnalyzedBiologicalSignalCount);
        Assert.Equal(2, planet.Organisms.Count);
        Assert.True(Assert.Single(
            planet.Organisms,
            organism => organism.Genus == "$Genus_A;").IsAnalyzed);
        Assert.Equal(1, planet.GeologicalSignalCount);
        Assert.Equal(1, planet.AnalyzedGeologicalSignalCount);
        Assert.Equal("Fumarole", Assert.Single(
            planet.AnalyzedGeologicalSignals));
        Assert.Equal(2, planet.AtmosphereComposition.Count);
        Assert.Equal(2, planet.Materials.Count);
        Assert.Single(planet.Rings);
        Assert.Equal(
            new SystemBodyParentSnapshot(SystemBodyParentKind.Planet, 0),
            Assert.Single(planet.Parents));
        Assert.True(planet.EstimatedMappedValue > planet.ScanValue);
        Assert.Equal(planet.EstimatedMappedValue, planet.CurrentScanValue);
        Assert.Equal(
            snapshot.Bodies.Sum(body => (long)body.CurrentScanValue),
            snapshot.CurrentScanValue);
    }

    [Fact]
    public void OrganicEventsRetainCodexIdentityRewardAndDiscoveryState()
    {
        var state = new SystemScanState();
        state.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42}"""));
        state.Apply(Parse(
            """{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}"""));
        state.Apply(Parse(
            """{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":2310101,"Name_Localised":"Aleoida Arcus - Green","SubCategory":"$Codex_SubCategory_Organic_Structures;","Latitude":1,"Longitude":2,"IsNewEntry":true}"""));

        var reported = Assert.Single(
            Assert.Single(state.CreateSnapshot().Bodies).Organisms);
        Assert.False(reported.IsScanned);
        Assert.False(reported.IsAnalyzed);

        state.Apply(Parse(
            """{"event":"ScanOrganic","ScanType":"Analyse","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida","Species":"$Codex_Ent_Aleoids_01_Name;","Species_Localised":"Aleoida Arcus","Variant":"$Codex_Ent_Aleoids_01_B_Name;","Variant_Localised":"Aleoida Arcus - Green"}"""));

        var body = Assert.Single(state.CreateSnapshot().Bodies);
        var organism = Assert.Single(body.Organisms);
        Assert.Equal("$Codex_Ent_Aleoids_Genus_Name;", organism.Genus);
        Assert.Equal("Aleoida", organism.GenusLocalized);
        Assert.Equal("$Codex_Ent_Aleoids_01_Name;", organism.Species);
        Assert.Equal("Aleoida Arcus", organism.SpeciesLocalized);
        Assert.Equal("$Codex_Ent_Aleoids_01_B_Name;", organism.Variant);
        Assert.Equal("Aleoida Arcus - Green", organism.VariantLocalized);
        Assert.Equal(2310101, organism.EntryId);
        Assert.Equal(7_252_500, organism.Reward);
        Assert.True(organism.IsScanned);
        Assert.True(organism.IsAnalyzed);
        Assert.True(organism.IsRegionalFirst);
        Assert.Equal(1, body.AnalyzedBiologicalSignalCount);
    }

    [Fact]
    public void OrganicEventsKeepMultipleSpeciesFromTheSameGenus()
    {
        var state = new SystemScanState();
        state.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42}"""));
        state.Apply(Parse(
            """{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2}],"Genuses":[{"Genus":"$Codex_Ent_BrainTree_Genus_Name;","Genus_Localised":"Brain Tree"}]}"""));
        state.Apply(Parse(
            """{"event":"ScanOrganic","ScanType":"Analyse","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_BrainTree_Genus_Name;","Species":"$Codex_Ent_BrainTree_01_Name;","Variant":"$Codex_Ent_BrainTree_01_A_Name;"}"""));
        state.Apply(Parse(
            """{"event":"ScanOrganic","ScanType":"Analyse","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_BrainTree_Genus_Name;","Species":"$Codex_Ent_BrainTree_02_Name;","Variant":"$Codex_Ent_BrainTree_02_A_Name;"}"""));

        var body = Assert.Single(state.CreateSnapshot().Bodies);
        Assert.Equal(2, body.Organisms.Count);
        Assert.Equal(2, body.AnalyzedBiologicalSignalCount);
        Assert.Equal(
            [
                "$Codex_Ent_BrainTree_01_A_Name;",
                "$Codex_Ent_BrainTree_02_A_Name;",
            ],
            body.Organisms.Select(organism => organism.Variant));
    }

    [Fact]
    public void LegacyCodexEntriesUseCanonicalGenusAndKeepDistinctVariants()
    {
        var state = new SystemScanState();
        state.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42}"""));
        state.Apply(Parse(
            """{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2}],"Genuses":[{"Genus":"$Codex_Ent_Brancae_Name;","Genus_Localised":"Brain Tree"}]}"""));
        state.Apply(Parse(
            """{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":2100201,"Name":"$Codex_Ent_Seed_Name;","Name_Localised":"Roseum Brain Tree","SubCategory":"$Codex_SubCategory_Organic_Structures;","Latitude":1,"Longitude":2}"""));
        state.Apply(Parse(
            """{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":2100202,"Name":"$Codex_Ent_SeedABCD_01_Name;","Name_Localised":"Gypseeum Brain Tree","SubCategory":"$Codex_SubCategory_Organic_Structures;","Latitude":1.1,"Longitude":2.1}"""));

        var body = Assert.Single(state.CreateSnapshot().Bodies);
        Assert.Equal(2, body.Organisms.Count);
        Assert.All(body.Organisms, organism =>
        {
            Assert.Equal("$Codex_Ent_Brancae_Name;", organism.Genus);
            Assert.Equal("Brain Tree", organism.GenusLocalized);
        });
        Assert.Equal(
            [2100201L, 2100202L],
            body.Organisms.Select(organism => organism.EntryId));
    }

    [Fact]
    public void ExactEntryIdentityCorrectsMismatchedGenusWithoutDuplication()
    {
        var state = new SystemScanState();
        state.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42}"""));
        state.Apply(Parse(
            """{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}]}"""));
        state.Apply(Parse(
            """{"event":"ScanOrganic","ScanType":"Log","SystemAddress":42,"Body":1,"Genus":"$Wrong_Genus;","Species":"$Codex_Ent_Seed_Name;","Variant":"$Codex_Ent_Seed_Name;"}"""));
        state.Apply(Parse(
            """{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":2100201,"Name_Localised":"Roseum Brain Tree","SubCategory":"$Codex_SubCategory_Organic_Structures;","Latitude":1,"Longitude":2}"""));

        var organism = Assert.Single(
            Assert.Single(state.CreateSnapshot().Bodies).Organisms);
        Assert.Equal("$Codex_Ent_Brancae_Name;", organism.Genus);
        Assert.Equal(2100201, organism.EntryId);
    }

    [Fact]
    public void OrganicCodexEntriesRequireSurfaceCoordinatesAndExcludeFixedLife()
    {
        var state = new SystemScanState();
        state.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42}"""));
        state.Apply(Parse(
            """{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}]}"""));
        state.Apply(Parse(
            """{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":2310101,"SubCategory":"$Codex_SubCategory_Organic_Structures;"}"""));
        state.Apply(Parse(
            """{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":2310101,"SubCategory":"$Codex_SubCategory_Organic_Structures;","Latitude":0,"Longitude":2}"""));
        state.Apply(Parse(
            """{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":2310101,"SubCategory":"$Codex_SubCategory_Organic_Structures;","Latitude":1,"Longitude":2,"NearestDestination":"$Fixed_Event_Life_Cloud;"}"""));
        Assert.Empty(Assert.Single(state.CreateSnapshot().Bodies).Organisms);

        state.Apply(Parse(
            """{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":2310101,"SubCategory":"$Codex_SubCategory_Organic_Structures;","Latitude":1,"Longitude":2}"""));
        Assert.Single(Assert.Single(state.CreateSnapshot().Bodies).Organisms);
    }

    [Fact]
    public void NewSystemClearsBodiesAndIgnoresLateEventsFromPriorSystem()
    {
        var state = new SystemScanState();
        state.Apply(Parse("""{"event":"Location","StarSystem":"First","SystemAddress":1}"""));
        state.Apply(Parse("""{"event":"Scan","SystemAddress":1,"BodyName":"First A","BodyID":0,"StarType":"G","StellarMass":1}"""));

        state.Apply(Parse("""{"event":"FSDJump","StarSystem":"Second","SystemAddress":2,"StarPos":[4,5,6]}"""));
        state.Apply(Parse("""{"event":"FSSBodySignals","SystemAddress":1,"BodyName":"First 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":3}]}"""));

        var snapshot = state.CreateSnapshot();
        Assert.Equal("Second", snapshot.SystemName);
        Assert.Equal(2, snapshot.SystemAddress);
        Assert.Equal(new GalacticCoordinate(4, 5, 6), snapshot.StarPosition);
        Assert.Empty(snapshot.Bodies);
    }

    [Theory]
    [InlineData("Died")]
    [InlineData("Resurrect")]
    public void DeathLifecycleClearsCurrentBodyButRetainsSystemSurvey(
        string eventName)
    {
        var state = new SystemScanState();
        state.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42,"Body":"Test 1","BodyID":1,"BodyType":"Planet"}"""));
        state.Apply(Parse(PlanetScan));

        Assert.True(state.Apply(Parse(
            $$"""{"event":"{{eventName}}"}""")));

        var snapshot = state.CreateSnapshot();
        Assert.Equal(42, snapshot.SystemAddress);
        Assert.Null(snapshot.CurrentBodyId);
        Assert.Single(snapshot.Bodies);
    }

    [Fact]
    public void HyperspaceDepartureClearsCurrentBodyButRetainsSystemSurvey()
    {
        var state = new SystemScanState();
        state.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42,"Body":"Test 1","BodyID":1,"BodyType":"Planet"}"""));
        state.Apply(Parse(PlanetScan));

        Assert.True(state.Apply(Parse(
            """{"event":"StartJump","JumpType":"Hyperspace"}""")));

        var snapshot = state.CreateSnapshot();
        Assert.Equal(42, snapshot.SystemAddress);
        Assert.Null(snapshot.CurrentBodyId);
        Assert.Single(snapshot.Bodies);
    }

    [Fact]
    public void SupercruiseDepartureRetainsCurrentBodyIdentity()
    {
        var state = new SystemScanState();
        state.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42,"Body":"Test 1","BodyID":1,"BodyType":"Planet"}"""));

        Assert.False(state.Apply(Parse(
            """{"event":"StartJump","JumpType":"Supercruise"}""")));

        Assert.Equal(1, state.CurrentBodyId);
    }

    [Fact]
    public void DssCompletionSetsScannedBodyAsCurrent()
    {
        var state = new SystemScanState();
        state.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42}"""));
        state.Apply(Parse(PlanetScan));

        Assert.True(state.Apply(Parse(
            """{"event":"SAAScanComplete","SystemAddress":42,"BodyName":"Test 1","BodyID":1}""")));

        Assert.Equal(1, state.CurrentBodyId);
    }

    [Fact]
    public void FssCountExcludesAsteroidsRingsAndBarycentres()
    {
        var state = new SystemScanState();
        state.Apply(Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""));
        state.Apply(Parse("""{"event":"FSSDiscoveryScan","SystemAddress":42,"BodyCount":2}"""));
        state.Apply(Parse("""{"event":"Scan","SystemAddress":42,"BodyName":"Test A","BodyID":0,"StarType":"K","StellarMass":1}"""));
        state.Apply(Parse("""{"event":"Scan","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"PlanetClass":"Rocky body","MassEM":1}"""));
        state.Apply(Parse("""{"event":"Scan","SystemAddress":42,"BodyName":"Test A Belt Cluster 1","BodyID":2}"""));
        state.Apply(Parse("""{"event":"Scan","SystemAddress":42,"BodyName":"Test 1 A Ring","BodyID":3}"""));
        state.Apply(Parse("""{"event":"ScanBaryCentre","StarSystem":"Test","SystemAddress":42,"BodyID":4}"""));

        var snapshot = state.CreateSnapshot();
        Assert.Equal(2, snapshot.FssBodyCount);
        Assert.True(snapshot.IsFssComplete);
        Assert.Equal(5, snapshot.ScannedBodyCount);
    }

    [Fact]
    public void LastDetailedBodyRemainsTheLatestStandalonePlanet()
    {
        var state = new SystemScanState();
        state.Apply(Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""));
        state.Apply(Parse("""{"event":"Scan","ScanType":"Detailed","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"PlanetClass":"Rocky body","MassEM":1}"""));
        state.Apply(Parse("""{"event":"Scan","ScanType":"Detailed","SystemAddress":42,"BodyName":"Test A","BodyID":0,"StarType":"K","StellarMass":1}"""));
        state.Apply(Parse("""{"event":"Scan","ScanType":"Detailed","SystemAddress":42,"BodyName":"Test A Belt Cluster 1","BodyID":2}"""));
        state.Apply(Parse("""{"event":"Scan","ScanType":"Detailed","SystemAddress":42,"BodyName":"Test 1 A Ring","BodyID":3,"PlanetClass":"Rocky body","MassEM":0.1,"Parents":[{"Ring":1}]}"""));

        Assert.Equal(1, state.CreateSnapshot().LastDetailedBodyId);
    }

    [Fact]
    public void ScanRetainsOrderedParentChainForStellarCalculations()
    {
        var state = new SystemScanState();
        state.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42}"""));
        state.Apply(Parse(
            """{"event":"ScanBaryCentre","SystemAddress":42,"BodyID":3,"Parents":[{"Null":1}]}"""));
        state.Apply(Parse(
            """{"event":"Scan","SystemAddress":42,"BodyName":"Test 4","BodyID":4,"PlanetClass":"Rocky body","Parents":[{"Ring":8},{"Planet":7},{"Null":3},{"Star":0}]}"""));

        var snapshot = state.CreateSnapshot();
        var barycentre = Assert.Single(
            snapshot.Bodies,
            body => body.BodyId == 3);
        var planet = Assert.Single(
            snapshot.Bodies,
            body => body.BodyId == 4);

        Assert.Equal(
            [new SystemBodyParentSnapshot(SystemBodyParentKind.Null, 1)],
            barycentre.Parents);
        Assert.Equal(
            [
                new SystemBodyParentSnapshot(SystemBodyParentKind.Ring, 8),
                new SystemBodyParentSnapshot(SystemBodyParentKind.Planet, 7),
                new SystemBodyParentSnapshot(SystemBodyParentKind.Null, 3),
                new SystemBodyParentSnapshot(SystemBodyParentKind.Star, 0),
            ],
            planet.Parents);
        Assert.True(planet.HasRingParent);
    }

    [Fact]
    public void UnknownEventsRemainAvailableToOtherReducers()
    {
        var state = new SystemScanState();

        Assert.False(state.Apply(Parse("""{"event":"FutureEvent"}""")));
        Assert.Equal(SystemScanSnapshot.Empty, state.CreateSnapshot());
    }

    [Fact]
    public void KnownSystemHistoryFillsMissingFieldsWithoutReplacingLiveScans()
    {
        var live = new SystemScanState();
        live.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42}"""));
        live.Apply(Parse(
            """
            {
              "event":"Scan",
              "SystemAddress":42,
              "BodyName":"Test 1",
              "BodyID":1,
              "PlanetClass":"Rocky body",
              "Landable":true,
              "WasDiscovered":false,
              "SurfaceGravity":20,
              "Rings":[{
                "Name":"Test 1 A Ring",
                "RingClass":"eRingClass_Rocky",
                "InnerRad":0,
                "OuterRad":0
              }]
            }
            """));
        var knownState = new SystemScanState();
        knownState.Apply(Parse(
            """
            {
              "event":"Location",
              "StarSystem":"Test",
              "SystemAddress":42,
              "StarPos":[1,2,3]
            }
            """));
        knownState.Apply(Parse(
            """
            {
              "event":"Scan",
              "SystemAddress":42,
              "BodyName":"Test 1",
              "BodyID":1,
              "PlanetClass":"Icy body",
              "Landable":true,
              "WasDiscovered":true,
              "SurfaceGravity":9,
              "SurfaceTemperature":180,
              "AtmosphereType":"Argon",
              "Materials":[{"Name":"iron","Percent":20}],
              "Rings":[{
                "Name":"Test 1 A Ring",
                "RingClass":"eRingClass_Rocky",
                "InnerRad":10,
                "OuterRad":20
              }]
            }
            """));
        knownState.Apply(Parse(
            """
            {
              "event":"FSSBodySignals",
              "SystemAddress":42,
              "BodyName":"Test 1",
              "BodyID":1,
              "Signals":[
                {"Type":"$SAA_SignalType_Biological;","Count":1}
              ],
              "Genuses":[
                {
                  "Genus":"$Codex_Ent_Aleoids_Genus_Name;",
                  "Genus_Localised":"Aleoida"
                }
              ]
            }
            """));

        var changed = live.MergeKnownData(knownState.CreateSnapshot());

        Assert.True(changed);
        var snapshot = live.CreateSnapshot();
        Assert.Equal(new GalacticCoordinate(1, 2, 3), snapshot.StarPosition);
        var body = Assert.Single(snapshot.Bodies);
        Assert.Equal("Rocky body", body.PlanetClass);
        Assert.False(body.WasDiscovered);
        Assert.Equal(20, body.SurfaceGravity);
        Assert.Equal(180, body.SurfaceTemperature);
        Assert.Equal("Argon", body.AtmosphereType);
        Assert.Equal(20, body.Materials["iron"]);
        Assert.Equal(10, Assert.Single(body.Rings).InnerRadius);
        Assert.Equal(20, Assert.Single(body.Rings).OuterRadius);
        Assert.Equal(1, body.BiologicalSignalCount);
        Assert.Equal("Aleoida", Assert.Single(body.Organisms).GenusLocalized);

        var other = new SystemScanState();
        other.Apply(Parse(
            """{"event":"Location","StarSystem":"Other","SystemAddress":99}"""));
        Assert.False(live.MergeKnownData(other.CreateSnapshot()));
    }

    [Fact]
    public void ExternalBiologyConsentOnlyControlsGenusConfirmations()
    {
        var live = new SystemScanState();
        live.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42}"""));
        var known = new SystemScanState();
        known.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42}"""));
        known.Apply(Parse(
            """{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}"""));

        Assert.True(live.MergeKnownData(
            known.CreateSnapshot(),
            includeBiologicalData: false));
        var signalsOnly = Assert.Single(live.CreateSnapshot().Bodies);
        Assert.Equal(2, signalsOnly.BiologicalSignalCount);
        Assert.Empty(signalsOnly.Organisms);

        Assert.True(live.MergeKnownData(
            known.CreateSnapshot(),
            includeBiologicalData: true));
        Assert.Equal(
            "Aleoida",
            Assert.Single(Assert.Single(live.CreateSnapshot().Bodies).Organisms)
                .GenusLocalized);
    }

    private const string PlanetScan = """
        {
          "event":"Scan",
          "ScanType":"Detailed",
          "StarSystem":"Test",
          "SystemAddress":42,
          "BodyName":"Test 1",
          "BodyID":1,
          "Parents":[{"Planet":0}],
          "DistanceFromArrivalLS":123.4,
          "TidalLock":true,
          "TerraformState":"Terraformable",
          "PlanetClass":"High metal content body",
          "Atmosphere":"thin carbon dioxide atmosphere",
          "AtmosphereType":"CarbonDioxide",
          "AtmosphereComposition":[
            {"Name":"CarbonDioxide","Percent":99.0},
            {"Name":"SulphurDioxide","Percent":1.0}
          ],
          "Volcanism":"minor silicate vapour geysers volcanism",
          "MassEM":1.2,
          "Radius":6000000,
          "SurfaceGravity":12.0,
          "SurfaceTemperature":300,
          "SurfacePressure":1000,
          "Landable":true,
          "Materials":[
            {"Name":"iron","Percent":20.0},
            {"Name":"yttrium","Percent":1.0}
          ],
          "Rings":[
            {"Name":"Test 1 A Ring","RingClass":"eRingClass_Rocky","InnerRad":1,"OuterRad":2}
          ],
          "SemiMajorAxis":12345,
          "WasDiscovered":false,
          "WasMapped":false,
          "WasFootfalled":false
        }
        """;

    private static JournalEventEnvelope Parse(string json)
    {
        var success = JournalEventEnvelope.TryParse(
            json,
            out var journalEvent,
            out var error);
        Assert.True(success, error);
        return Assert.IsType<JournalEventEnvelope>(journalEvent);
    }
}
