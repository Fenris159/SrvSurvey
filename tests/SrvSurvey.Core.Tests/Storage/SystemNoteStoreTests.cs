using System.Text.Json.Nodes;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class SystemNoteStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-system-note-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadFindsLegacySystemByAddressAndReadsNotes()
    {
        var systemsDirectory = CreateSystemsDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(systemsDirectory, "Old Name_10477373803.json"),
            """
            {
              "name": "Renamed System",
              "address": 10477373803,
              "notes": "Remember this place"
            }
            """);
        var store = new SystemNoteStore(temporaryDirectory);

        var result = await store.LoadAsync(
            "F123",
            "Renamed System",
            10477373803);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Exists);
        Assert.Equal("Remember this place", result.Notes);
        Assert.Equal("Old Name_10477373803.json", Path.GetFileName(result.Path));
    }

    [Fact]
    public async Task SavePreservesAllUnknownLegacySystemData()
    {
        var systemsDirectory = CreateSystemsDirectory();
        var path = Path.Combine(systemsDirectory, "Test System_42.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "name": "Test System",
              "address": 42,
              "notes": "Before",
              "bodies": [{ "name": "Test System 1", "futureBody": 7 }],
              "futureField": { "enabled": true }
            }
            """);
        var store = new SystemNoteStore(temporaryDirectory);

        await store.SaveAsync(
            new SystemNoteContext(
                "F123",
                "Drew",
                "Test System",
                42,
                new GalacticCoordinate(1, 2, 3)),
            "After");

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal("After", root["notes"]!.GetValue<string>());
        Assert.True(root["futureField"]!["enabled"]!.GetValue<bool>());
        Assert.Equal(7, root["bodies"]![0]!["futureBody"]!.GetValue<int>());
        Assert.Null(root["starPos"]);
        Assert.Null(root["commander"]);
    }

    [Fact]
    public async Task SaveRefusesToOverwriteMalformedSystemData()
    {
        var systemsDirectory = CreateSystemsDirectory();
        var path = Path.Combine(systemsDirectory, "Test System_42.json");
        const string malformed = "{\"name\":\"Test System\",";
        await File.WriteAllTextAsync(path, malformed);
        var store = new SystemNoteStore(temporaryDirectory);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.SaveAsync(
                new SystemNoteContext(
                    "F123",
                    "Drew",
                    "Test System",
                    42,
                    null),
                "Do not write"));

        Assert.Equal(malformed, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task SaveCreatesLegacyCompatibleSystemDataWithSafeName()
    {
        var store = new SystemNoteStore(temporaryDirectory);
        var context = new SystemNoteContext(
            "F123",
            "Drew",
            "Test: System/One",
            42,
            new GalacticCoordinate(1.5, -2.25, 3));

        var path = await store.SaveAsync(context, "A new note");

        Assert.Equal("Test- System-One_42.json", Path.GetFileName(path));
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal("Test: System/One", root["name"]!.GetValue<string>());
        Assert.Equal(42, root["address"]!.GetValue<long>());
        Assert.Equal("Drew", root["commander"]!.GetValue<string>());
        Assert.Equal("A new note", root["notes"]!.GetValue<string>());
        Assert.Equal(1.5, root["starPos"]![0]!.GetValue<double>());
        Assert.Empty(root["bodies"]!.AsArray());
    }

    [Fact]
    public async Task LoadFallsBackToLegacySafeSystemName()
    {
        var systemsDirectory = CreateSystemsDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(systemsDirectory, "Test- System_99.json"),
            "{\"notes\":\"Found by name\"}");
        var store = new SystemNoteStore(temporaryDirectory);

        var result = await store.LoadAsync("F123", "Test: System", 0);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Found by name", result.Notes);
    }

    [Fact]
    public async Task MissingSystemDoesNotCreateAFileUntilSaved()
    {
        var store = new SystemNoteStore(temporaryDirectory);

        var result = await store.LoadAsync("F123", "Test System", 42);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Exists);
        Assert.Equal(string.Empty, result.Notes);
        Assert.False(File.Exists(result.Path));
    }

    [Fact]
    public async Task FrontierIdCannotEscapeTheDataDirectory()
    {
        var store = new SystemNoteStore(temporaryDirectory);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.LoadAsync("../outside", "Test System", 42));
    }

    private string CreateSystemsDirectory()
    {
        var path = Path.Combine(temporaryDirectory, "systems", "F123");
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
