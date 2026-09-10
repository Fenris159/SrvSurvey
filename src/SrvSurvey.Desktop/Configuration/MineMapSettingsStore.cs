using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class MineMapSettingsStore(string path)
{
    private const string SettingsKey = "MineMap";
    private readonly UiSettingsDocumentStore document = new(path);

    public MineMapPreferences Load()
    {
        var settings = document.Load()[SettingsKey] as JsonObject;
        return new MineMapPreferences(
            OnlyShowWhileOnGround: settings?["OnlyShowWhileOnGround"]
                is JsonValue value
                && value.TryGetValue<bool>(out var enabled)
                && enabled);
    }

    public void Save(MineMapPreferences preferences) => document.Update(root =>
    {
        if (root[SettingsKey] is not JsonObject settings)
        {
            settings = [];
            root[SettingsKey] = settings;
        }

        settings["OnlyShowWhileOnGround"] = preferences.OnlyShowWhileOnGround;
    });
}

public sealed record MineMapPreferences(bool OnlyShowWhileOnGround);
