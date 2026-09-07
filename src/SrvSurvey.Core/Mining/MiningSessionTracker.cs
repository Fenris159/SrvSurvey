using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Mining;

public sealed record MiningMaterial(string Name, double Percentage);
public sealed record MiningProspect(DateTimeOffset Time, IReadOnlyList<MiningMaterial> Materials, string Core, string Content)
{
    public string MineralSummary => string.Join(" · ", Materials.Select(m => $"{m.Name} {m.Percentage:0.0}%"));
}
public sealed record MiningCollection(DateTimeOffset Time, string Name, int Count, bool Engineering)
{
    public int? Grade => Engineering ? Name.ToLowerInvariant() switch
    {
        "carbon" or "iron" or "lead" or "nickel" or "phosphorus" or "rhenium" or "sulphur" => 1,
        "arsenic" or "chromium" or "germanium" or "manganese" or "vanadium" or "zinc" or "zirconium" => 2,
        "boron" or "cadmium" or "mercury" or "molybdenum" or "niobium" or "tin" or "tungsten" => 3,
        "antimony" or "polonium" or "ruthenium" or "selenium" or "technetium" or "tellurium" or "yttrium" => 4,
        _ => null,
    } : null;
}

public sealed record MiningSession
{
    public MiningImportedReport? Imported { get; init; }
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset? LastObserved { get; set; }
    public DateTimeOffset Started { get; init; }
    public DateTimeOffset? Ended { get; set; }
    public DateTimeOffset? PausedAt { get; set; }
    public TimeSpan PausedDuration { get; set; }
    public string System { get; set; } = "";
    public string Ring { get; set; } = "";
    public string Ship { get; init; } = "";
    public string Notes { get; set; } = "";
    public Dictionary<string, double> Thresholds { get; set; } = new();
    public Dictionary<string, int> QualityAdjustments { get; init; } = new();
    public Dictionary<string, double> RefineryEstimates { get; init; } = new();
    public List<string> Screenshots { get; init; } = [];
    public List<MiningProspect> Prospects { get; init; } = [];
    public List<MiningCollection> Collections { get; init; } = [];
    public int ProspectorLimpets { get; set; }
    public int CollectorLimpets { get; set; }
    public int AsteroidAdjustment { get; set; }
    public double RefinedTons => (Imported?.Tons ?? 0) + Collections.Where(item => !item.Engineering).Sum(item => item.Count);
    public int EngineeringCount => (Imported?.Engineering ?? 0) + Collections.Where(item => item.Engineering).Sum(item => item.Count);
    public int Asteroids => Math.Max(0, (Imported?.Asteroids ?? 0) + Prospects.Count + AsteroidAdjustment);
    public int CoreHits => (Imported?.Cores ?? 0) + Prospects.Count(item => !string.IsNullOrEmpty(item.Core));
    public TimeSpan ActiveDuration => DurationAt(Ended ?? DateTimeOffset.UtcNow);
    public double TonsPerHour => ActiveDuration.TotalHours > 0 ? RefinedTons / ActiveDuration.TotalHours : 0;
    public double TonsPerAsteroid => Asteroids > 0 ? (double)RefinedTons / Asteroids : 0;
    public TimeSpan DurationAt(DateTimeOffset now) => TimeSpan.FromTicks(Math.Max(0,
        ((PausedAt ?? Ended ?? now) - Started - PausedDuration).Ticks));

    public IReadOnlyList<MiningMaterialSummary> Summarize(IReadOnlyDictionary<string, double> thresholds) =>
        Prospects.SelectMany(p => p.Materials).GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new MiningMaterialSummary(group.Key, group.Count(),
                Math.Clamp(group.Count(m => m.Percentage >= thresholds.FirstOrDefault(p => p.Key.Equals(group.Key, StringComparison.OrdinalIgnoreCase)).Value) + QualityAdjustments.GetValueOrDefault(group.Key.ToLowerInvariant()), 0, group.Count()),
                group.Average(m => m.Percentage), group.Max(m => m.Percentage),
                Collections.Where(c => !c.Engineering && c.Name.Equals(group.Key, StringComparison.OrdinalIgnoreCase)).Sum(c => c.Count)))
            .OrderByDescending(item => item.Average).ToArray();
}

public sealed record MiningMaterialSummary(string Name, int Finds, int QualityHits, double Average, double Best, int Tons);

/// <summary>Session accounting accepts the existing journal feed; cargo inventory remains owned by CargoInventoryState.</summary>
public sealed class MiningSessionTracker
{
    public MiningSession? Current { get; private set; }
    public void Restore(MiningSession? session) => Current = session is { Ended: null } ? session : null;

    public void Start(DateTimeOffset now, string system, string ring, string ship)
    {
        if (Current is not null) return;
        Current = new MiningSession { Started = now, System = system, Ring = ring, Ship = ship };
    }

    public void Pause(DateTimeOffset now)
    {
        if (Current is { PausedAt: null } session) session.PausedAt = now;
    }

    public void Resume(DateTimeOffset now)
    {
        if (Current is not { PausedAt: { } paused } session) return;
        session.PausedDuration += now > paused ? now - paused : TimeSpan.Zero;
        session.PausedAt = null;
    }

    public MiningSession Stop(DateTimeOffset now)
    {
        var session = Current ?? throw new InvalidOperationException("No mining session is active.");
        Resume(now);
        session.Ended = now;
        Current = null;
        return session;
    }

    public bool Apply(JournalEventEnvelope entry)
    {
        if (Current is not { PausedAt: null } session || entry.Timestamp is not { } time || time < session.Started) return false;
        var data = entry.Payload;
        switch (entry.EventName)
        {
            case "LaunchDrone":
                var type = MiningJson.Text(data, "Type");
                if (type.Equals("Prospector", StringComparison.OrdinalIgnoreCase)) session.ProspectorLimpets++;
                else if (type.Equals("Collection", StringComparison.OrdinalIgnoreCase)) session.CollectorLimpets++;
                else return false;
                break;
            case "ProspectedAsteroid":
                // A depleted asteroid can be re-targeted; it is not a fresh prospect.
                if (data.TryGetProperty("Remaining", out var remaining) && remaining.TryGetDouble(out var value) && value < 100) return false;
                var materials = MiningJson.Array(data, "Materials")
                    .Select(m => new MiningMaterial(MiningJson.Text(m, "Name"), MiningJson.Number(m, "Proportion")))
                    .Where(m => m.Name.Length > 0 && m.Percentage is >= 0 and <= 100).ToArray();
                session.Prospects.Add(new MiningProspect(time, materials, MiningJson.Text(data, "MotherlodeMaterial"), MiningJson.Text(data, "Content")));
                break;
            case "MiningRefined":
                session.Collections.Add(new MiningCollection(time, MiningJson.Text(data, "Type"), 1, false));
                break;
            case "MaterialCollected":
                if (!MiningJson.Text(data, "Category").Equals("Raw", StringComparison.OrdinalIgnoreCase)) return false;
                session.Collections.Add(new MiningCollection(time, MiningJson.Text(data, "Name"), (int)MiningJson.Number(data, "Count"), true));
                break;
            default:
                return false;
        }
        return true;
    }
}

internal static class MiningJson
{
    public static string Text(JsonElement data, string name) => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    public static double Number(JsonElement data, string name) => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : 0;
    public static IEnumerable<JsonElement> Array(JsonElement data, string name) => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];
}
