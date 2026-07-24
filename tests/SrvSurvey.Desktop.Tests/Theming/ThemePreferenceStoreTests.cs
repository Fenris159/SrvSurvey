using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.Tests.Theming;

public sealed class ThemePreferenceStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-theme-tests-{Guid.NewGuid():N}");

    [Fact]
    public void SaveAndLoadRoundTrip()
    {
        var settingsPath = Path.Combine(temporaryDirectory, "ui.json");
        var store = new ThemePreferenceStore(settingsPath);

        store.SaveThemeKey("green-light");

        Assert.Equal("green-light", store.LoadThemeKey());
    }

    [Fact]
    public void CorruptSettingsAreIgnored()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var settingsPath = Path.Combine(temporaryDirectory, "ui.json");
        File.WriteAllText(settingsPath, "{not json");

        var store = new ThemePreferenceStore(settingsPath);

        Assert.Null(store.LoadThemeKey());
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
