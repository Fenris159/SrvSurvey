using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Configuration;
using Xunit;

namespace SrvSurvey.Desktop.Tests.ProfileSync;

public sealed class WorkspaceContinuitySettingsStoreTests
{
    [Fact]
    public void DefaultsAndSelectionsArePortableWithoutChangingOtherSections()
    {
        using var root = new ProfileSyncTestRoot();
        var store = new WorkspaceContinuitySettingsStore(root.Paths.UiSettingsPath);
        Assert.Equal(new WorkspaceContinuityPreferences(), store.Load());
        root.Write("config", "cross-platform-ui.json", "{\"Theme\":\"gold\"}");
        var selections = new WorkspaceContinuityPreferences("surface-mining", 4, 3, 2, 1);
        store.Save(selections);
        Assert.Equal(selections, store.Load());
        Assert.Equal("gold", JsonNode.Parse(File.ReadAllText(root.Paths.UiSettingsPath))!["Theme"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void InvalidSavedTabsUseTheirSafeDefault(int index)
    {
        using var root = new ProfileSyncTestRoot();
        root.Write(
            "config",
            "cross-platform-ui.json",
            $"{{\"Workspace\":{{\"Navigation\":15,\"MiningTab\":{index},\"SurfaceMiningTab\":{index},\"GuardianTab\":{index},\"FleetCarrierTab\":{index}}}}}"
        );
        Assert.Equal(
            new WorkspaceContinuityPreferences(),
            new WorkspaceContinuitySettingsStore(root.Paths.UiSettingsPath).Load()
        );
    }
}
