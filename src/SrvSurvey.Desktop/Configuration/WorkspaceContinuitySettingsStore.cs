using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

/// <summary>Portable workspace selections that can be resumed on another computer.</summary>
internal sealed record WorkspaceContinuityPreferences(
    string Navigation = "overview",
    int MiningTab = 0,
    int SurfaceMiningTab = 0,
    int GuardianTab = 0,
    int FleetCarrierTab = 0
);

/// <summary>Stores workspace navigation independently of machine-dependent window placement.</summary>
internal sealed class WorkspaceContinuitySettingsStore(string path)
{
    private readonly UiSettingsDocumentStore document = new(path);

    /// <summary>Loads known workspace choices, rejecting invalid tab indexes.</summary>
    internal WorkspaceContinuityPreferences Load()
    {
        var workspace = document.Load()["Workspace"] as JsonObject;
        return new(
            workspace?["Navigation"] is JsonValue name && name.TryGetValue<string>(out string? navigation)
                ? navigation
                : "overview",
            ReadIndex(workspace, "MiningTab", 7),
            ReadIndex(workspace, "SurfaceMiningTab", 5),
            ReadIndex(workspace, "GuardianTab", 8),
            ReadIndex(workspace, "FleetCarrierTab", 2)
        );
    }

    /// <summary>Writes the last visible workspace and its selected activity tabs.</summary>
    internal void Save(WorkspaceContinuityPreferences preferences) =>
        document.Update(root =>
            root["Workspace"] = new JsonObject
            {
                ["Navigation"] = preferences.Navigation,
                ["MiningTab"] = preferences.MiningTab,
                ["SurfaceMiningTab"] = preferences.SurfaceMiningTab,
                ["GuardianTab"] = preferences.GuardianTab,
                ["FleetCarrierTab"] = preferences.FleetCarrierTab,
            }
        );

    /// <summary>Uses a safe default when a saved tab is missing or outside its workspace.</summary>
    private static int ReadIndex(JsonObject? workspace, string key, int maximum) =>
        workspace?[key] is JsonValue value && value.TryGetValue<int>(out int index) && index >= 0 && index <= maximum
            ? index
            : 0;
}
