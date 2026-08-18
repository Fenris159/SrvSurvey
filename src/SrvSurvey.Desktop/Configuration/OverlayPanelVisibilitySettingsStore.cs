using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Configuration;

public sealed class OverlayPanelVisibilitySettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public OverlayPanelVisibilitySettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public IReadOnlyDictionary<string, bool> Load()
    {
        var stored = documentStore.Load()["OverlayPanelVisibility"]
            as JsonObject;
        return OverlayLayoutCatalog.Supported.ToDictionary(
            definition => definition.Name,
            definition => GetBoolean(stored, definition.Name, fallback: true),
            StringComparer.Ordinal);
    }

    public void Save(IReadOnlyDictionary<string, bool> visibility)
    {
        ArgumentNullException.ThrowIfNull(visibility);
        documentStore.Update(root =>
        {
            var settings = root["OverlayPanelVisibility"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["OverlayPanelVisibility"] = settings;
            }

            foreach (var definition in OverlayLayoutCatalog.Supported)
            {
                settings[definition.Name] = visibility.GetValueOrDefault(
                    definition.Name,
                    true);
            }

            root["Version"] = 1;
        });
    }

    private static bool GetBoolean(
        JsonObject? settings,
        string propertyName,
        bool fallback)
    {
        return settings?[propertyName] is JsonValue value
            && value.TryGetValue<bool>(out var result)
                ? result
                : fallback;
    }
}
