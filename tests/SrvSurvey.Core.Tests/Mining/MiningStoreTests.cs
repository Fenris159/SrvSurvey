using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class MiningStoreTests
{
    [Fact]
    public void RejectsInvalidRecoveredSessionAndPresetBeforeRestore()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => MiningStore.Parse("""{"SchemaVersion":1,"Current":{"Prospects":null}}"""));
        Assert.Throws<System.Text.Json.JsonException>(() => MiningStore.Parse("""{"SchemaVersion":1,"Settings":{"AnnouncementPresets":{"test":null}}}"""));
    }

    [Fact]
    public void RecoveryKeepsCommandersSeparateAndRejectsCorruptRestoreWithoutReplacingData()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var store = new MiningStore(directory);
            var state = new MiningCommanderData { Current = new MiningSession { System = "Sol", Started = DateTimeOffset.UtcNow } };
            state.Rings.Add(new MiningRing { System = "Achenar", Body = "Ring", Position = new SrvSurvey.Core.Search.GalacticCoordinate(1, 2, 3) });
            store.Save("F1", state);
            Assert.Equal("Sol", store.Load("F1").Current?.System);
            Assert.Equal(3, store.Load("F1").Rings[0].Position?.Z);
            Assert.Null(store.Load("F2").Current);
            Assert.Throws<System.Text.Json.JsonException>(() => store.Restore("F1", "{}"));
            Assert.Equal("Sol", store.Load("F1").Current?.System);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
