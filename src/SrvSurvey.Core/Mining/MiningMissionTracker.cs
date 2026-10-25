using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Mining;

public sealed record MiningMission
{
    public long Id { get; init; }
    public string Commodity { get; init; } = "";
    public string Title { get; init; } = "";
    public string Destination { get; init; } = "";
    public string DestinationSystem { get; init; } = "";
    public string DestinationStation { get; init; } = "";
    public bool IsTracked { get; set; } = true;
    public int Required { get; init; }
    public int Delivered { get; set; }
    public int OnBoard { get; set; }
    public long Reward { get; init; }
    public string Expiry { get; init; } = "";
    public string Status { get; set; } = "Active";
    public int Remaining => Math.Max(0, Required - Delivered - OnBoard);

    public bool IsActiveAt(DateTimeOffset now) =>
        Status == "Active"
        && (
            !DateTimeOffset.TryParse(
                Expiry,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTimeOffset expiry
            )
            || expiry > now
        );
}

public sealed class MiningMissionTracker
{
    public List<MiningMission> Missions { get; } = [];

    public bool Apply(JournalEventEnvelope entry, string system = "", string station = "")
    {
        JsonElement data = entry.Payload;
        if (
            !data.TryGetProperty("MissionID", out JsonElement idValue)
            || idValue.ValueKind != JsonValueKind.Number
            || !idValue.TryGetInt64(out long id)
        )
        {
            return false;
        }

        MiningMission? existing = Missions.Find(m => m.Id == id);
        if (entry.EventName == "MissionAccepted")
        {
            if (
                existing is not null
                || !MiningJson.Text(data, "Name").StartsWith("Mission_Mining", StringComparison.OrdinalIgnoreCase)
                || MiningJson.Number(data, "Count") <= 0
                || string.IsNullOrWhiteSpace(MiningJson.Text(data, "Commodity"))
            )
            {
                return false;
            }

            string destinationSystem = MiningJson.Text(data, "DestinationSystem");
            string destinationStation = MiningJson.Text(data, "DestinationStation");
            if (destinationSystem.Length == 0)
            {
                destinationSystem = system;
            }
            if (destinationStation.Length == 0 && destinationSystem.Equals(system, StringComparison.OrdinalIgnoreCase))
            {
                destinationStation = station;
            }
            Missions.Add(
                new MiningMission
                {
                    Id = id,
                    Commodity = MiningCommodityName.Canonical(MiningJson.Text(data, "Commodity")),
                    Title = MiningJson.Text(data, "LocalisedName"),
                    Required = (int)MiningJson.Number(data, "Count"),
                    Reward = (long)MiningJson.Number(data, "Reward"),
                    Expiry = MiningJson.Text(data, "Expiry"),
                    DestinationSystem = destinationSystem,
                    DestinationStation = destinationStation,
                    Destination = string.Join(
                        " / ",
                        new[] { destinationSystem, destinationStation }.Where(s => s.Length > 0)
                    ),
                }
            );
            return true;
        }
        if (existing is null)
        {
            return false;
        }

        switch (entry.EventName)
        {
            case "CargoDepot" when MiningJson.Text(data, "UpdateType") == "Deliver":
                existing.Delivered = Math.Clamp((int)MiningJson.Number(data, "ItemsDelivered"), 0, existing.Required);
                break;
            case "MissionCompleted":
                existing.Delivered = existing.Required;
                existing.OnBoard = 0;
                existing.Status = "Completed";
                break;
            case "MissionAbandoned":
                existing.Status = "Abandoned";
                break;
            case "MissionFailed":
                existing.Status = "Failed";
                break;
            default:
                return false;
        }
        return true;
    }

    public bool UpdateCargo(IEnumerable<CargoItem> cargo, DateTimeOffset? now = null)
    {
        var available = cargo
            .GroupBy(item => MiningCommodityName.Key(item.Name))
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Count));
        bool changed = false;
        foreach (MiningMission mission in Missions)
        {
            int previous = mission.OnBoard;
            mission.OnBoard = mission.IsActiveAt(now ?? DateTimeOffset.UtcNow)
                ? Math.Min(
                    Math.Max(0, mission.Required - mission.Delivered),
                    available.GetValueOrDefault(MiningCommodityName.Key(mission.Commodity))
                )
                : 0;
            changed |= previous != mission.OnBoard;
            available[MiningCommodityName.Key(mission.Commodity)] =
                available.GetValueOrDefault(MiningCommodityName.Key(mission.Commodity)) - mission.OnBoard;
        }
        return changed;
    }
}
