using SrvSurvey.Desktop.Input;

namespace SrvSurvey.Desktop.Tests.Input;

public sealed class GlobalInputSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-input-settings-tests-{Guid.NewGuid():N}");

    [Fact]
    public void CatalogPreservesEveryLegacyActionAndDefault()
    {
        Assert.Equal(30, GlobalInputActionCatalog.All.Count);
        Assert.Equal(
            GlobalInputActionCatalog.All.Count,
            GlobalInputActionCatalog.All
                .Select(definition => definition.LegacyName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
        Assert.Equal(
            "ALT F2",
            GlobalInputActionCatalog.Get(
                GlobalInputAction.ToggleAllVisibility).DefaultChord);
        Assert.Equal(
            "ALT CTRL I",
            GlobalInputActionCatalog.Get(
                GlobalInputAction.ToggleImageEmbed).DefaultChord);
    }

    [Fact]
    public void SaveAndLoadRoundTripPreservesThemeAndBindings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui.json");
        File.WriteAllText(
            path,
            """
            {
              "Version": 1,
              "Theme": "green-dark"
            }
            """);
        var store = new GlobalInputSettingsStore(path);
        var bindings = GlobalInputSettings.Default.Bindings.ToDictionary();
        bindings[GlobalInputAction.CopyNextBoxel] = "ALT X";

        store.Save(new GlobalInputSettings(
            KeyboardEnabled: true,
            ControllerEnabled: true,
            ControllerDeviceId: "controller-1",
            bindings));
        var loaded = store.Load();

        Assert.True(loaded.KeyboardEnabled);
        Assert.True(loaded.ControllerEnabled);
        Assert.Equal("controller-1", loaded.ControllerDeviceId);
        Assert.Equal(
            "ALT X",
            loaded.Bindings[GlobalInputAction.CopyNextBoxel]);
        Assert.Contains("\"Theme\": \"green-dark\"", File.ReadAllText(path));
    }

    [Fact]
    public void LoadMergesMissingActionsAndIgnoresUnknownEntries()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui.json");
        File.WriteAllText(
            path,
            """
            {
              "Version": 1,
              "Input": {
                "KeyboardEnabled": true,
                "Bindings": {
                  "copyNextBoxel": "SHIFT C",
                  "futureAction": "ALT Z"
                }
              }
            }
            """);

        var loaded = new GlobalInputSettingsStore(path).Load();

        Assert.Equal(
            "SHIFT C",
            loaded.Bindings[GlobalInputAction.CopyNextBoxel]);
        Assert.Equal(
            "ALT F2",
            loaded.Bindings[GlobalInputAction.ToggleAllVisibility]);
        Assert.Equal(30, loaded.Bindings.Count);
    }

    [Fact]
    public void SavePreservesUnknownFutureInputSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui.json");
        File.WriteAllText(
            path,
            """
            {
              "Version": 1,
              "Input": {
                "FutureOption": 42,
                "Bindings": {
                  "futureAction": "ALT Z"
                }
              }
            }
            """);

        new GlobalInputSettingsStore(path).Save(GlobalInputSettings.Default);
        var json = File.ReadAllText(path);

        Assert.Contains("\"FutureOption\": 42", json);
        Assert.Contains("\"futureAction\": \"ALT Z\"", json);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
