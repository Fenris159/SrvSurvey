using System.Text.Json.Nodes;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class SystemSurfaceStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-system-surface-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadsLegacyTouchdownBookmarksAndBiologicalScans()
    {
        var path = CreateSystemPath();
        await File.WriteAllTextAsync(
            path,
            """
            {
              "name": "Test System",
              "address": 42,
              "bodies": [
                {
                  "name": "Test System 1 a",
                  "id": 7,
                  "radius": 1000,
                  "lastTouchdown": { "lat": 1.5, "long": -2.25 },
                  "bookmarks": {
                    "Aleoida": [
                      { "lat": 1, "long": 2 },
                      { "lat": 95, "long": 0 }
                    ],
                    "#mini": [{ "lat": -3, "long": 4 }]
                  },
                  "bioScans": [
                    {
                      "location": { "lat": 5, "long": 6 },
                      "radius": 150,
                      "genus": "$Codex_Ent_Aleoids_Genus_Name;",
                      "species": "$Codex_Ent_Aleoids_01_Name;",
                      "status": "Complete",
                      "entryId": 123,
                      "body": "Test System 1 a"
                    },
                    {
                      "location": { "lat": 0, "long": 0 },
                      "radius": -1
                    }
                  ]
                }
              ]
            }
            """);
        var store = new SystemSurfaceStore(temporaryDirectory);

        var result = await store.LoadBodyAsync(Context());

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.FileExists);
        Assert.True(result.BodyExists);
        Assert.Equal(
            new SurfaceCoordinate(1.5, -2.25),
            result.Snapshot!.LastTouchdown);
        Assert.Equal(
            new SurfaceCoordinate(1, 2),
            Assert.Single(result.Snapshot.Bookmarks["Aleoida"]));
        Assert.Equal(
            new SurfaceCoordinate(-3, 4),
            Assert.Single(result.Snapshot.Bookmarks["#mini"]));
        var scan = Assert.Single(result.Snapshot.BioScans);
        Assert.Equal("Complete", scan.Status);
        Assert.Equal(150, scan.RadiusMeters);
        Assert.Equal(123, scan.EntryId);
        Assert.Equal(2, result.Warnings.Count);
    }

    [Fact]
    public async Task MissingFileReturnsAnEmptyCompatibleBodyWithoutWriting()
    {
        var store = new SystemSurfaceStore(temporaryDirectory);

        var result = await store.LoadBodyAsync(Context());

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.FileExists);
        Assert.False(result.BodyExists);
        Assert.Equal(7, result.Snapshot!.BodyId);
        Assert.Empty(result.Snapshot.Bookmarks);
        Assert.Empty(result.Snapshot.BioScans);
        Assert.False(File.Exists(result.Path));
    }

    [Fact]
    public async Task DeathMarksEveryMatchingSampleAcrossExistingSystemFiles()
    {
        var path = CreateSystemPath();
        await File.WriteAllTextAsync(
            path,
            """
            {
              "name": "Test System",
              "address": 42,
              "futureSystem": true,
              "bodies": [
                {
                  "name": "Test System 1 a",
                  "id": 7,
                  "bioScans": [
                    {"location":{"lat":1,"long":2},"radius":150,"species":"one","status":"Complete","entryId":123},
                    {"location":{"lat":2,"long":3},"radius":150,"species":"one","status":"Complete","entryId":123},
                    {"location":{"lat":3,"long":4},"radius":150,"species":"one","status":"Complete","entryId":123},
                    {"location":{"lat":4,"long":5},"radius":150,"species":"two","status":"Abandoned","entryId":456}
                  ]
                }
              ]
            }
            """);
        var store = new SystemSurfaceStore(temporaryDirectory);

        var result = await store.MarkBioScansDiedAsync(
            "F123",
            ["42_7_123_7252500_False", "42_7_456_1_False", "invalid"]);

        Assert.Equal(3, result.MarkedScanCount);
        Assert.Equal(1, result.ChangedFileCount);
        Assert.Single(result.Warnings);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.True(root["futureSystem"]!.GetValue<bool>());
        var scans = root["bodies"]![0]!["bioScans"]!.AsArray();
        Assert.Equal(
            ["Died", "Died", "Died", "Abandoned"],
            scans.Select(scan => scan!["status"]!.GetValue<string>()));

        var missing = await store.MarkBioScansDiedAsync(
            "F123",
            ["99_1_123_1_False"]);
        Assert.Equal(0, missing.MarkedScanCount);
        Assert.Equal(0, missing.ChangedFileCount);
        Assert.DoesNotContain(
            Directory.EnumerateFiles(
                Path.Combine(temporaryDirectory, "systems", "F123")),
            file => Path.GetFileName(file).EndsWith("_99.json"));
    }

    [Fact]
    public async Task MutationsPreserveNotesUnknownFieldsAndExistingBodyData()
    {
        var path = CreateSystemPath();
        await File.WriteAllTextAsync(
            path,
            """
            {
              "name": "Test System",
              "address": 42,
              "notes": "Keep me",
              "futureSystem": 9,
              "bodies": [
                {
                  "name": "Test System 1 a",
                  "id": 7,
                  "radius": 1000,
                  "futureBody": true,
                  "bookmarks": {
                    "Aleoida": [{ "lat": 0, "long": 0 }]
                  },
                  "bioScans": []
                }
              ]
            }
            """);
        var store = new SystemSurfaceStore(temporaryDirectory);

        await store.SetLastTouchdownAsync(
            Context(),
            new SurfaceCoordinate(1, 2));
        var added = await store.AddBookmarkAsync(
            Context(),
            "Bacterium",
            new SurfaceCoordinate(3, 4));
        await store.AppendBioScansAsync(
            Context(),
            [new SurfaceBioScan(
                new SurfaceCoordinate(5, 6),
                500,
                "$Codex_Ent_Bacterial_Genus_Name;",
                "$Codex_Ent_Bacterial_01_Name;",
                "Complete",
                456,
                "Test System 1 a")]);

        Assert.Equal(SurfaceBookmarkMutation.Added, added.Mutation);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal("Keep me", root["notes"]!.GetValue<string>());
        Assert.Equal(9, root["futureSystem"]!.GetValue<int>());
        var body = root["bodies"]![0]!.AsObject();
        Assert.True(body["futureBody"]!.GetValue<bool>());
        Assert.Equal(1, body["lastTouchdown"]!["lat"]!.GetValue<double>());
        Assert.Single(body["bookmarks"]!["Aleoida"]!.AsArray());
        Assert.Single(body["bookmarks"]!["Bacterium"]!.AsArray());
        Assert.Equal(456, body["bioScans"]![0]!["entryId"]!.GetValue<long>());
    }

    [Fact]
    public async Task BookmarkSeparationAndScanIdentityMatchLegacyRules()
    {
        var store = new SystemSurfaceStore(temporaryDirectory);
        var first = new SurfaceCoordinate(0, 0);

        var added = await store.AddBookmarkAsync(Context(), "Aleoida", first);
        var tooClose = await store.AddBookmarkAsync(
            Context(),
            "Aleoida",
            new SurfaceCoordinate(0, 0.5));
        var scan = new SurfaceBioScan(
            first,
            150,
            "genus",
            "species",
            "Complete",
            1,
            "Test System 1 a");
        await store.AppendBioScansAsync(Context(), [scan, scan]);

        Assert.Equal(SurfaceBookmarkMutation.Added, added.Mutation);
        Assert.Equal(SurfaceBookmarkMutation.TooClose, tooClose.Mutation);
        var loaded = await store.LoadBodyAsync(Context());
        Assert.Single(loaded.Snapshot!.Bookmarks["Aleoida"]);
        Assert.Single(loaded.Snapshot.BioScans);
    }

    [Fact]
    public async Task RemovesNearestBookmarkOnlyInsideSamplingThreshold()
    {
        var store = new SystemSurfaceStore(temporaryDirectory);
        await store.AddBookmarkAsync(
            Context(),
            "Aleoida",
            new SurfaceCoordinate(0, 0));
        await store.AddBookmarkAsync(
            Context(),
            "Aleoida",
            new SurfaceCoordinate(0, 10));

        var removed = await store.RemoveBookmarkAsync(
            Context(),
            "Aleoida",
            new SurfaceCoordinate(0, 1),
            maximumDistanceMeters: 150);
        var outside = await store.RemoveBookmarkAsync(
            Context(),
            "Aleoida",
            new SurfaceCoordinate(0, -90),
            maximumDistanceMeters: 150);

        Assert.Equal(SurfaceBookmarkMutation.Removed, removed.Mutation);
        Assert.Equal(SurfaceBookmarkMutation.NotFound, outside.Mutation);
        var loaded = await store.LoadBodyAsync(Context());
        Assert.Equal(
            new SurfaceCoordinate(0, 10),
            Assert.Single(loaded.Snapshot!.Bookmarks["Aleoida"]));
    }

    [Fact]
    public async Task NoteAndSurfaceStoresSerializeUpdatesToTheSameFile()
    {
        var noteStore = new SystemNoteStore(temporaryDirectory);
        var surfaceStore = new SystemSurfaceStore(temporaryDirectory);
        var noteContext = new SystemNoteContext(
            "F123",
            "Drew",
            "Test System",
            42,
            new GalacticCoordinate(1, 2, 3));

        var tasks = Enumerable.Range(0, 20)
            .SelectMany(index => new Task[]
            {
                noteStore.SaveAsync(noteContext, $"Note {index}"),
                surfaceStore.AddBookmarkAsync(
                    Context(),
                    $"Group {index}",
                    new SurfaceCoordinate(index - 10, index)),
            })
            .ToArray();
        await Task.WhenAll(tasks);

        var note = await noteStore.LoadAsync("F123", "Test System", 42);
        var surface = await surfaceStore.LoadBodyAsync(Context());
        Assert.StartsWith("Note ", note.Notes);
        Assert.Equal(20, surface.Snapshot!.Bookmarks.Count);
    }

    [Fact]
    public async Task MalformedKnownCollectionsAreNotOverwritten()
    {
        var path = CreateSystemPath();
        const string malformed =
            "{\"name\":\"Test System\",\"address\":42,\"bodies\":{}}";
        await File.WriteAllTextAsync(path, malformed);
        var store = new SystemSurfaceStore(temporaryDirectory);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.AddBookmarkAsync(
                Context(),
                "Aleoida",
                new SurfaceCoordinate(1, 2)));

        Assert.Equal(malformed, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task InvalidScanIsRejectedBeforeCreatingAFile()
    {
        var store = new SystemSurfaceStore(temporaryDirectory);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.AppendBioScansAsync(
                Context(),
                [new SurfaceBioScan(
                    new SurfaceCoordinate(1, 2),
                    0,
                    "genus",
                    "species",
                    "Complete",
                    1,
                    "Test System 1 a")]));

        var result = await store.LoadBodyAsync(Context());
        Assert.False(result.FileExists);
    }

    private SystemSurfaceContext Context()
    {
        return new SystemSurfaceContext(
            "F123",
            "Drew",
            "Test System",
            42,
            new GalacticCoordinate(1, 2, 3),
            7,
            "Test System 1 a",
            1_000);
    }

    private string CreateSystemPath()
    {
        var directory = Path.Combine(temporaryDirectory, "systems", "F123");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "Test System_42.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
