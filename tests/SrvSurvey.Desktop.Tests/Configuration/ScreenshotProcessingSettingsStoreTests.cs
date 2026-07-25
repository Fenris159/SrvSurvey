using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class ScreenshotProcessingSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-screenshot-settings-tests-{Guid.NewGuid():N}");

    [Fact]
    public void SaveRoundTripsAndPreservesUnknownSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(
            path,
            "{\"Future\":42,\"Screenshots\":{\"FutureOption\":true}}");
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
            1700);

        store.Save(preferences);

        Assert.Equal(preferences, store.Load());
        var root = Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(path)));
        Assert.Equal(42, root["Future"]?.GetValue<int>());
    }

    [Fact]
    public void LegacyColorObjectIsTranslatedWithoutFailure()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(
            path,
            "{\"Screenshots\":{\"BannerColor\":{\"A\":255,"
            + "\"R\":18,\"G\":171,\"B\":239}}}");

        var preferences = new ScreenshotProcessingSettingsStore(path).Load();

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
