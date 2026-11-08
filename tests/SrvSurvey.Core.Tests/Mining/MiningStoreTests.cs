using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class MiningStoreTests
{
    [Fact]
    public void LegacyReceptionPreferenceIsIgnoredAndNotSavedAgain()
    {
        MiningCommanderData data = MiningStore.Parse(
            """{"SchemaVersion":1,"Settings":{"ReceiveCommunityData":true,"AutoStart":false,"SearchOptions":{"Reference":"Wille"}}}"""
        );
        Assert.False(data.Settings.AutoStart);
        Assert.Equal("Wille", data.Settings.SearchOptions.Reference);
        Assert.DoesNotContain("ReceiveCommunityData", MiningStore.Export(data), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpeningMiningStorageRemovesOnlyTheRetiredReceptionCache(bool hasCache)
    {
        string directory = Path.Combine(Path.GetTempPath(), "mining-no-reception-" + Guid.NewGuid());
        string mining = Path.Combine(directory, "mining");
        string obsolete = Path.Combine(mining, "community-cache.json");
        string history = Path.Combine(mining, "commander.json");
        string outbound = Path.Combine(directory, "eddn-outbox.json");
        try
        {
            Directory.CreateDirectory(mining);
            File.WriteAllText(history, "preserve commander history");
            File.WriteAllText(outbound, "preserve outbound EDDN contributions");
            if (hasCache)
            {
                File.WriteAllText(obsolete, "malformed retired data must never be read");
            }
            var store = new MiningStore(directory);
            Assert.False(File.Exists(obsolete));
            Assert.Equal("preserve commander history", File.ReadAllText(history));
            Assert.Equal("preserve outbound EDDN contributions", File.ReadAllText(outbound));
            store.Save("F1", new MiningCommanderData());
            Assert.Empty(store.Load("F1").History);
            Assert.False(File.Exists(obsolete));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void LegacyMiningDataUsesFiveSessionResultsSortedByAverage()
    {
        MiningPreferences settings = MiningStore.Parse("""{"SchemaVersion":1}""").Settings;
        Assert.Equal(MiningSessionYieldSort.AveragePercentage, settings.SessionYieldSort);
        Assert.Equal(5, settings.SessionYieldLimit);
    }

    [Theory]
    [InlineData(MiningSessionYieldSort.AveragePercentage, 1)]
    [InlineData(MiningSessionYieldSort.Finds, 100)]
    public void SessionOverlayOptionsRoundTrip(MiningSessionYieldSort sort, int limit)
    {
        var data = new MiningCommanderData();
        data.Settings.SessionYieldSort = sort;
        data.Settings.SessionYieldLimit = limit;
        MiningPreferences recovered = MiningStore.Parse(MiningStore.Export(data)).Settings;
        Assert.Equal(sort, recovered.SessionYieldSort);
        Assert.Equal(limit, recovered.SessionYieldLimit);
    }

    [Theory]
    [InlineData("SessionYieldSort", -1)]
    [InlineData("SessionYieldSort", 2)]
    [InlineData("SessionYieldLimit", 0)]
    [InlineData("SessionYieldLimit", 101)]
    public void RejectsInvalidSessionOverlayOptions(string property, int value)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(
            new { SchemaVersion = 1, Settings = new Dictionary<string, int> { [property] = value } }
        );
        Assert.Throws<System.Text.Json.JsonException>(() => MiningStore.Parse(json));
    }

    [Fact]
    public void RejectsInvalidRecoveredSessionAndPresetBeforeRestore()
    {
        Assert.Throws<System.Text.Json.JsonException>(() =>
            MiningStore.Parse("""{"SchemaVersion":1,"Current":{"Prospects":null}}""")
        );
        Assert.Throws<System.Text.Json.JsonException>(() =>
            MiningStore.Parse("""{"SchemaVersion":1,"Settings":{"AnnouncementPresets":{"test":null}}}""")
        );
    }

    [Fact]
    public void RecoveryKeepsCommandersSeparateAndRejectsCorruptRestoreWithoutReplacingData()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var store = new MiningStore(directory);
            var state = new MiningCommanderData
            {
                Current = new MiningSession { System = "Sol", Started = DateTimeOffset.UtcNow },
            };
            state.Rings.Add(
                new MiningRing
                {
                    System = "Achenar",
                    Body = "Ring",
                    Position = new SrvSurvey.Core.Search.GalacticCoordinate(1, 2, 3),
                }
            );
            store.Save("F1", state);
            Assert.Equal("Sol", store.Load("F1").Current?.System);
            Assert.Equal(3, store.Load("F1").Rings[0].Position?.Z);
            Assert.Null(store.Load("F2").Current);
            Assert.Throws<System.Text.Json.JsonException>(() => store.Restore("F1", "{}"));
            Assert.Equal("Sol", store.Load("F1").Current?.System);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
