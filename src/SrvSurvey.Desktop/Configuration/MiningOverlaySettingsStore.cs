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
        settings.NotificationSeconds = NotificationSeconds;
        settings.PersistentProspectSlots = Math.Clamp(PersistentProspectSlots, 1, 8);
        settings.HideProspectsBelowThreshold = HideProspectsBelowThreshold;
        settings.SessionYieldSort = Enum.IsDefined(SessionYieldSort)
            ? SessionYieldSort
            : MiningSessionYieldSort.AveragePercentage;
        settings.SessionYieldLimit = Math.Clamp(SessionYieldLimit, 1, 100);
    }
}

/// <summary>Keeps commander preferences and a patch of edits made before commander detection.</summary>
public sealed class MiningOverlaySettingsStore(string path)
{
    private const string Section = "MiningOverlays";
    private const string Profiles = "Commanders";
    private const string Pending = "Pending";
    private readonly UiSettingsDocumentStore document = new(path);

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
        document.Update(root =>
        {
            JsonObject section = GetSection(root);
            if (commander is not null)
            {
                GetProfiles(section)[commander] = Serialize(after);
                return;
            }
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
        Func<MiningOverlayPreferences> fallback
    )
    {
        var section = root[Section] as JsonObject;
        MiningOverlayPreferences saved =
            commander is not null && (section?[Profiles] as JsonObject)?[commander] is JsonObject profile
                ? profile.Deserialize<MiningOverlayPreferences>() ?? fallback()
                : fallback();
        JsonObject values = Serialize(saved);
        if (section?[Pending] is JsonObject pending)
        {
            foreach (string name in values.Select(property => property.Key).ToArray())
            {
                if (pending.TryGetPropertyValue(name, out JsonNode? value))
                {
                    values[name] = value?.DeepClone();
                }
            }
        }
        return values.Deserialize<MiningOverlayPreferences>() ?? saved;
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
