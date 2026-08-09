using System.Text.Json.Nodes;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Diagnostics;

public sealed class LegacySystemSnapshotMergerTests
{
    [Fact]
    public void MergePreservesUnknownDataAndAddsReconstructedExplorationState()
    {
        var state = new SystemScanState();
        state.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[1,2,3]}"""));
        state.Apply(Parse(
            """{"event":"FSSDiscoveryScan","SystemAddress":42,"BodyCount":2}"""));
        state.Apply(Parse(
            """{"event":"Scan","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"PlanetClass":"Rocky body","Landable":true,"Radius":1000,"AtmosphereComposition":[{"Name":"CarbonDioxide","Percent":100}],"Parents":[{"Star":0}]}"""));
        state.Apply(Parse(
            """{"event":"SAASignalsFound","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}"""));
        state.Apply(Parse(
            """{"event":"ScanOrganic","ScanType":"Analyse","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Species":"$Codex_Ent_Aleoids_01_Name;","Species_Localised":"Aleoida Arcus"}"""));
        var existing = JsonNode.Parse(
            """
            {
              "name": "Test",
              "address": 42,
              "firstVisited": "2026-07-20T00:00:00Z",
              "lastVisited": "2026-07-21T00:00:00Z",
              "futureRoot": { "value": 7 },
              "bodies": [
                {
                  "name": "Test 1",
                  "id": 1,
                  "futureBody": true,
                  "organisms": [
                    {
                      "genus": "$Codex_Ent_Aleoids_Genus_Name;",
                      "futureOrganism": "keep"
                    }
                  ]
                }
              ]
            }
            """)!.AsObject();

        var merged = LegacySystemSnapshotMerger.Merge(
            existing,
            state.CreateSnapshot(),
            "Drew",
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-22T00:00:00Z"));

        Assert.NotSame(existing, merged);
        Assert.Equal(7, merged["futureRoot"]!["value"]!.GetValue<int>());
        Assert.Equal("Drew", merged["commander"]!.GetValue<string>());
        Assert.Equal(
            "2026-07-19T00:00:00.0000000+00:00",
            merged["firstVisited"]!.GetValue<string>());
        Assert.Equal(
            "2026-07-22T00:00:00.0000000+00:00",
            merged["lastVisited"]!.GetValue<string>());
        Assert.True(merged["honked"]!.GetValue<bool>());
        Assert.Equal(2, merged["bodyCount"]!.GetValue<int>());
        var body = Assert.IsType<JsonObject>(
            Assert.Single(merged["bodies"]!.AsArray()));
        Assert.True(body["futureBody"]!.GetValue<bool>());
        Assert.Equal("LandableBody", body["type"]!.GetValue<string>());
        Assert.Equal(1, body["bioSignalCount"]!.GetValue<int>());
        Assert.Equal(
            100,
            body["atmosphereComposition"]!["CarbonDioxide"]!.GetValue<double>());
        var parent = Assert.IsType<JsonObject>(
            Assert.Single(body["parents"]!.AsArray()));
        Assert.Equal("Star", parent["type"]!.GetValue<string>());
        Assert.Equal(0, parent["id"]!.GetValue<int>());
        var organism = Assert.IsType<JsonObject>(
            Assert.Single(body["organisms"]!.AsArray()));
        Assert.Equal("keep", organism["futureOrganism"]!.GetValue<string>());
        Assert.Equal(
            "Aleoida Arcus",
            organism["speciesLocalized"]!.GetValue<string>());
        Assert.True(organism["analyzed"]!.GetValue<bool>());
        Assert.Null(existing["commander"]);
        Assert.Null(existing["bodies"]![0]!["type"]);
    }

    [Fact]
    public void MergeRejectsMalformedKnownCollectionsWithoutOverwritingThem()
    {
        var state = new SystemScanState();
        state.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42}"""));
        var existing = JsonNode.Parse(
            """{"name":"Test","address":42,"bodies":{"future":true}}""")!
            .AsObject();

        Assert.Throws<InvalidDataException>(() =>
            LegacySystemSnapshotMerger.Merge(
                existing,
                state.CreateSnapshot(),
                "Drew",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));
        Assert.True(existing["bodies"]!["future"]!.GetValue<bool>());
    }

    [Fact]
    public void MergeKeepsMultipleSpeciesFromTheSameGenus()
    {
        var state = new SystemScanState();
        state.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42}"""));
        state.Apply(Parse(
            """{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2}],"Genuses":[{"Genus":"$Genus_BrainTree;"}]}"""));
        state.Apply(Parse(
            """{"event":"ScanOrganic","ScanType":"Analyse","SystemAddress":42,"Body":1,"Genus":"$Genus_BrainTree;","Species":"$Species_BrainTree_A;","Variant":"$Variant_BrainTree_A;"}"""));
        state.Apply(Parse(
            """{"event":"ScanOrganic","ScanType":"Analyse","SystemAddress":42,"Body":1,"Genus":"$Genus_BrainTree;","Species":"$Species_BrainTree_B;","Variant":"$Variant_BrainTree_B;"}"""));

        var merged = LegacySystemSnapshotMerger.Merge(
            JsonNode.Parse(
                """{"name":"Test","address":42,"bodies":[]}""")!.AsObject(),
            state.CreateSnapshot(),
            "Drew",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var organisms = Assert.Single(merged["bodies"]!.AsArray())!["organisms"]!
            .AsArray();
        Assert.Equal(2, organisms.Count);
        Assert.Equal(
            ["$Variant_BrainTree_A;", "$Variant_BrainTree_B;"],
            organisms.Select(organism =>
                organism!["variant"]!.GetValue<string>()));
    }

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
