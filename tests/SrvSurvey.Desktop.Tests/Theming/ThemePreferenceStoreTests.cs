using SrvSurvey.Desktop.Theming;
using System.Text.Json.Nodes;

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

    [Fact]
    public void WrongSettingTypesAreIgnored()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var settingsPath = Path.Combine(temporaryDirectory, "ui.json");
        File.WriteAllText(
            settingsPath,
            """{"Version":"one","Theme":42}""");

        Assert.Null(new ThemePreferenceStore(settingsPath).LoadThemeKey());
    }

    [Fact]
    public void SavingThemePreservesOtherUiSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var settingsPath = Path.Combine(temporaryDirectory, "ui.json");
        File.WriteAllText(
            settingsPath,
            """
            {
              "Version": 1,
              "Input": {
                "KeyboardEnabled": true
              }
            }
            """);

        new ThemePreferenceStore(settingsPath).SaveThemeKey("blue-dark");

        var root = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject;
        Assert.Equal("blue-dark", root?["Theme"]?.GetValue<string>());
        Assert.True(root?["Input"]?["KeyboardEnabled"]?.GetValue<bool>());
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
