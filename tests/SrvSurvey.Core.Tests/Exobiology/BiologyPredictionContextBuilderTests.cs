using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Exobiology;

public sealed class BiologyPredictionContextBuilderTests
{
    [Fact]
    public void BuildsLegacyUnitsEnvironmentAndKnowledgeFromJournalState()
    {
        var state = new SystemScanState();
        var position = new GalacticCoordinate(
            1099.21875,
            -146.6875,
            -133.59375);
        state.Apply(Parse(
            $$"""{"event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[{{position.X}},{{position.Y}},{{position.Z}}]}"""));
        state.Apply(Parse(
            """{"event":"Scan","SystemAddress":42,"BodyName":"Test A","BodyID":0,"StarType":"L","StellarMass":1,"Radius":695700000,"SurfaceTemperature":5000}"""));
        state.Apply(Parse(PlanetScan));
        state.Apply(Parse(
            """{"event":"FSSBodySignals","SystemAddress":42,"BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}"""));
        state.Apply(Parse(
            """{"event":"ScanOrganic","ScanType":"Log","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida","Species":"$Codex_Ent_Aleoids_02_Name;","Species_Localised":"Aleoida Coronamus"}"""));

        var snapshot = state.CreateSnapshot();
        var nebulaCatalog = new NebulaCatalog(
        [
            new GalacticCoordinate(
                position.X + 42,
                position.Y,
                position.Z),
        ]);
        var inputs = BiologyPredictionContextBuilder.Build(
            snapshot,
            bodyId: 1,
            nebulaCatalog);

        Assert.NotNull(inputs);
        Assert.Equal("Rocky body", inputs.Context.PlanetClass);
        Assert.Equal(0.2, inputs.Context.SurfaceGravity);
        Assert.Equal(0.03, inputs.Context.SurfacePressure);
        Assert.Equal("thin carbon dioxide", inputs.Context.Atmosphere);
        Assert.Equal("None", inputs.Context.Volcanism);
        Assert.Equal(["L"], inputs.Context.StarTypes);
        Assert.Equal(["L"], inputs.Context.ParentStarTypes);
        Assert.Equal("L", inputs.Context.PrimaryStarType);
        Assert.Equal(42, inputs.Context.NebulaDistanceLy);
        Assert.Equal(GalacticRegionMap.Find(position)?.Id, inputs.Context.RegionId);
        Assert.True(inputs.Context.IsWithinGuardianBubble);
        Assert.True(inputs.Knowledge.AllGeneraKnown);
        Assert.Equal(["Aleoida"], inputs.Knowledge.KnownGenera);
        Assert.Equal(
            "Aleoida Coronamus",
            inputs.Knowledge.KnownSpeciesByGenus["Aleoida"]);

        var prediction = new BiologyPredictionEvaluator(
            BiologyCriteriaCatalog.LoadEmbedded())
            .Evaluate(inputs.Context);
        Assert.Contains("Aleoida Coronamus - Lime", prediction.Predictions);
        Assert.True(prediction.HasCompleteContext);
    }

    [Fact]
    public void ChoosesBrightestStarAcrossBarycentreSiblings()
    {
        var state = new SystemScanState();
        state.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[0,0,0]}"""));
        state.Apply(Parse(
            """{"event":"ScanBaryCentre","SystemAddress":42,"BodyID":2}"""));
        state.Apply(Parse(
            """{"event":"Scan","SystemAddress":42,"BodyName":"Test A","BodyID":0,"StarType":"DA","Radius":10,"SurfaceTemperature":1000,"SemiMajorAxis":100,"Parents":[{"Null":2}]}"""));
        state.Apply(Parse(
            """{"event":"Scan","SystemAddress":42,"BodyName":"Test B","BodyID":1,"StarType":"M_RedGiant","Radius":1,"SurfaceTemperature":100,"SemiMajorAxis":1,"Parents":[{"Null":2}]}"""));
        state.Apply(Parse(
            """{"event":"Scan","SystemAddress":42,"BodyName":"Test 3","BodyID":3,"PlanetClass":"Rocky body","Landable":true,"SemiMajorAxis":10,"Parents":[{"Null":2}]}"""));

        var inputs = BiologyPredictionContextBuilder.Build(
            state.CreateSnapshot(),
            bodyId: 3);

        Assert.NotNull(inputs);
        Assert.Equal(["D"], inputs.Context.StarTypes);
        Assert.Equal(["D", "M"], inputs.Context.ParentStarTypes);
        Assert.Equal("D", inputs.Context.PrimaryStarType);
        Assert.True(inputs.Context.NebulaDistanceLy > 0);
    }

    [Theory]
    [InlineData("DAB", "D")]
    [InlineData("WN", "W")]
    [InlineData("CHd", "C")]
    [InlineData("M_RedGiant", "M")]
    [InlineData("TTS", "TTS")]
    public void FlattensLegacyStarFamilies(string starType, string expected)
    {
        Assert.Equal(
            expected,
            BiologyPredictionContextBuilder.FlattenStarType(starType));
    }

    [Fact]
    public void RejectsBodiesWithoutLandabilityOrParentData()
    {
        var state = new SystemScanState();
        state.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42}"""));
        state.Apply(Parse(
            """{"event":"Scan","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"PlanetClass":"Rocky body"}"""));

        Assert.Null(BiologyPredictionContextBuilder.Build(
            state.CreateSnapshot(),
            bodyId: 1));
    }

    private const string PlanetScan = """
        {
          "event":"Scan",
          "SystemAddress":42,
          "BodyName":"Test 1",
          "BodyID":1,
          "Parents":[{"Star":0}],
          "PlanetClass":"Rocky body",
          "Atmosphere":"thin carbon dioxide atmosphere",
          "AtmosphereType":"CarbonDioxide",
          "AtmosphereComposition":[{"Name":"CarbonDioxide","Percent":100}],
          "Volcanism":"",
          "SurfaceGravity":2,
          "SurfaceTemperature":185,
          "SurfacePressure":3000,
          "DistanceFromArrivalLS":500,
          "SemiMajorAxis":100000,
          "Landable":true,
          "Materials":[{"Name":"Iron","Percent":20}]
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
