using System.Text.Json.Nodes;
using SrvSurvey.Core.Settlements;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class HumanSiteMaterialStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-human-material-{Guid.NewGuid():N}");
    private readonly HumanSiteLiveSnapshot site = CreateSite();

    [Fact]
    public async Task AppendWritesLegacyCompatibleSurveyAndPreservesUnknownFields()
    {
        var path = SurveyPath("2026-07-24 120000");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            """
            {"name":"Old","marketId":12345,"systemAddress":42,"completed":false,"totalMatCount":2,"countMats":{"graphene":2},"countTypes":{"Component":2},"countBuildings":{"HAB":2},"matLocations":["graphene_Component_1.5_-2.25"],"future":{"keep":true}}
            """);
        var store = new HumanSiteMaterialStore(temporaryDirectory);

        var result = await store.AppendAsync(
            Context(),
            [
                new HumanSiteCollectedMaterial(
                    "opinionpolls",
                    "Opinion Polls",
                    "Data",
                    2,
                    new HumanSiteMapPoint(3.25, 4.5),
                    null),
            ]);

        Assert.Equal(path, result.Path);
        Assert.Equal(4, result.Survey.TotalMaterialCount);
        Assert.Equal(2, result.Survey.CountByMaterial["graphene"]);
        Assert.Equal(2, result.Survey.CountByMaterial["opinionpolls"]);
        Assert.Equal(2, result.Survey.Materials.Count);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!
            .AsObject();
        Assert.True(root["future"]!["keep"]!.GetValue<bool>());
        Assert.Equal(
            "opinionpolls_Data_3.25_4.5",
            root["matLocations"]![1]!.GetValue<string>());
    }

    [Fact]
    public async Task CompletedSurveyStartsNewTimestampedFile()
    {
        var oldPath = SurveyPath("2026-07-24 120000");
        Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
        await File.WriteAllTextAsync(
            oldPath,
            """{"completed":true,"totalMatCount":1,"matLocations":["old_Data_1_2"]}""");
        var time = new FixedTimeProvider(
            DateTimeOffset.Parse("2026-07-25T13:14:15Z"));
        var store = new HumanSiteMaterialStore(temporaryDirectory, time);

        var result = await store.AppendAsync(
            Context(),
            [Material("new", "Component", 5, 6)]);

        Assert.NotEqual(oldPath, result.Path);
        Assert.EndsWith("42-12345-2026-07-25 131415.json", result.Path);
        Assert.True(File.Exists(oldPath));
        Assert.Single(result.Survey.Materials);
    }

    [Fact]
    public async Task CompletionClosesSurveyAndNextLoadIsEmpty()
    {
        var store = new HumanSiteMaterialStore(temporaryDirectory);
        await store.AppendAsync(
            Context(),
            [Material("graphene", "Component", 1, 2)]);

        var completed = await store.CompleteAsync(Context());
        var loaded = await store.LoadActiveAsync(Context());

        Assert.True(completed.Survey.Completed);
        Assert.False(loaded.Exists);
        Assert.NotNull(loaded.Survey);
        Assert.Empty(loaded.Survey.Materials);
    }

    [Fact]
    public async Task CorruptLatestSurveyIsNotOverwritten()
    {
        var path = SurveyPath("2026-07-24 120000");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string corrupt = "{ not json";
        await File.WriteAllTextAsync(path, corrupt);
        var store = new HumanSiteMaterialStore(temporaryDirectory);

        var load = await store.LoadActiveAsync(Context());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.AppendAsync(
            Context(),
            [Material("graphene", "Component", 1, 2)]));

        Assert.Equal(path, load.Path);
        Assert.NotNull(load.Error);
        Assert.Equal(corrupt, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ConcurrentAppendsRetainBothLocations()
    {
        var store = new HumanSiteMaterialStore(temporaryDirectory);

        await Task.WhenAll(
            store.AppendAsync(
                Context(),
                [Material("graphene", "Component", 1, 2)]),
            store.AppendAsync(
                Context(),
                [Material("opinionpolls", "Data", 3, 4)]));
        var loaded = await store.LoadActiveAsync(Context());

        Assert.NotNull(loaded.Survey);
        Assert.Equal(2, loaded.Survey.TotalMaterialCount);
        Assert.Equal(2, loaded.Survey.Materials.Count);
        Assert.Equal(1, loaded.Survey.CountByMaterial["graphene"]);
        Assert.Equal(1, loaded.Survey.CountByMaterial["opinionpolls"]);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private HumanSiteMaterialContext Context()
    {
        return new HumanSiteMaterialContext("F123", site);
    }

    private string SurveyPath(string timestamp)
    {
        return Path.Combine(
            temporaryDirectory,
            "footMatStats",
            "F123",
            $"42-12345-{timestamp}.json");
    }

    private static HumanSiteCollectedMaterial Material(
        string name,
        string type,
        double x,
        double y)
    {
        return new HumanSiteCollectedMaterial(
            name,
            null,
            type,
            1,
            new HumanSiteMapPoint(x, y),
            null);
    }

    private static HumanSiteLiveSnapshot CreateSite()
    {
        return new HumanSiteLiveSnapshot(
            "Test Settlement",
            "Test Settlement",
            12345,
            42,
            3,
            "Test 1",
            new HumanSiteSurfaceLocation(1, 2),
            HumanSiteEconomy.Extraction,
            "$economy_Extraction;",
            "Extraction",
            "Raven Colonial",
            "Boom",
            "$government_Democracy;",
            "Democracy",
            ["dock"],
            "OnFootSettlement",
            HumanSiteLandingPads.Empty,
            5,
            null,
            90,
            HumanSiteDockingStatus.None,
            0,
            null,
            true,
            default,
            default);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
