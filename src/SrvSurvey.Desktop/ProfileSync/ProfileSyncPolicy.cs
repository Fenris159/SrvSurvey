using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SrvSurvey.Desktop.ProfileSync;

/// <summary>Defines an explicit boundary between portable data, machine settings, and excluded credentials.</summary>
internal static partial class ProfileSyncPolicy
{
    private const string ScreenshotsProperty = "Screenshots";
    private static readonly string[] ScreenshotFields = [ScreenshotsProperty, "screenshots"];
    private static readonly HashSet<string> UiSections = new(StringComparer.Ordinal)
    {
        "Version",
        "Theme",
        "Localization",
        "OverlayBehavior",
        "OverlayPanelVisibility",
        "OverlayVehicleAllowLists",
        "DesktopBehavior",
        "Input",
        "OverlayScale",
        "OverlayEditorControls",
        "SystemSurvey",
        "JumpInfo",
        "BiologyPredictions",
        "BiologyRewards",
        "BoxelSurveyStats",
        "Combat",
        "Travel",
        "FiregroupsOverlay",
        "FirstFootfallInference",
        "GalaxyMap",
        "GuardianGestures",
        "GuardianOverlays",
        "HumanSite",
        "MineMap",
        "Notifications",
        "PulseOverlay",
        "Quests",
        "Screenshots",
        "StationInfo",
        "Streaming",
        "SurfaceMining",
        "SystemNicknames",
        "VirtualReality",
        "WaylandCapture",
        "CodexImages",
        "Colonization",
        "ReleaseUpdates",
        "Workspace",
        "MiningReferenceCommodities",
        "NetworkPrivacy",
    };
    private static readonly HashSet<string> MachineSections = new(StringComparer.Ordinal)
    {
        "NetworkPrivacy",
        "WaylandCapture",
        "VirtualReality",
        "FirstFootfallInference",
        "OverlayScale",
        "OverlayEditorControls",
    };
    private static readonly HashSet<string> MachineFields = new(StringComparer.Ordinal)
    {
        "PreferredMonitor",
        "LockToMonitor",
        "BypassWindowManagement",
        "ApplicationWindowPosition",
        "ApplicationWindowScalePercent",
        "ControllerDeviceId",
        "KeyboardSource",
        "Detection",
        "FssTuningDetector",
        "SourceFolder",
        "TargetFolder",
        "CacheDirectory",
        "LocalFloraDirectory",
        "Voice",
    };
    private static readonly HashSet<string> DataDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "systems",
        "guardian",
        "journey",
        "routes",
        "savedBoxelSearches",
        "mining",
        "firegroups",
        "boxelSurveyStats",
        "mining-search-cache",
    };
    private static readonly HashSet<string> OverlayFiles = new(StringComparer.Ordinal)
    {
        "theme.json",
        "overlay-theme-states.json",
        "overlay-typography-overrides.json",
        "plotters.json",
        "overlay-scale-overrides.json",
        "overlay-size-overrides.json",
        "overlay-position-references.json",
        "settings.json",
    };

    /// <summary>Accepts only documented profile files and rejects paths that could escape their root.</summary>
    internal static bool AllowsFile(string area, string relative)
    {
        string[] parts = relative.Split('/');
        if (
            parts.Any(part =>
                part.Length == 0
                || part is "." or ".."
                || part.Any(char.IsControl)
                || part.Contains(':')
                || part.Contains('\\')
            ) || (!relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !IsSearchSelection(relative))
        )
        {
            return false;
        }
        if (area == "config")
        {
            return relative == "cross-platform-ui.json" || IsCommanderUiFile(parts);
        }
        if (area != "data" || parts.Any(part => part is ".trash" or "attachments" or "legacy"))
        {
            return false;
        }
        return IsRootDataFile(parts, relative)
            || (
                parts.Length == 4
                && parts[0] == "commanders"
                && FrontierId().IsMatch(parts[1])
                && parts[2] == "overlays"
                && OverlayFiles.Contains(parts[3])
            )
            || (parts.Length > 1 && DataDirectories.Contains(parts[0]));
    }

    /// <summary>Recognizes portable root documents and hardware overlay files.</summary>
    private static bool IsRootDataFile(string[] parts, string relative) =>
        parts.Length == 1
        && (
            relative is "bookmarks.json" or "surface-mining-survey-progress.json"
            || OverlayFiles.Contains(relative)
            || CodexFile().IsMatch(relative)
        );

    /// <summary>Recognizes an isolated commander's portable preference document.</summary>
    private static bool IsCommanderUiFile(string[] parts) =>
        parts.Length == 3
        && parts[0] == "commanders"
        && FrontierId().IsMatch(parts[1])
        && parts[2] == "cross-platform-ui.json";

    /// <summary>Recognizes the last-used filter keys required to restore saved search presentations.</summary>
    internal static bool IsSearchSelection(string relative) =>
        relative.StartsWith("mining-search-cache/", StringComparison.Ordinal)
        && relative.EndsWith("-last.txt", StringComparison.Ordinal)
        && relative.Split('/').Length == 2;

    /// <summary>Prunes unrelated caches, logs and credentials before walking a profile tree.</summary>
    internal static bool AllowsDirectory(string area, string relative)
    {
        string[] parts = relative.Split('/');
        if (parts.Any(part => part is ".trash" or "attachments" or "legacy"))
        {
            return false;
        }

        if (parts[0] == "commanders")
        {
            return parts.Length <= 3;
        }

        return area == "data" && DataDirectories.Contains(parts[0]);
    }

    /// <summary>Removes machine-specific attachment references inside atomic bookmark and history arrays.</summary>
    internal static void RemoveLocalReferencesFromArrays(JsonNode? node, bool insideArray = false)
    {
        if (node is JsonArray array)
        {
            foreach (JsonNode? child in array)
            {
                RemoveLocalReferencesFromArrays(child, insideArray: true);
            }
        }
        else if (node is JsonObject obj)
        {
            foreach (string key in obj.Select(pair => pair.Key).ToArray())
            {
                if (insideArray && IsLocalArrayReference(key, obj[key]))
                {
                    obj.Remove(key);
                }
                else
                {
                    RemoveLocalReferencesFromArrays(obj[key], insideArray);
                }
            }
        }
    }

    /// <summary>Distinguishes local attachment lists from numeric journey screenshot counters.</summary>
    private static bool IsLocalArrayReference(string key, JsonNode? value) =>
        (ScreenshotFields.Contains(key, StringComparer.Ordinal) && value is JsonArray)
        || key is "JournalPath" or "StartingJournal";

    /// <summary>Retains this computer's screenshot links when a portable collection replaces matching records.</summary>
    internal static void PreserveLocalAttachments(JsonNode? previous, JsonNode? incoming)
    {
        if (previous is JsonObject oldObject && incoming is JsonObject newObject)
        {
            PreserveObjectAttachments(oldObject, newObject);
        }
        else if (previous is JsonArray oldArray && incoming is JsonArray newArray)
        {
            foreach (JsonObject item in newArray.OfType<JsonObject>())
            {
                JsonNode? id = item["Id"] ?? item["id"];
                if (id is not null)
                {
                    JsonObject? original = oldArray
                        .OfType<JsonObject>()
                        .FirstOrDefault(old => JsonNode.DeepEquals(old["Id"] ?? old["id"], id));
                    PreserveLocalAttachments(original, item);
                }
            }
        }
    }

    /// <summary>Preserves local screenshot lists while recursively inspecting nested record collections.</summary>
    private static void PreserveObjectAttachments(JsonObject previous, JsonObject incoming)
    {
        foreach ((string name, JsonNode? child) in incoming.ToArray())
        {
            PreserveLocalAttachments(previous[name], child);
        }
        foreach (string field in ScreenshotFields)
        {
            if (!incoming.ContainsKey(field) && previous[field] is JsonArray screenshots)
            {
                incoming[field] = screenshots.DeepClone();
            }
        }
    }

    /// <summary>Classifies each JSON leaf; null excludes operational state and credentials from all backups.</summary>
    internal static bool? IsMachineValue(string area, string relative, string pointer)
    {
        string[] fields = pointer.Split('/').Skip(1).Select(Unescape).ToArray();
        string file = relative.Split('/')[^1];
        if (area == "config")
        {
            return ClassifyUiValue(fields);
        }
        if (file == "settings.json")
        {
            if (
                fields.Length == 0
                || fields[0] is not ("targetLatLongActive" or "targetLatLong" or "systemNotesTopMost")
            )
            {
                return null;
            }

            return false;
        }
        if (
            file
            is "plotters.json"
                or "overlay-scale-overrides.json"
                or "overlay-size-overrides.json"
                or "overlay-position-references.json"
        )
        {
            return true;
        }

        return (
                fields.Any(field => field is ScreenshotsProperty or "screenshots" or "JournalPath" or "StartingJournal")
                && !fields.Any(field => field is "Counts" or "counts")
            )
            || (
                relative.StartsWith("mining/", StringComparison.Ordinal)
                && fields.Contains("Voice", StringComparer.Ordinal)
            );
    }

    /// <summary>Allows portable UI preferences while keeping credentials and API operation queues out of all snapshots.</summary>
    private static bool? ClassifyUiValue(string[] fields)
    {
        if (fields.Length == 0 || !UiSections.Contains(fields[0]))
        {
            return null;
        }

        if (
            fields[0] == "Colonization"
            && (
                fields.Length < 2
                || fields[1]
                    is not ("Enabled" or "Overlay" or "FleetCarrierCargoSyncEnabled" or "ShipCargoPublishingEnabled")
            )
        )
        {
            return null;
        }

        return MachineSections.Contains(fields[0]) || fields.Any(MachineFields.Contains);
    }

    /// <summary>Formats a file and JSON pointer as an unambiguous sync key.</summary>
    internal static string Key(string area, string relative, string pointer) =>
        $"{area}/{Uri.EscapeDataString(relative)}#{pointer}";

    /// <summary>Validates keys from untrusted backups using the same policy as locally captured files.</summary>
    internal static (string Area, string Relative, string Pointer) ParseKey(string key, bool machine)
    {
        int slash = key.IndexOf('/');
        int hash = key.IndexOf('#');
        if (slash < 1 || hash <= slash)
        {
            throw new InvalidDataException("The backup contains an invalid data key.");
        }

        string area = key[..slash];
        string relative = Uri.UnescapeDataString(key[(slash + 1)..hash]);
        string pointer = key[(hash + 1)..];
        if (
            !AllowsFile(area, relative)
            || (pointer.Length > 0 && !pointer.StartsWith('/'))
            || pointer.Split('/').Skip(1).Any(segment => segment.Length == 0)
            || IsMachineValue(area, relative, pointer) != machine
        )
        {
            throw new InvalidDataException("The backup contains data outside its permitted scope.");
        }
        return (area, relative, pointer);
    }

    /// <summary>Escapes a JSON pointer segment without confusing slashes in property names.</summary>
    internal static string Escape(string value) =>
        value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    /// <summary>Decodes a JSON pointer segment.</summary>
    internal static string Unescape(string value) =>
        value.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);

    /// <summary>Flattens JSON objects to independent settings while treating arrays as complete collections and omitting empty containers.</summary>
    internal static IEnumerable<(string Pointer, JsonNode? Value)> Flatten(JsonNode? node, string pointer = "")
    {
        if (node is JsonObject obj)
        {
            foreach ((string name, JsonNode? value) in obj)
            {
                foreach ((string Pointer, JsonNode? Value) leaf in Flatten(value, pointer + "/" + Escape(name)))
                {
                    yield return leaf;
                }
            }
        }
        else
        {
            yield return (pointer, node);
        }
    }

    /// <summary>Recognizes commander IDs rather than accepting arbitrary directory names.</summary>
    [GeneratedRegex("^F[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex FrontierId();

    /// <summary>Includes collected codex records, excluding live journal and API state files.</summary>
    [GeneratedRegex("^F[0-9]+-codex(-[0-9]+)?\\.json$", RegexOptions.CultureInvariant)]
    private static partial Regex CodexFile();
}
