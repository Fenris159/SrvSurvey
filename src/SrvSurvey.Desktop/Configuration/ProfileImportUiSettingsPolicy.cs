using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

/// <summary>Separates portable player preferences from the current installation's overlay configuration.</summary>
internal static class ProfileImportUiSettingsPolicy
{
    private static readonly HashSet<string> OverlayWorkspaces = new(StringComparer.Ordinal)
    {
        "SystemSurvey",
        "JumpInfo",
        "GalaxyMap",
        "ExplorationPois",
        "BiologyPredictions",
        "BiologyRewards",
        "Combat",
        "FirstFootfallInference",
        "GuardianGestures",
        "HumanSite",
        "MineMap",
        "Notifications",
        "StationInfo",
        "Streaming",
        "SurfaceMining",
        "VirtualReality",
        "WaylandCapture",
        "Input",
    };

    internal static bool IsOverlaySection(string section) =>
        section.Contains("Overlay", StringComparison.OrdinalIgnoreCase) || OverlayWorkspaces.Contains(section);

    /// <summary>Removes incoming overlay preferences, including defaults that the destination has never saved.</summary>
    internal static void ExcludeOverlays(JsonObject incoming)
    {
        foreach (string section in incoming.Select(property => property.Key).Where(IsOverlaySection).ToArray())
        {
            incoming.Remove(section);
        }
        // Colonization also contains delivery recovery data and service preferences.
        // Only its presentation settings belong to the current overlay configuration.
        if (incoming["Colonization"] is JsonObject colonization)
        {
            colonization.Remove("Overlay");
        }
    }

    /// <summary>Counts imported setting values after excluded overlay preferences have been removed.</summary>
    internal static int CountPreferences(JsonObject settings) =>
        settings
            .Where(property => property.Key != "Version")
            .Sum(property => property.Value is JsonObject section ? CountPreferences(section) : 1);
}
