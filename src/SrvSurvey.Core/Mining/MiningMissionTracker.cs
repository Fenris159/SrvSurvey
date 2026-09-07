using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Mining;

public sealed record MiningMission
{
    public long Id { get; init; }
    public string Commodity { get; init; } = "";
    public string Title { get; init; } = "";
    public string Destination { get; init; } = "";
    public int Required { get; init; }
    public int Delivered { get; set; }
    public int OnBoard { get; set; }
    public long Reward { get; init; }
    public string Expiry { get; init; } = "";
    public string Status { get; set; } = "Active";
    public int Remaining => Math.Max(0, Required - Delivered - OnBoard);
}

public sealed class MiningMissionTracker
{
    public List<MiningMission> Missions { get; } = [];

    public bool Apply(JournalEventEnvelope entry)
    {
        var data = entry.Payload;
        if (!data.TryGetProperty("MissionID", out var idValue) || !idValue.TryGetInt64(out var id)) return false;
        var existing = Missions.Find(m => m.Id == id);
        if (entry.EventName == "MissionAccepted")
        {
            if (existing is not null || !MiningJson.Text(data, "Name").Contains("Mining", StringComparison.OrdinalIgnoreCase)) return false;
            Missions.Add(new MiningMission
            {
                Id = id,
                Commodity = NormalizeCommodity(MiningJson.Text(data, "Commodity")),
                Title = MiningJson.Text(data, "LocalisedName"),
                Required = (int)MiningJson.Number(data, "Count"),
                Reward = (long)MiningJson.Number(data, "Reward"),
                Expiry = MiningJson.Text(data, "Expiry"),
                Destination = MiningJson.Text(data, "DestinationSystem") + " / " + MiningJson.Text(data, "DestinationStation"),
            });
            return true;
        }
        if (existing is null) return false;
        switch (entry.EventName)
        {
            case "CargoDepot" when MiningJson.Text(data, "UpdateType") == "Deliver":
                existing.Delivered = Math.Clamp((int)MiningJson.Number(data, "ItemsDelivered"), 0, existing.Required);
                break;
            case "MissionCompleted": existing.Status = "Completed"; break;
            case "MissionAbandoned": existing.Status = "Abandoned"; break;
            case "MissionFailed": existing.Status = "Failed"; break;
            default: return false;
        }
        return true;
    }

    public void UpdateCargo(IEnumerable<CargoItem> cargo)
    {
        var available = cargo.GroupBy(item => NormalizeCommodity(item.Name)).ToDictionary(g => g.Key, g => g.Sum(i => i.Count));
        foreach (var mission in Missions)
        {
            mission.OnBoard = mission.Status == "Active" ? Math.Min(Math.Max(0, mission.Required - mission.Delivered), available.GetValueOrDefault(mission.Commodity)) : 0;
            available[mission.Commodity] = available.GetValueOrDefault(mission.Commodity) - mission.OnBoard;
        }
    }

    private static string NormalizeCommodity(string name) => name.Trim().TrimStart('$').Replace("_name;", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
}
