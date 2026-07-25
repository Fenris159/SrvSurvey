using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class StationInfoSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-station-info-settings-{Guid.NewGuid():N}");

    [Fact]
    public void DefaultsToLegacyAutomaticBehavior()
    {
        var store = new StationInfoSettingsStore(SettingsPath);

        Assert.True(store.Load().AutoShow);
    }

    [Fact]
    public void SavesWithoutRemovingOtherSections()
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(SettingsPath, """{"Theme":{"Key":"raven-dark"}}""");
        var store = new StationInfoSettingsStore(SettingsPath);

        store.Save(new StationInfoPreferences(AutoShow: false));

        Assert.False(store.Load().AutoShow);
        var root = JsonNode.Parse(File.ReadAllText(SettingsPath))!.AsObject();
        Assert.Equal("raven-dark", root["Theme"]!["Key"]!.GetValue<string>());
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
