using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Exploration;

public sealed class SystemScanStateTests
{
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
            """{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":2310101,"Name_Localised":"Aleoida Arcus - Green","SubCategory":"$Codex_SubCategory_Organic_Structures;","IsNewEntry":true}"""));
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
        Assert.True(organism.IsAnalyzed);
        Assert.True(organism.IsRegionalFirst);
        Assert.Equal(1, body.AnalyzedBiologicalSignalCount);
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
    public void UnknownEventsRemainAvailableToOtherReducers()
    {
        var state = new SystemScanState();

        Assert.False(state.Apply(Parse("""{"event":"FutureEvent"}""")));
        Assert.Equal(SystemScanSnapshot.Empty, state.CreateSnapshot());
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
