using System.Text.Json.Nodes;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class LocalizationSettingsStoreTests : IDisposable
{
    /// <summary>New profiles inherit the previous language without later shared-file edits replacing their own selection.</summary>
    [Fact]
    public void NewCommanderInheritsSharedLanguageUntilItsOwnSettingsExist()
    {
        var shared = new AppDataPaths(
            Path.Combine(temporaryDirectory, "config"),
            Path.Combine(temporaryDirectory, "data"),
            temporaryDirectory,
            []
        );
        var sharedStore = new LocalizationSettingsStore(shared.SharedUiSettingsPath, shared.DataDirectory);
        sharedStore.Save("de");
        AppDataPaths commander = shared with { SettingsFrontierId = "F123" };
        Assert.Equal("de", LocalizationSettingsStore.ResolveCurrent(commander));
        CommanderSettingsProfile.Prepare(commander);
        new LocalizationSettingsStore(commander.UiSettingsPath, commander.OverlaySettingsDirectory).Save("fr");
        sharedStore.Save("en");
        Assert.Equal("fr", LocalizationSettingsStore.ResolveCurrent(commander));
        Assert.Equal("en", LocalizationSettingsStore.ResolveCurrent(shared));
    }

    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-localization-settings-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public void LegacyLanguageLoadsUntilCrossPlatformPreferenceIsSaved()
    {
        string dataDirectory = Path.Combine(temporaryDirectory, "profile");
        string settingsPath = Path.Combine(temporaryDirectory, "ui-settings.json");
        Directory.CreateDirectory(dataDirectory);
        const string legacySettings = "{\"lang\":\"fr\",\"futureLegacyValue\":42}";
        string legacyPath = Path.Combine(dataDirectory, "settings.json");
        File.WriteAllText(legacyPath, legacySettings);
        var store = new LocalizationSettingsStore(settingsPath, dataDirectory);

        Assert.Equal("fr", store.Load());

        store.Save("de");

        Assert.Equal("de", store.Load());
        Assert.Equal(legacySettings, File.ReadAllText(legacyPath));
        var root = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject;
        Assert.Equal("de", root?["Localization"]?["Language"]?.GetValue<string>());
    }

    [Fact]
    public void InvalidOrCorruptLegacyLanguageFallsBackToEnglish()
    {
        string dataDirectory = Path.Combine(temporaryDirectory, "profile");
        string settingsPath = Path.Combine(temporaryDirectory, "ui-settings.json");
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(Path.Combine(dataDirectory, "settings.json"), "{\"lang\":\"Klingon\"}");

        Assert.Equal("en", new LocalizationSettingsStore(settingsPath, dataDirectory).Load());

        File.WriteAllText(Path.Combine(dataDirectory, "settings.json"), "{not json");
        Assert.Equal("en", new LocalizationSettingsStore(settingsPath, dataDirectory).Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
