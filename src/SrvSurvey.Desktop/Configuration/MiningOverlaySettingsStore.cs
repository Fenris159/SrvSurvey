using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Desktop.Configuration;

/// <summary>Presentation preferences can be edited without loading a commander's mining history.</summary>
public sealed record MiningOverlayPreferences(
    bool HideInSupercruise = true,
    bool OverlaysOnlyDuringSession = true,
    int NotificationSeconds = 15,
    int PersistentProspectSlots = 4,
    bool HideProspectsBelowThreshold = false,
    MiningSessionYieldSort SessionYieldSort = MiningSessionYieldSort.AveragePercentage,
    int SessionYieldLimit = 5
)
{
    public static MiningOverlayPreferences From(MiningPreferences settings) =>
        new(
            settings.HideInSupercruise,
            settings.OverlaysOnlyDuringSession,
            settings.NotificationSeconds,
            settings.PersistentProspectSlots,
            settings.HideProspectsBelowThreshold,
            settings.SessionYieldSort,
            settings.SessionYieldLimit
        );

    public void ApplyTo(MiningPreferences settings)
    {
        settings.HideInSupercruise = HideInSupercruise;
        settings.OverlaysOnlyDuringSession = OverlaysOnlyDuringSession;
        settings.NotificationSeconds = Math.Clamp(NotificationSeconds, 3, 120);
        settings.PersistentProspectSlots = Math.Clamp(PersistentProspectSlots, 1, 8);
        settings.HideProspectsBelowThreshold = HideProspectsBelowThreshold;
        settings.SessionYieldSort = Enum.IsDefined(SessionYieldSort)
            ? SessionYieldSort
            : MiningSessionYieldSort.AveragePercentage;
        settings.SessionYieldLimit = Math.Clamp(SessionYieldLimit, 1, 100);
    }

    /// <summary>Merges only edited fields so a commander's unrelated preferences remain intact.</summary>
    public MiningOverlayPreferences WithChangesFrom(MiningOverlayPreferences before, MiningOverlayPreferences after) =>
        new(
            before.HideInSupercruise != after.HideInSupercruise ? after.HideInSupercruise : HideInSupercruise,
            before.OverlaysOnlyDuringSession != after.OverlaysOnlyDuringSession
                ? after.OverlaysOnlyDuringSession
                : OverlaysOnlyDuringSession,
            before.NotificationSeconds != after.NotificationSeconds ? after.NotificationSeconds : NotificationSeconds,
            before.PersistentProspectSlots != after.PersistentProspectSlots
                ? after.PersistentProspectSlots
                : PersistentProspectSlots,
            before.HideProspectsBelowThreshold != after.HideProspectsBelowThreshold
                ? after.HideProspectsBelowThreshold
                : HideProspectsBelowThreshold,
            before.SessionYieldSort != after.SessionYieldSort ? after.SessionYieldSort : SessionYieldSort,
            before.SessionYieldLimit != after.SessionYieldLimit ? after.SessionYieldLimit : SessionYieldLimit
        );
}

/// <summary>Keeps commander preferences and a patch of edits made before commander detection.</summary>
public sealed class MiningOverlaySettingsStore(string path)
{
    private const string Section = "MiningOverlays";
    private const string Profiles = "Commanders";
    private const string Pending = "Pending";
    private readonly UiSettingsDocumentStore document = new(path);

    /// <summary>Reads preferences without assigning an unidentified draft or writing the settings file.</summary>
    public MiningOverlayPreferences ReadPreferences(
        string? commander,
        Func<MiningOverlayPreferences> fallback,
        bool includeDraft = true
    )
    {
        commander = string.IsNullOrWhiteSpace(commander) ? null : commander.Trim().ToUpperInvariant();
        return Read(document.Load(), commander, fallback, includeDraft);
    }

