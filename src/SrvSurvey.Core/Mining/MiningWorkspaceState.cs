using System.Security.Cryptography;
using System.Text;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Mining;

public sealed record MiningNotice(DateTimeOffset Time, string Kind, string Text);

public sealed class MiningWorkspaceState
{
    private readonly HashSet<string> processed;
    public MiningWorkspaceState(MiningCommanderData data)
    {
        Data = data;
        Session.Restore(data.Current);
        Missions.Missions.AddRange(data.Missions);
        processed = data.ProcessedEvents.ToHashSet(StringComparer.Ordinal);
    }
    public MiningCommanderData Data { get; }
    public MiningSessionTracker Session { get; } = new();
    public MiningMissionTracker Missions { get; } = new();
    public List<MiningNotice> Notices { get; } = [];

    public bool Apply(JournalEventEnvelope entry, bool bootstrap, string system, string body, string ship, GalacticCoordinate? position = null)
    {
        if (!RelevantEvents.Contains(entry.EventName)) return false;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.RawJson)));
        if (!processed.Add(key)) return false;
        Data.ProcessedEvents.Add(key);
        // Bound recovery metadata; older replayed activity is also constrained by the active session start time.
        if (Data.ProcessedEvents.Count > 50000)
        {
            processed.Remove(Data.ProcessedEvents[0]);
            Data.ProcessedEvents.RemoveAt(0);
        }
        var json = entry.Payload;
        if (!bootstrap && Data.Settings.AutoStart && Session.Current is null && entry.Timestamp is { } time
            && entry.EventName == "LaunchDrone" && MiningJson.Text(json, "Type").Equals("Prospector", StringComparison.OrdinalIgnoreCase))
            Session.Start(time, system, body, ship);
        var changed = Session.Apply(entry);
        Missions.Apply(entry);
        ApplyRing(entry, system, position);
        if (changed && !bootstrap) AddNotice(entry);
        Synchronize();
        return true;
    }

    public void CacheRing(MiningRing ring)
    {
        var existing = Data.Rings.Find(r => r.System.Equals(ring.System, StringComparison.OrdinalIgnoreCase) && r.Body.Equals(ring.Body, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && existing.Scanned > ring.Scanned) return;
        if (existing is not null) Data.Rings.Remove(existing);
        Data.Rings.Add(ring);
    }
    public void Import(MiningCommanderData imported)
    {
        foreach (var ring in imported.Rings) CacheRing(ring);
        foreach (var mission in imported.Missions.Where(mission => !Missions.Missions.Any(m => m.Id == mission.Id)))
            Missions.Missions.Add(mission);
        Synchronize();
    }
    public void Synchronize()
    {
        Data.Current = Session.Current;
        Data.Missions.Clear();
        Data.Missions.AddRange(Missions.Missions);
    }

    public void Stop(DateTimeOffset now)
    {
        if (Session.Current is null) return;
        Session.Current.Thresholds = new Dictionary<string, double>(Data.Settings.Thresholds);
        Data.History.Insert(0, Session.Stop(now));
        Synchronize();
    }

    private void AddNotice(JournalEventEnvelope entry)
    {
        var json = entry.Payload;
        var settings = Data.Settings;
        string? text = entry.EventName switch
        {
            "MiningRefined" when settings.NotifyRefined => $"{(MiningJson.Text(json, "Type_Localised") is { Length: > 0 } localized ? localized : MiningJson.Text(json, "Type"))} ×1",
            "MaterialCollected" when settings.NotifyMaterials => $"Collected: {MiningJson.Text(json, "Name")} ×{MiningJson.Number(json, "Count")}",
            "ProspectedAsteroid" when settings.NotifyProspecting => FormatProspect(),
            _ => null,
        };
        if (text is null) return;
        var kind = entry.EventName switch { "MiningRefined" => "Refined", "MaterialCollected" => "Collected", _ => "Prospected" };
        Notices.Insert(0, new MiningNotice(entry.Timestamp ?? DateTimeOffset.UtcNow, kind, text));
        if (Notices.Count > 100) Notices.RemoveAt(Notices.Count - 1);
    }

    private string? FormatProspect()
    {
        var last = Session.Current?.Prospects.LastOrDefault();
        if (last is null || (!string.IsNullOrEmpty(last.Core) ? !Data.Settings.AnnounceCores : !Data.Settings.AnnounceNonCores)) return null;
        var selected = last.Materials.Where(m => Data.Settings.Thresholds.Count == 0
            || Data.Settings.Thresholds.TryGetValue(m.Name.ToLowerInvariant(), out var threshold) && m.Percentage >= threshold).ToArray();
        if (selected.Length == 0 && string.IsNullOrEmpty(last.Core)) return null;
        return string.Join(" · ", selected.Select(m => $"{m.Name} {m.Percentage:0.0}%"))
            + (string.IsNullOrEmpty(last.Core) ? "" : $" · Core: {last.Core}");
    }

    private void ApplyRing(JournalEventEnvelope entry, string system, GalacticCoordinate? position)
    {
        var json = entry.Payload;
        if (string.IsNullOrWhiteSpace(system)) return;
        if (entry.EventName == "Scan")
        {
            foreach (var ring in MiningJson.Array(json, "Rings"))
            {
                var name = MiningJson.Text(ring, "Name");
                var item = GetRing(system, name, position);
                item.RingType = MiningJson.Text(ring, "RingClass").Replace("eRingClass_", "");
                item.Reserve = MiningJson.Text(json, "ReserveLevel");
                item.ArrivalLs = MiningJson.Number(json, "DistanceFromArrivalLS");
                item.Scanned = entry.Timestamp ?? default;
            }
        }
        if (entry.EventName != "SAASignalsFound") return;
        var body = MiningJson.Text(json, "BodyName");
        if (!body.Contains("Ring", StringComparison.OrdinalIgnoreCase)) return;
        var target = GetRing(system, body, position);
        foreach (var signal in MiningJson.Array(json, "Signals"))
        {
            var name = MiningJson.Text(signal, "Type_Localised");
            if (name.Length == 0) name = MiningJson.Text(signal, "Type");
            target.Hotspots[name] = (int)MiningJson.Number(signal, "Count");
        }
        target.Scanned = entry.Timestamp ?? default;
    }

    private MiningRing GetRing(string system, string body, GalacticCoordinate? position)
    {
        var ring = Data.Rings.Find(r => r.System.Equals(system, StringComparison.OrdinalIgnoreCase) && r.Body.Equals(body, StringComparison.OrdinalIgnoreCase));
        if (ring is not null) return ring;
        ring = new MiningRing { System = system, Body = body, Position = position };
        Data.Rings.Add(ring);
        return ring;
    }
    private static readonly HashSet<string> RelevantEvents = ["LaunchDrone", "ProspectedAsteroid", "MiningRefined", "MaterialCollected", "MissionAccepted", "MissionCompleted", "MissionAbandoned", "MissionFailed", "CargoDepot", "Scan", "SAASignalsFound"];
}
