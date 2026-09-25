using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class OverlayEditorControlsSettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-editor-controls-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public void SavesHeightAlongsideUnrelatedUiSettings()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "ui-settings.json");
        File.WriteAllText(path, "{\"FutureSetting\":true}");
        var store = new OverlayEditorControlsSettingsStore(path);

        Assert.Equal(0, store.LoadHeightPercent());
        store.SaveHeightPercent(-35.5);

        Assert.Equal(-35.5, new OverlayEditorControlsSettingsStore(path).LoadHeightPercent());
        JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.True(root["FutureSetting"]!.GetValue<bool>());
    }

    [Fact]
    public void OutOfRangeSavedHeightIsClampedOnLoad()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "ui-settings.json");
        File.WriteAllText(path, "{\"OverlayEditorControls\":{\"HeightPercent\":500}}");

        Assert.Equal(100, new OverlayEditorControlsSettingsStore(path).LoadHeightPercent());
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
