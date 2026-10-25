using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class MiningWorkspaceViewModel
{
    // Mission collection and return trips remain useful outside an active mining session.
    public bool ShouldShowMissions =>
        sessionAvailable
        && storageAvailable
        && eliteStatus is { InMainShip: true, OnFoot: false, InSrv: false }
        && TrackedMissionCards.Count > 0;

    private IReadOnlyList<MiningMissionCardViewModel>? cachedMissionCards,
        cachedTrackedMissionCards;
    public IReadOnlyList<MiningMissionCardViewModel> MissionCards => cachedMissionCards ??= ReadMissionCards();
    public bool HasMissionCards => MissionCards.Count > 0;
    public IReadOnlyList<MiningMissionCardViewModel> TrackedMissionCards =>
        cachedTrackedMissionCards ??= ReadTrackedMissionCards();

    private MiningMissionCardViewModel[] ReadMissionCards() => Missions.Select(CreateMissionCard).ToArray();

    private MiningMissionCardViewModel[] ReadTrackedMissionCards() =>
        MissionCards.Where(mission => mission.IsTracked && mission.IsActive).ToArray();

    private void InvalidateMissionCards()
    {
        cachedMissionCards = null;
        cachedTrackedMissionCards = null;
    }

    private void RefreshMissionTimes()
    {
        DateTimeOffset now = clock.GetUtcNow();
        foreach (MiningMissionCardViewModel card in MissionCards)
        {
            card.UpdateTime(now);
        }
        MiningMissionCardViewModel[] tracked = ReadTrackedMissionCards();
        if (cachedTrackedMissionCards is null || !cachedTrackedMissionCards.SequenceEqual(tracked))
        {
            cachedTrackedMissionCards = tracked;
        }
    }

    private MiningMissionCardViewModel CreateMissionCard(MiningMission mission)
    {
        string? owner = commander;
        return new(
            mission,
            clock.GetUtcNow(),
            tracked => SetMissionTracked(owner, mission.Id, tracked),
            () => FindMissionHotspots(mission)
        );
    }

    private void SetMissionTracked(string? owner, long id, bool tracked)
    {
        if (owner != commander || !storageAvailable || state.Missions.Missions.Find(m => m.Id == id) is not { } mission)
        {
            return;
        }
        mission.IsTracked = tracked;
        cachedMissions = null;
        InvalidateMissionCards();
        Save();
        Changed(nameof(MissionCards));
        Changed(nameof(ShouldShowMissions));
    }

    private void ApplyMissionLocation(JournalEventEnvelope entry)
    {
        if (entry.EventName is "Location" or "Docked")
        {
            station = Text(entry.Payload, "StationName");
            system = Text(entry.Payload, "StarSystem");
        }
        if (entry.EventName is "Undocked" or FsdJumpEvent or "CarrierJump")
        {
            station = "";
        }
    }

    private void FindMissionHotspots(MiningMission mission)
    {
        Filter = mission.Commodity;
        string mineral = MiningCommodityName.Canonical(mission.Commodity);
        Search.Mineral = mineral;
        Search.RingMineralChips.Add(mineral);
        SelectedTab = 4;
    }
}
