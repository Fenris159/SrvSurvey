using System.Text.Json.Nodes;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class CommanderProfileStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-commander-profile-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadReadsLegacyExplorationFields()
    {
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "F123-live.json"),
            """
            {
              "fid": "F123",
              "commander": "Drew",
              "isOdyssey": true,
              "explRewards": 123456,
              "distanceTravelled": 42.5,
              "countJumps": 3,
              "countScans": 4,
              "countDSS": 5,
              "countLanded": 6,
              "futureSetting": { "enabled": true }
            }
            """);
        var store = new CommanderProfileStore(temporaryDirectory);

        var result = await store.LoadAsync("F123", isOdyssey: true);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Exists);
        Assert.NotNull(result.Data);
        Assert.Equal("Drew", result.Data.CommanderName);
        Assert.Equal(
            new ExplorationSnapshot(123456, 42.5, 3, 4, 5, 6),
            result.Data.Exploration);
    }

    [Fact]
    public async Task SavePreservesUnknownAndConcurrentFields()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "F123-live.json");
        await File.WriteAllTextAsync(
            path,
            "{\"fid\":\"F123\",\"commander\":\"Drew\",\"futureSetting\":{\"enabled\":true},\"activeJourney\":\"Before\"}");
        var store = new CommanderProfileStore(temporaryDirectory);

        await File.WriteAllTextAsync(
            path,
            "{\"fid\":\"F123\",\"commander\":\"Drew\",\"futureSetting\":{\"enabled\":true},\"activeJourney\":\"Changed elsewhere\"}");
        await store.SaveExplorationAsync(
            "F123",
            "Drew",
            isOdyssey: true,
            new ExplorationSnapshot(9000, 12.25, 1, 2, 3, 4));

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.True(root["futureSetting"]!["enabled"]!.GetValue<bool>());
        Assert.Equal("Changed elsewhere", root["activeJourney"]!.GetValue<string>());
        Assert.Equal(9000, root["explRewards"]!.GetValue<long>());
        Assert.Equal(12.25, root["distanceTravelled"]!.GetValue<double>());
        Assert.Equal(4, root["countLanded"]!.GetValue<int>());
    }

    [Fact]
    public async Task SaveRefusesToOverwriteMalformedProfile()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "F123-live.json");
        const string malformed = "{\"fid\":\"F123\",";
        await File.WriteAllTextAsync(path, malformed);
        var store = new CommanderProfileStore(temporaryDirectory);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => store.SaveExplorationAsync(
                "F123",
                "Drew",
                isOdyssey: true,
                ExplorationSnapshot.Empty));

        Assert.Contains("was not overwritten", exception.Message);
        Assert.Equal(malformed, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task SaveCreatesCorrectLiveAndLegacyNames()
    {
        var store = new CommanderProfileStore(temporaryDirectory);

        await store.SaveExplorationAsync(
            "F123",
            "Drew",
            isOdyssey: false,
            ExplorationSnapshot.Empty);

        Assert.True(File.Exists(Path.Combine(temporaryDirectory, "F123-legacy.json")));
        Assert.False(File.Exists(Path.Combine(temporaryDirectory, "F123-live.json")));
    }

    [Fact]
    public async Task LoadAndSavePreserveLegacyExobiologyFields()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "F123-live.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "fid": "F123",
              "lastOrganicScan": "123|7|species",
              "scanOne": {
                "location": { "lat": 12.5, "long": -45.25, "futureCoordinate": 8 },
                "radius": 150,
                "genus": "$Codex_Ent_Aleoids_Genus_Name;",
                "species": "$Codex_Ent_Aleoids_01_Name;",
                "status": "Active",
                "entryId": 2310101,
                "body": "Test A 1",
                "futureSample": true
              },
              "scanTwo": null,
              "organicRewards": 7252500,
              "scannedBioEntryIds": ["123_7_2310101_7252500_False"],
              "countRadicoidaUnica": 2
            }
            """);
        var store = new CommanderProfileStore(temporaryDirectory);

        var loaded = await store.LoadAsync("F123", isOdyssey: true);

        Assert.NotNull(loaded.Data);
        var bio = loaded.Data.Exobiology;
        Assert.Equal("123|7|species", bio.LastOrganicScan);
        Assert.Equal(new SurfaceLocation(12.5, -45.25), bio.ScanOne?.Location);
        Assert.Equal(7_252_500, bio.OrganicRewards);
        Assert.Equal(2, bio.CountRadicoidaUnica);
        Assert.Single(bio.ScannedBioEntryIds);

        await store.SaveExobiologyAsync("F123", "Drew", true, bio);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.True(root["scanOne"]!["futureSample"]!.GetValue<bool>());
        Assert.Equal(
            8,
            root["scanOne"]!["location"]!["futureCoordinate"]!.GetValue<int>());
        Assert.Equal(
            "123_7_2310101_7252500_False",
            root["scannedBioEntryIds"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task ExobiologySaveRefusesMalformedProfileOverwrite()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "F123-live.json");
        const string malformed = "{\"fid\":\"F123\",";
        await File.WriteAllTextAsync(path, malformed);
        var store = new CommanderProfileStore(temporaryDirectory);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.SaveExobiologyAsync(
                "F123",
                "Drew",
                true,
                ExobiologySnapshot.Empty));

        Assert.Equal(malformed, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ConcurrentFeatureSavesDoNotLoseEitherUpdate()
    {
        var store = new CommanderProfileStore(temporaryDirectory);
        var bio = new ExobiologySnapshot(
            null,
            null,
            null,
            42,
            ["123_7_2310101_42_False"],
            0);

        await Task.WhenAll(
            store.SaveExplorationAsync(
                "F123",
                "Drew",
                true,
                new ExplorationSnapshot(99, 10, 1, 2, 3, 4)),
            store.SaveExobiologyAsync("F123", "Drew", true, bio));

        var result = await store.LoadAsync("F123", true);
        Assert.NotNull(result.Data);
        Assert.Equal(99, result.Data.Exploration.EstimatedRewards);
        Assert.Equal(42, result.Data.Exobiology.OrganicRewards);
        Assert.Single(result.Data.Exobiology.ScannedBioEntryIds);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
