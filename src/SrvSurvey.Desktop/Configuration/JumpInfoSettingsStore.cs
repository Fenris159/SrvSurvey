using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class JumpInfoSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public JumpInfoSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public JumpInfoPreferences Load()
    {
        var root = documentStore.Load();
        var jumpInfo = root["JumpInfo"] as JsonObject;
        var defaults = JumpInfoPreferences.Default;
        return new JumpInfoPreferences(
            GetBoolean(jumpInfo, "AutoShow", defaults.AutoShow),
            GetBoolean(jumpInfo, "Minimal", defaults.Minimal),
            GetBoolean(
                jumpInfo,
                "ShowWhenNextHopSelected",
                defaults.ShowWhenNextHopSelected),
            GetBoolean(
                jumpInfo,
                "UseSpanshLastUpdated",
                defaults.UseSpanshLastUpdated));
    }

    public void Save(JumpInfoPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            var jumpInfo = root["JumpInfo"] as JsonObject;
            if (jumpInfo is null)
            {
                jumpInfo = [];
                root["JumpInfo"] = jumpInfo;
            }

            root["Version"] = 1;
            jumpInfo["AutoShow"] = preferences.AutoShow;
            jumpInfo["Minimal"] = preferences.Minimal;
            jumpInfo["ShowWhenNextHopSelected"] =
                preferences.ShowWhenNextHopSelected;
            jumpInfo["UseSpanshLastUpdated"] =
                preferences.UseSpanshLastUpdated;
        });
    }

    private static bool GetBoolean(
        JsonObject? source,
        string propertyName,
        bool fallback)
    {
        return source?[propertyName] is JsonValue value
            && value.TryGetValue<bool>(out var result)
                ? result
                : fallback;
    }
}

public sealed record JumpInfoPreferences(
    bool AutoShow,
    bool Minimal,
    bool ShowWhenNextHopSelected,
    bool UseSpanshLastUpdated = false)
{
    public static JumpInfoPreferences Default { get; } = new(
        AutoShow: true,
        Minimal: false,
        ShowWhenNextHopSelected: false,
        UseSpanshLastUpdated: false);
}
