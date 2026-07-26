using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class LocalizationSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-localization-settings-tests-{Guid.NewGuid():N}");

    [Fact]
    public void LegacyLanguageLoadsUntilCrossPlatformPreferenceIsSaved()
    {
        var dataDirectory = Path.Combine(temporaryDirectory, "profile");
        var settingsPath = Path.Combine(temporaryDirectory, "ui-settings.json");
        Directory.CreateDirectory(dataDirectory);
        const string legacySettings =
            "{\"lang\":\"fr\",\"futureLegacyValue\":42}";
        var legacyPath = Path.Combine(dataDirectory, "settings.json");
        File.WriteAllText(legacyPath, legacySettings);
        var store = new LocalizationSettingsStore(settingsPath, dataDirectory);

        Assert.Equal("fr", store.Load());

        store.Save("de");

        Assert.Equal("de", store.Load());
        Assert.Equal(legacySettings, File.ReadAllText(legacyPath));
        var root = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject;
        Assert.Equal(
            "de",
            root?["Localization"]?["Language"]?.GetValue<string>());
    }

    [Fact]
    public void InvalidOrCorruptLegacyLanguageFallsBackToEnglish()
    {
        var dataDirectory = Path.Combine(temporaryDirectory, "profile");
        var settingsPath = Path.Combine(temporaryDirectory, "ui-settings.json");
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(
            Path.Combine(dataDirectory, "settings.json"),
            "{\"lang\":\"Klingon\"}");

        Assert.Equal(
            "en",
            new LocalizationSettingsStore(settingsPath, dataDirectory).Load());

        File.WriteAllText(
            Path.Combine(dataDirectory, "settings.json"),
            "{not json");
        Assert.Equal(
            "en",
            new LocalizationSettingsStore(settingsPath, dataDirectory).Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