    /// <summary>Persists a commander snapshot, optionally completing the assignment of an unidentified draft.</summary>
    public MiningOverlayPreferences SaveCommander(
        string commander,
        MiningOverlayPreferences before,
        MiningOverlayPreferences after,
        bool assignDraft = false,
        bool replaceSnapshot = false
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commander);
        commander = commander.Trim().ToUpperInvariant();
        MiningOverlayPreferences result = after;
        document.Update(root =>
        {
            JsonObject section = GetSection(root);
            JsonObject profiles = GetProfiles(section);
            JsonObject previous = Serialize(before);
            JsonObject updated = Serialize(after);
            JsonObject values = replaceSnapshot
                ? updated.DeepClone().AsObject()
                : (profiles[commander] as JsonObject)?.DeepClone().AsObject() ?? [];
            ApplyEdits(values, previous, updated);
            if (assignDraft)
            {
                if (!replaceSnapshot && section[Pending] is JsonObject pending)
                {
                    MergeDraftEdits(values, previous, updated, pending);
                }
                section.Remove(Pending);
            }
            profiles[commander] = values;
            result = Read(root, commander, () => before, includeDraft: false);
        });
        return result;
    }

    public MiningOverlayPreferences Load(string? commander, Func<MiningOverlayPreferences> fallback)
    {
        commander = string.IsNullOrWhiteSpace(commander) ? null : commander.Trim().ToUpperInvariant();
        JsonObject root = document.Load();
        MiningOverlayPreferences result = Read(root, commander, fallback);
        if (commander is not null && (root[Section] as JsonObject)?[Pending] is JsonObject { Count: > 0 })
        {
            // Consume the unassigned patch atomically with saving the first identified commander's preferences.
            document.Update(current =>
            {
                result = Read(current, commander, fallback);
                JsonObject section = GetSection(current);
                GetProfiles(section)[commander] = Serialize(result);
                section.Remove(Pending);
            });
        }
        return result;
    }

    public void Save(string? commander, MiningOverlayPreferences before, MiningOverlayPreferences after)
    {
        commander = string.IsNullOrWhiteSpace(commander) ? null : commander.Trim().ToUpperInvariant();
        if (before == after)
        {
            return;
        }
        if (commander is not null)
        {
            SaveCommander(commander, before, after);
            return;
        }
        document.Update(root =>
        {
            JsonObject section = GetSection(root);
            JsonObject previous = Serialize(before);
            JsonObject updated = Serialize(after);
            JsonObject pending = section[Pending] as JsonObject ?? [];
            foreach ((string name, JsonNode? value) in updated)
            {
                if (!JsonNode.DeepEquals(previous[name], value))
                {
                    pending[name] = value?.DeepClone();
                }
            }
            section[Pending] = pending;
        });
    }

    private static MiningOverlayPreferences Read(
        JsonObject root,
        string? commander,
        Func<MiningOverlayPreferences> fallback,
        bool includeDraft = true
    )
    {
        var section = root[Section] as JsonObject;
        JsonObject? profile = commander is not null
            ? (section?[Profiles] as JsonObject)?[commander] as JsonObject
            : null;
        JsonObject defaults = Serialize(new());
        JsonObject values =
            profile is not null && defaults.All(property => profile.ContainsKey(property.Key))
                ? profile.DeepClone().AsObject()
                : Serialize(fallback());
        ApplyFields(values, profile);
        if (includeDraft && section?[Pending] is JsonObject pending)
        {
            ApplyFields(values, pending);
        }
        return values.Deserialize<MiningOverlayPreferences>() ?? fallback();
    }

    private static void ApplyFields(JsonObject values, JsonObject? patch)
    {
        if (patch is null)
        {
            return;
        }
        foreach (string name in values.Select(property => property.Key).ToArray())
        {
            if (patch.TryGetPropertyValue(name, out JsonNode? value))
            {
                values[name] = value?.DeepClone();
            }
        }
    }

    private static void ApplyEdits(JsonObject values, JsonObject previous, JsonObject updated)
    {
        foreach ((string name, JsonNode? value) in updated)
        {
            if (!JsonNode.DeepEquals(previous[name], value))
            {
                values[name] = value?.DeepClone();
            }
        }
    }

    private static void MergeDraftEdits(JsonObject values, JsonObject previous, JsonObject updated, JsonObject pending)
    {
        foreach ((string name, JsonNode? value) in pending)
        {
            if (
                updated.ContainsKey(name)
                && (JsonNode.DeepEquals(previous[name], updated[name]) || !JsonNode.DeepEquals(previous[name], value))
            )
            {
                values[name] = value?.DeepClone();
            }
        }
    }

    private static JsonObject GetSection(JsonObject root)
    {
        JsonObject section = root[Section] as JsonObject ?? [];
        root[Section] = section;
        return section;
    }

    private static JsonObject GetProfiles(JsonObject section)
    {
        JsonObject profiles = section[Profiles] as JsonObject ?? [];
        section[Profiles] = profiles;
        return profiles;
    }

    private static JsonObject Serialize(MiningOverlayPreferences value) =>
        JsonSerializer.SerializeToNode(value)!.AsObject();
}
