using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class GuardianOverlaySettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-guardian-overlay-settings-{Guid.NewGuid():N}");

    [Fact]
    public void DefaultsMatchLegacyGuardianOverlayBehavior()
    {
        var store = new GuardianOverlaySettingsStore(SettingsPath);

        Assert.Equal(GuardianOverlayPreferences.Default, store.Load());
        Assert.True(store.Load().EnableGuardianSites);
        Assert.True(store.Load().AutoShowGuardianSummary);
        Assert.True(store.Load().AutoShowRamTah);
        Assert.False(store.Load().SuppressForActiveBuildProjects);
        Assert.True(store.Load().AutoZoomNearObelisks);
        Assert.False(store.Load().AutoZoomInSrvTurret);
        Assert.Equal(0, store.Load().OverlaySizeIndex);
    }

    [Fact]
    public void SavesPreferencesWithoutLosingOtherUiSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(
            SettingsPath,
            """{"Theme":{"Key":"raven-dark"},"Future":42}""");
        var store = new GuardianOverlaySettingsStore(SettingsPath);

        var expected = new GuardianOverlayPreferences(
            EnableGuardianSites: false,
            AutoShowGuardianSummary: false,
            AutoShowRamTah: true,
            SuppressForActiveBuildProjects: true,
            AutoZoomNearObelisks: false,
            AutoZoomInSrvTurret: true,
            OverlaySizeIndex: 4);
        store.Save(expected);

        Assert.Equal(expected, store.Load());
        var root = JsonNode.Parse(File.ReadAllText(SettingsPath))!.AsObject();
        Assert.Equal("raven-dark", root["Theme"]!["Key"]!.GetValue<string>());
        Assert.Equal(42, root["Future"]!.GetValue<int>());
    }

    private string SettingsPath => Path.Combine(
        temporaryDirectory,
        "ui-settings.json");

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
