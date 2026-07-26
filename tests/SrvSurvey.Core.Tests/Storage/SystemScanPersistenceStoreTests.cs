using System.Text.Json.Nodes;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class SystemScanPersistenceStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-SystemScanPersistence-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExplicitFirstFootfallCorrectionCanClearPersistedTrue()
    {
        var path = CreateSystemFile(
            "Test_42.json",
            """
            {
              "name": "Test",
              "address": 42,
              "bodies": [{ "name": "Test 1", "id": 1, "firstFootFall": true }]
            }
            """);
        var snapshot = CreateSnapshot(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42}""",
            """{"event":"Disembark","SystemAddress":42,"Body":"Test 1","BodyID":1,"OnPlanet":true,"OnStation":false}""");
        var store = new SystemScanPersistenceStore(temporaryDirectory);

        await store.SaveFirstFootfallCorrectionAsync(
            new SystemScanPersistenceContext(
                "F123",
                "Drew",
                DateTimeOffset.Parse("2026-07-25T00:00:00Z")),
            snapshot,
            1,
            false);

        var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.False(saved["bodies"]![0]!["firstFootFall"]!.GetValue<bool>());
    }

    [Fact]
    public async Task SavePreservesImportedDataAndDetectsCompletedRepeatVisit()
    {
        var path = CreateSystemFile(
            "Test_42.json",
            """
            {
              "name": "Test",
              "address": 42,
              "firstVisited": "2026-07-20T00:00:00Z",
              "lastVisited": "2026-07-20T00:00:00Z",
              "futureRoot": { "value": 7 },
              "bodies": [
                {
                  "name": "Test 1",
                  "id": 1,
                  "bioSignalCount": 1,
                  "bookmarks": { "Aleoida": [{ "latitude": 1 }] },
                  "futureBody": true,
                  "organisms": [
                    {
                      "genus": "$Codex_Ent_Aleoids_Genus_Name;",
                      "analyzed": true,
                      "futureOrganism": "keep"
                    }
                  ]
                }
              ]
            }
            """);
        var snapshot = CreateSnapshot(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[1,2,3]}""");
        var store = new SystemScanPersistenceStore(temporaryDirectory);

        var result = await store.SaveAsync(
            new SystemScanPersistenceContext(
                "F123",
                "Drew",
                DateTimeOffset.Parse("2026-07-22T00:00:00Z")),
            snapshot);

        Assert.Equal(path, result.Path);
        Assert.True(result.IsRepeatVisit);
        Assert.Equal(0, result.BiologicalSignalsRemaining);
        Assert.True(result.ShouldSuppressBiologyOverlays);
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal(7, saved["futureRoot"]!["value"]!.GetValue<int>());
        Assert.Equal("Drew", saved["commander"]!.GetValue<string>());
        Assert.Equal(
            "2026-07-20T00:00:00Z",
            saved["firstVisited"]!.GetValue<string>());
        Assert.Equal(
            "2026-07-22T00:00:00.0000000+00:00",
            saved["lastVisited"]!.GetValue<string>());
        var body = Assert.IsType<JsonObject>(
            Assert.Single(saved["bodies"]!.AsArray()));
        Assert.True(body["futureBody"]!.GetValue<bool>());
        Assert.NotNull(body["bookmarks"]);
        Assert.Equal(
            "keep",
            body["organisms"]![0]!["futureOrganism"]!.GetValue<string>());
    }

    [Fact]
    public async Task IncompleteBiologyDoesNotSuppressRepeatVisit()
    {
        CreateSystemFile(
            "Test_42.json",
            """
            {
              "name": "Test",
              "address": 42,
              "firstVisited": "2026-07-20T00:00:00Z",
              "lastVisited": "2026-07-21T00:00:00Z",
              "bodies": [{ "name": "Test 1", "id": 1, "bioSignalCount": 2 }]
            }
            """);
        var store = new SystemScanPersistenceStore(temporaryDirectory);

        var result = await store.SaveAsync(
            new SystemScanPersistenceContext(
                "F123",
                "Drew",
                DateTimeOffset.Parse("2026-07-22T00:00:00Z")),
            CreateSnapshot(
                """{"event":"Location","StarSystem":"Test","SystemAddress":42}"""));

        Assert.True(result.IsRepeatVisit);
        Assert.Equal(2, result.BiologicalSignalsRemaining);
        Assert.False(result.ShouldSuppressBiologyOverlays);
    }

    [Fact]
    public async Task MalformedImportedFileIsNotOverwritten()
    {
        var path = CreateSystemFile("Test_42.json", "{ malformed");
        var before = await File.ReadAllBytesAsync(path);
        var store = new SystemScanPersistenceStore(temporaryDirectory);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.SaveAsync(
                new SystemScanPersistenceContext(
                    "F123",
                    "Drew",
                    DateTimeOffset.Parse("2026-07-22T00:00:00Z")),
                CreateSnapshot(
                    """{"event":"Location","StarSystem":"Test","SystemAddress":42}""")));

        Assert.Contains("was not overwritten", exception.Message);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task LoadProjectsImportedSystemHistoryWithoutChangingItsBytes()
    {
        var path = CreateSystemFile(
            "Test_42.json",
            """
            {
              "name": "Test",
              "address": 42,
              "starPos": [1, 2, 3],
              "bodyCount": 2,
              "honked": true,
              "futureRoot": { "keep": true },
              "bodies": [
                {
                  "name": "Test A",
                  "id": 0,
                  "type": "Star",
                  "starType": "K",
                  "scanned": true
                },
                {
                  "name": "Test 1",
                  "id": 1,
                  "type": "LandableBody",
                  "planetClass": "Rocky body",
                  "surfaceGravity": 9.5,
                  "bioSignalCount": 1,
                  "geoSignalCount": 1,
                  "materials": { "iron": 20 },
                  "parents": [
                    { "type": "Star", "id": 0 }
                  ],
                  "geoSignals": [
                    {
                      "entryId": 123,
                      "name": "$Codex_Ent_Geysers_Water_Name;",
                      "nameLocalized": "Water Geysers"
                    }
                  ],
                  "organisms": [
                    {
                      "genus": "$Codex_Ent_Aleoids_Genus_Name;",
                      "genusLocalized": "Aleoida",
                      "analyzed": true,
                      "futureOrganism": 7
                    }
                  ],
                  "futureBody": true
                }
              ]
            }
            """);
        var before = await File.ReadAllBytesAsync(path);
        var store = new SystemScanPersistenceStore(temporaryDirectory);

        var result = await store.LoadAsync(
            "F123",
            "Drew",
            "Test",
            42);

        Assert.True(result.Exists);
        Assert.Null(result.Error);
        var snapshot = Assert.IsType<SystemScanSnapshot>(result.Snapshot);
        Assert.Equal(new GalacticCoordinate(1, 2, 3), snapshot.StarPosition);
        Assert.True(snapshot.HasDiscoveryScan);
        Assert.Equal(2, snapshot.ExpectedBodyCount);
        Assert.Equal(2, snapshot.Bodies.Count);
        var body = snapshot.Bodies.Single(candidate => candidate.BodyId == 1);
        Assert.Equal(SystemBodyKind.LandablePlanet, body.Kind);
        Assert.Equal(9.5, body.SurfaceGravity);
        Assert.Equal(20, body.Materials["iron"]);
        Assert.Equal(
            new SystemBodyParentSnapshot(SystemBodyParentKind.Star, 0),
            Assert.Single(body.Parents));
        Assert.Equal(
            "Water Geysers",
            Assert.Single(body.AnalyzedGeologicalSignals));
        Assert.True(Assert.Single(body.Organisms).IsAnalyzed);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task LoadRejectsMalformedTypedHistoryWithoutChangingItsBytes()
    {
        var path = CreateSystemFile(
            "Test_42.json",
            """
            {
              "name": "Test",
              "address": 42,
              "bodies": [
                { "name": "Test 1", "id": 1, "type": "FutureBodyType" }
              ]
            }
            """);
        var before = await File.ReadAllBytesAsync(path);

        var result = await new SystemScanPersistenceStore(temporaryDirectory)
            .LoadAsync("F123", "Drew", "Test", 42);

        Assert.True(result.Exists);
        Assert.Null(result.Snapshot);
        Assert.Contains("not recognized", result.Error);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private string CreateSystemFile(string fileName, string json)
    {
        var directory = Path.Combine(temporaryDirectory, "systems", "F123");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, json);
        return path;
    }

    private static SystemScanSnapshot CreateSnapshot(params string[] events)
    {
        var state = new SystemScanState();
        foreach (var json in events)
        {
            Assert.True(
                JournalEventEnvelope.TryParse(
                    json,
                    out var journalEvent,
                    out var error),
                error);
            state.Apply(Assert.IsType<JournalEventEnvelope>(journalEvent));
        }

        return state.CreateSnapshot();
    }
}
