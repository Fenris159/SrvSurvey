using System.Text.Json.Nodes;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Settlements;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class HumanSiteKnowledgeStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-human-sites-{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadsLegacyCanonnStationGeometryAndMisspelledPads()
    {
        var path = CreateSystemPath();
        await File.WriteAllTextAsync(
            path,
            """
            {
              "name": "Test System",
              "address": 42,
              "bodies": [],
              "stations": [{
                "name": "Haberlandt Survey",
                "marketId": 12345,
                "systemAddress": 42,
                "bodyId": 3,
                "stationEconomy": "$economy_Agri;",
                "lat": 12.5,
                "long": -45.25,
                "heading": 370,
                "subType": 4,
                "calcMethod": "AutoDock",
                "availblePads": {"Large":1,"Medium":0,"Small":2}
              }]
            }
            """);
        var store = new HumanSiteKnowledgeStore(temporaryDirectory);

        var result = await store.LoadAsync(Context(), 12345);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.FileExists);
        Assert.True(result.SiteExists);
        Assert.Equal(4, result.Knowledge!.SubType);
        Assert.Equal(10, result.Knowledge.Heading);
        Assert.Equal(HumanSiteGeometrySource.AutoDock,
            result.Knowledge.GeometrySource);
        Assert.Equal(new HumanSiteLandingPads(2, 0, 1),
            result.Knowledge.AvailablePads);
    }

    [Fact]
    public async Task MissingSiteReturnsWithoutWriting()
    {
        var store = new HumanSiteKnowledgeStore(temporaryDirectory);

        var result = await store.LoadAsync(Context(), 12345);

        Assert.False(result.FileExists);
        Assert.False(result.SiteExists);
        Assert.Null(result.Knowledge);
        Assert.False(File.Exists(result.Path));
    }

    [Fact]
    public async Task SaveCreatesLegacyCompatibleStationAndPreservesUnknownData()
    {
        var path = CreateSystemPath();
        await File.WriteAllTextAsync(
            path,
            """
            {
              "name": "Test System",
              "address": 42,
              "futureSystem": true,
              "bodies": [],
              "stations": [{"name":"Other","marketId":999,"futureStation":7}]
            }
            """);
        var store = new HumanSiteKnowledgeStore(temporaryDirectory);

        await store.SaveAsync(
            Context(),
            Site() with
            {
                SubType = 4,
                Heading = 270,
                AvailablePads = new HumanSiteLandingPads(2, 0, 1),
            },
            HumanSiteGeometrySource.ManualFoot);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.True(root["futureSystem"]!.GetValue<bool>());
        var stations = root["stations"]!.AsArray();
        Assert.Equal(2, stations.Count);
        Assert.Equal(7, stations[0]!["futureStation"]!.GetValue<int>());
        var saved = stations[1]!.AsObject();
        Assert.Equal(12345, saved["marketId"]!.GetValue<long>());
        Assert.Equal("$economy_Agri;",
            saved["stationEconomy"]!.GetValue<string>());
        Assert.Equal(270, saved["heading"]!.GetValue<double>());
        Assert.Equal("ManualFoot", saved["calcMethod"]!.GetValue<string>());
        Assert.Equal(2,
            saved["availblePads"]!["Small"]!.GetValue<int>());
    }

    [Fact]
    public async Task WeakerObservationDoesNotEraseKnownGeometry()
    {
        var store = new HumanSiteKnowledgeStore(temporaryDirectory);
        await store.SaveAsync(
            Context(),
            Site() with { SubType = 4, Heading = 270 },
            HumanSiteGeometrySource.AutoDock);

        await store.SaveAsync(
            Context(),
            Site() with { SubType = 4, Heading = 270 });
        var result = await store.LoadAsync(Context(), 12345);

        Assert.Equal(4, result.Knowledge!.SubType);
        Assert.Equal(270, result.Knowledge.Heading);
        Assert.Equal(HumanSiteGeometrySource.AutoDock,
            result.Knowledge.GeometrySource);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private string CreateSystemPath()
    {
        var directory = Path.Combine(temporaryDirectory, "systems", "F123");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "Test System_42.json");
    }

    private static HumanSiteKnowledgeContext Context()
    {
        return new HumanSiteKnowledgeContext(
            "F123",
            "Cmdr Test",
            "Test System",
            42,
            new GalacticCoordinate(1, 2, 3),
            6_000_000);
    }

    private static HumanSiteLiveSnapshot Site()
    {
        return new HumanSiteLiveSnapshot(
            "Haberlandt Survey",
            "Haberlandt Survey",
            12345,
            42,
            3,
            "Test System 1",
            new HumanSiteSurfaceLocation(12.5, -45.25),
            HumanSiteEconomy.Agriculture,
            "$economy_Agri;",
            "Agriculture",
            "Raven Colonial",
            null,
            "$government_Democracy;",
            "Democracy",
            ["dock"],
            "OnFootSettlement",
            HumanSiteLandingPads.Empty,
            0,
            null,
            null,
            HumanSiteDockingStatus.None,
            0,
            null,
            false,
            DateTimeOffset.Parse("2026-07-25T03:00:00Z"),
            DateTimeOffset.Parse("2026-07-25T03:10:00Z"));
    }
}
