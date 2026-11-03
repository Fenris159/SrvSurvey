using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class ScreenshotProcessingSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-screenshot-settings-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public void SaveRoundTripsAndPreservesUnknownSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Future\":42,\"Screenshots\":{\"FutureOption\":true}}");
        var store = new ScreenshotProcessingSettingsStore(path);
        var preferences = new ScreenshotProcessingPreferences(
            true,
            false,
            true,
            false,
            "/screenshots/source",
            "/screenshots/target",
            false,
            "#12ABEF",
            true,
            1100,
            1500,
            1700
        );

        store.Save(preferences);

        Assert.Equal(preferences, store.Load());
        JsonObject root = Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(path)));
        Assert.Equal(42, root["Future"]?.GetValue<int>());
        Assert.True(root["Screenshots"]?["FutureOption"]?.GetValue<bool>());
    }

    [Fact]
    public void AutomaticFoldersFollowTheJournalAfterSavingOtherPreferences()
    {
        string first = Path.Combine(temporaryDirectory, "first", "drive_c", "users", "steamuser");
        string second = Path.Combine(temporaryDirectory, "second", "drive_c", "users", "steamuser");
        string firstJournal = Path.Combine(first, "Saved Games", "Frontier Developments", "Elite Dangerous");
        string secondJournal = Path.Combine(second, "Saved Games", "Frontier Developments", "Elite Dangerous");
        var store = new ScreenshotProcessingSettingsStore(Path.Combine(temporaryDirectory, "settings.json"));
        store.Save(store.Load(firstJournal) with { AddBanner = false }, firstJournal);

        ScreenshotProcessingPreferences restored = store.Load(secondJournal);

        Assert.Equal(
            Path.Combine(second, "Pictures", "Frontier Developments", "Elite Dangerous"),
            restored.SourceFolder
        );
        Assert.Equal(
            Path.Combine(second, "Pictures", "Frontier Developments", "Elite Dangerous", "converted"),
            restored.TargetFolder
        );
        Assert.False(restored.AddBanner);

        string customSource = Path.Combine(first, "Pictures", "Frontier Developments", "Elite Dangerous");
        string customTarget = Path.Combine(customSource, "converted");
        store.Save(restored with { SourceFolder = customSource, TargetFolder = customTarget }, secondJournal);
        ScreenshotProcessingPreferences manual = store.Load(firstJournal);
        Assert.Equal(customSource, manual.SourceFolder);
        Assert.Equal(customTarget, manual.TargetFolder);
        store.Save(manual with { AddBanner = true }, firstJournal);
        ScreenshotProcessingPreferences savedAgain = store.Load(secondJournal);
        Assert.Equal(customSource, savedAgain.SourceFolder);
        Assert.Equal(customTarget, savedAgain.TargetFolder);
    }

    [Fact]
    public void LegacyColorObjectIsTranslatedWithoutFailure()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Screenshots\":{\"BannerColor\":{\"A\":255," + "\"R\":18,\"G\":171,\"B\":239}}}");

        ScreenshotProcessingPreferences preferences = new ScreenshotProcessingSettingsStore(path).Load();

        Assert.Equal("#12ABEF", preferences.BannerColor);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
