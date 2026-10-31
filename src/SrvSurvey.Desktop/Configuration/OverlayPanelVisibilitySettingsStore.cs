using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Configuration;

public sealed class OverlayPanelVisibilitySettingsStore
{
    private const string VisibilityPropertyName = "OverlayPanelVisibility";
    private readonly UiSettingsDocumentStore documentStore;

    public OverlayPanelVisibilitySettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public IReadOnlyDictionary<string, bool> Load()
    {
        var stored = documentStore.Load()[VisibilityPropertyName] as JsonObject;
        return OverlayLayoutCatalog.Supported.ToDictionary(
            definition => definition.Name,
            definition => GetBoolean(stored, definition.Name, fallback: true),
            StringComparer.Ordinal
        );
    }

    /// <summary>Distinguishes an explicit saved panel preference from the default availability.</summary>
    internal bool HasSavedVisibility(string plotterName) =>
        documentStore.Load()[VisibilityPropertyName] is JsonObject settings
        && settings[plotterName] is JsonValue value
        && value.TryGetValue<bool>(out _);

    public void Save(IReadOnlyDictionary<string, bool> visibility)
    {
        ArgumentNullException.ThrowIfNull(visibility);
        documentStore.Update(root =>
        {
            var settings = root[VisibilityPropertyName] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root[VisibilityPropertyName] = settings;
            }

            foreach (string? plotterName in OverlayLayoutCatalog.Supported.Select(definition => definition.Name))
            {
                settings[plotterName] = visibility.GetValueOrDefault(plotterName, true);
            }

            root["Version"] = 1;
        });
    }

    private static bool GetBoolean(JsonObject? settings, string propertyName, bool fallback)
    {
        return settings?[propertyName] is JsonValue value && value.TryGetValue<bool>(out bool result)
            ? result
            : fallback;
    }
}
