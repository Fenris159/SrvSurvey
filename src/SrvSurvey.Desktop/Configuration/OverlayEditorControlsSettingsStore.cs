using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class OverlayEditorControlsSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public OverlayEditorControlsSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public double LoadHeightPercent()
    {
        var settings = documentStore.Load()["OverlayEditorControls"] as JsonObject;
        return
            settings?["HeightPercent"] is JsonValue value
            && value.TryGetValue<double>(out double percent)
            && double.IsFinite(percent)
            ? Math.Clamp(percent, -100, 100)
            : 0;
    }

    public void SaveHeightPercent(double percent)
    {
        if (!double.IsFinite(percent))
        {
            throw new ArgumentOutOfRangeException(nameof(percent));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(percent, -100);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);

        documentStore.Update(root =>
        {
            root["Version"] = 1;
            var settings = root["OverlayEditorControls"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["OverlayEditorControls"] = settings;
            }

            settings["HeightPercent"] = percent;
        });
    }
}
