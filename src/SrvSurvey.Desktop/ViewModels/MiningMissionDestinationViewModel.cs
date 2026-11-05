using SrvSurvey.Core.Mining;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>A tracked delivery destination, shared by the workspace summary and overlay.</summary>
public sealed class MiningMissionDestinationViewModel : WorkspaceObservable
{
    private readonly MiningMissionCardViewModel? earliestMission;
    private int cargoCapacity;

    private MiningMissionDestinationViewModel(
        MiningMissionCardViewModel[] missions,
        bool useShortTags,
        int cargoCapacity
    )
    {
        this.cargoCapacity = cargoCapacity;
        System = missions[0].System.Trim();
        Station = missions[0].Station.Trim();
        TotalMissions = missions.Length;
        MiningMissionCardViewModel[] tracked = missions.Where(mission => mission.IsTracked).ToArray();
        TrackedMissions = tracked.Length;
        earliestMission = tracked.Where(mission => mission.ExpiresAt is not null).MinBy(mission => mission.ExpiresAt);
        Commodities = tracked
            .GroupBy(mission => MiningCommodityName.Key(mission.Commodity))
            .Select(group => new MiningMissionCommodityTotalsViewModel(group.ToArray(), useShortTags))
            .OrderBy(commodity => commodity.Commodity, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string System { get; }
    public string Station { get; }
    public bool HasStation => Station.Length > 0;
    public string Heading
    {
        get
        {
            if (HasStation)
            {
                return Station + " @";
            }
            return System.Length > 0 ? System : "Destination not reported";
        }
    }
    public string Destination => string.Join(" @ ", new[] { Station, System }.Where(value => value.Length > 0));
    public int TotalMissions { get; }
    public int TrackedMissions { get; }
    public string TrackedCount => $"{TrackedMissions}/{TotalMissions}";
    public string TrackedLabel => TrackedCount + " missions tracked";
    public string TimeRemaining => earliestMission?.TimeRemaining ?? "No expiry reported";
    public string ExpiryLabel => "Earliest: " + TimeRemaining;
    public IReadOnlyList<MiningMissionCommodityTotalsViewModel> Commodities { get; }
    public bool IsComplete => Commodities.All(commodity => commodity.Delivered >= commodity.Required);
    public long? EstimatedTrips
    {
        get
        {
            long need = Commodities.Sum(commodity => commodity.Remaining);
            if (need == 0)
            {
                return 0;
            }
            return cargoCapacity > 0 ? 1 + (need - 1) / cargoCapacity : null;
        }
    }
    public string EstimatedTripsValue => EstimatedTrips is { } trips ? $"{trips:N0}" : "—";
    public string EstimatedTripsLabel => "Estimated Trips: " + EstimatedTripsValue;

    public void RefreshReadouts(int shipCargoCapacity)
    {
        cargoCapacity = shipCargoCapacity;
        Changed(nameof(TimeRemaining));
        Changed(nameof(ExpiryLabel));
        Changed(nameof(EstimatedTrips));
        Changed(nameof(EstimatedTripsValue));
        Changed(nameof(EstimatedTripsLabel));
    }

    /// <summary>Includes only active destinations with a selection, retaining untracked missions in each denominator.</summary>
    public static IReadOnlyList<MiningMissionDestinationViewModel> Create(
        IReadOnlyList<MiningMissionCardViewModel> missions,
        bool useShortTags,
        int cargoCapacity = 0
    ) =>
        missions
            .Where(mission => mission.IsActive)
            .GroupBy(mission =>
                (
                    System: mission.System.Trim().ToUpperInvariant(),
                    Station: mission.Station.Trim().ToUpperInvariant(),
                    Unknown: string.IsNullOrWhiteSpace(mission.System) && string.IsNullOrWhiteSpace(mission.Station)
                        ? mission.Id
                        : 0
                )
            )
            .Where(group => group.Any(mission => mission.IsTracked))
            .Select(group => new MiningMissionDestinationViewModel(group.ToArray(), useShortTags, cargoCapacity))
            .ToArray();
}

/// <summary>Selected mission quantities for one commodity; allocated cargo is summed without counting it twice.</summary>
public sealed class MiningMissionCommodityTotalsViewModel
{
    internal MiningMissionCommodityTotalsViewModel(MiningMissionCardViewModel[] missions, bool useShortTags)
    {
        Commodity = missions[0].Commodity;
        Required = missions.Sum(mission => (long)mission.Required);
        Delivered = missions.Sum(mission => (long)mission.Delivered);
        OnBoard = missions.Sum(mission => (long)mission.OnBoard);
        Remaining = missions.Sum(mission => (long)mission.Remaining);
        UseShortTags = useShortTags;
    }

    public string Commodity { get; }
    public string Code => MiningCommodityCode.Abbreviate(Commodity);
    public string ColorHex => SurfaceMaterialBadgePalette.ColorHexFor(Code);
    public string ContrastForeground => SurfaceMaterialBadgePalette.ForegroundFor(ColorHex);
    public bool UseShortTags { get; }
    public long Required { get; }
    public long Delivered { get; }
    public long OnBoard { get; }
    public long Remaining { get; }
    public string CargoAmount => $"{OnBoard:N0} t";
    public string DeliveryAmount => $"{Delivered:N0}/{Required:N0}";
    public string CargoProgress => "On board: " + CargoAmount;
    public string DeliveryProgress => "Delivered: " + DeliveryAmount;
    public string Needed => $"Need: {Remaining:N0}";
    public double CargoPercentage => Required > 0 ? Math.Clamp(OnBoard * 100d / Required, 0, 100) : 0;
    public double DeliveryPercentage => Required > 0 ? Math.Clamp(Delivered * 100d / Required, 0, 100) : 0;
}
