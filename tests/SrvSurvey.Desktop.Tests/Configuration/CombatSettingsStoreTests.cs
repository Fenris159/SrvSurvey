using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class CombatSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-combat-settings-tests-{Guid.NewGuid():N}");

    [Fact]
    public void DefaultsMatchDisabledLegacyTestFeatures()
    {
        var store = new CombatSettingsStore(SettingsPath);

        Assert.Equal(CombatPreferences.Default, store.Load());
        Assert.False(store.Load().AutoShowFootCombat);
        Assert.False(store.Load().AutoShowMassacreMissions);
        Assert.False(store.Load().SuppressForActiveBuildProjects);
    }

    [Fact]
    public void SavesCombatSettingsWithoutLosingOtherSections()
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(
            SettingsPath,
            """{"Theme":{"Key":"raven-dark"},"Future":42}""");
        var store = new CombatSettingsStore(SettingsPath);

        store.Save(new CombatPreferences(
            AutoShowFootCombat: true,
            AutoShowMassacreMissions: true,
            SuppressForActiveBuildProjects: true));

        Assert.Equal(
            new CombatPreferences(true, true, true),
            store.Load());
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
