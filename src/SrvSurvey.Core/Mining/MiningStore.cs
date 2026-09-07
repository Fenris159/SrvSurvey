using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SrvSurvey.Core.Mining;

public sealed record MiningPreferences
{
    public bool ReceiveCommunityData { get; set; }
    public MiningSearchPreferences SearchOptions { get; set; } = new();
    public bool AutoStart { get; set; } = true;
    public bool SpeakAnnouncements { get; set; }
    public string Voice { get; set; } = "";
    public int SpeechVolume { get; set; } = 70;
    public int SpeechRate { get; set; }
    public bool AnnounceCores { get; set; } = true;
    public bool AnnounceNonCores { get; set; } = true;
    public Dictionary<string, MiningAnnouncementPreset> AnnouncementPresets { get; set; } = new();
    public bool AutoSwitchTabs { get; set; }
    public bool AutoSearch { get; set; }
    public bool NotifyProspecting { get; set; } = true;
    public bool NotifyRefined { get; set; } = true;
    public bool NotifyMaterials { get; set; } = true;
    public bool NotifyCargoFull { get; set; } = true;
    public bool HideInSupercruise { get; set; } = true;
    public bool OverlaysOnlyDuringSession { get; set; } = true;
    public int NotificationSeconds { get; set; } = 15;
    public string HomeSystem { get; set; } = "";
    public Dictionary<string, double> Thresholds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<MiningFiregroup> Firegroups { get; set; } = [];
}
public sealed record MiningAnnouncementPreset(Dictionary<string, double> Thresholds, bool Cores, bool NonCores);

public sealed record MiningFiregroup(int Group, string Primary, string Secondary);

public sealed record MiningCommanderData
{
    public int SchemaVersion { get; init; } = 1;
    public MiningSession? Current { get; set; }
    public List<MiningSession> History { get; init; } = [];
    public List<MiningMission> Missions { get; init; } = [];
    public MiningPreferences Settings { get; init; } = new();
    public List<string> ProcessedEvents { get; init; } = [];
    public List<MiningRing> Rings { get; init; } = [];
}

public sealed record MiningRing
{
    public string System { get; init; } = "";
    public string Body { get; init; } = "";
    public string RingType { get; set; } = "";
    public string Reserve { get; set; } = "";
    public double? ArrivalLs { get; set; }
    public double? DistanceLy { get; init; }
    public Search.GalacticCoordinate? Position { get; set; }
    public Dictionary<string, int> Hotspots { get; set; } = new();
    public DateTimeOffset Scanned { get; set; }
    public string Source { get; init; } = "Journal";
    public string Overlaps { get; init; } = "";
    public string ResourceExtractionSites { get; init; } = "";
    public string Power { get; init; } = "";
    public string Minerals => string.Join(", ", Hotspots.Select(p => $"{p.Key} ×{p.Value}"));
}

/// <summary>Commander-scoped mining history and recovery, stored independently of overlay configuration.</summary>
public sealed class MiningStore(string directory)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public MiningCommanderData Load(string commander)
    {
        var path = GetPath(commander);
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : new();
    }
    public static string Export(MiningCommanderData state) => JsonSerializer.Serialize(state, Options);
    public void Save(string commander, MiningCommanderData state)
    {
        var path = GetPath(commander);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, Export(state));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public MiningCommanderData Restore(string commander, string json)
    {
        var state = Parse(json);
        // Retain the pre-restore state for recovery even when an otherwise valid backup was selected accidentally.
        var path = GetPath(commander);
        if (File.Exists(path)) File.Copy(path, path + ".before-restore", true);
        Save(commander, state);
        return state;
    }
    private string GetPath(string commander)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commander);
        return Path.Combine(directory, "mining", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(commander)))[..24] + ".json");
    }
    public static MiningCommanderData Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("SchemaVersion", out var version)
            || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1) throw new JsonException("Unsupported mining backup format.");
        var state = JsonSerializer.Deserialize<MiningCommanderData>(json, Options) ?? throw new JsonException("Empty mining backup.");
        if (state.History is null || state.Missions is null || state.Settings is null || state.Rings is null || state.ProcessedEvents is null
            || state.Settings.SearchOptions is null || state.Settings.Thresholds is null || state.Settings.Firegroups is null || state.Settings.AnnouncementPresets is null) throw new JsonException("Incomplete mining backup.");
        if (state.History.Any(InvalidSession) || (state.Current is { } current && InvalidSession(current))
            || state.Rings.Any(r => r is null || r.Hotspots is null || r.System is null || r.Body is null)
            || state.Missions.Any(m => m is null || m.Commodity is null)) throw new JsonException("Invalid records in mining backup.");
        ValidateSettings(state.Settings);
        return state;
    }
    private static void ValidateSettings(MiningPreferences settings)
    {
        if (settings.AnnouncementPresets.Values.Any(p => p is null || p.Thresholds is null || p.Thresholds.Values.Any(v => !double.IsFinite(v) || v is < 0 or > 100))
            || settings.Thresholds.Values.Any(v => !double.IsFinite(v) || v is < 0 or > 100)
            || settings.Firegroups.Any(g => g is null || g.Group is < 0 or > 7 || g.Primary is null || g.Secondary is null))
            throw new JsonException("Invalid mining settings in backup.");
    }
    private static bool InvalidSession(MiningSession s) => s is null || s.System is null || s.Ring is null || s.Notes is null
        || s.Thresholds is null || s.QualityAdjustments is null || s.Prospects is null || s.Collections is null || s.Screenshots is null || s.RefineryEstimates is null
        || (s.Imported is { } imported && (imported.Fields is null || !double.IsFinite(imported.Tons) || imported.Tons < 0))
        || s.Screenshots.Any(string.IsNullOrWhiteSpace)
        || s.Prospects.Any(p => p is null || p.Materials is null || p.Materials.Any(m => m is null || m.Name is null || !double.IsFinite(m.Percentage) || m.Percentage is < 0 or > 100))
        || s.Collections.Any(c => c is null || c.Name is null || c.Count < 0)
        || s.RefineryEstimates.Values.Any(v => !double.IsFinite(v) || v is < 0 or > 16);
}
