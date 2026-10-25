using System.Globalization;
using System.Windows.Input;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>A mission snapshot shared by the workspace cards and passive overlay.</summary>
public sealed class MiningMissionCardViewModel : WorkspaceObservable
{
    private readonly Action<bool> setTracked;
    private bool tracked;
    private readonly MiningMission mission;
    private readonly DateTimeOffset? expiry;
    private DateTimeOffset now;

    public MiningMissionCardViewModel(
        MiningMission mission,
        DateTimeOffset now,
        Action<bool> setTracked,
        Action findHotspots
    )
    {
        this.mission = mission;
        this.now = now;
        this.setTracked = setTracked;
        tracked = mission.IsTracked;
        Id = mission.Id;
        Commodity = MiningCommodityName.Canonical(mission.Commodity);
        Required = mission.Required;
        OnBoard = mission.OnBoard;
        Delivered = mission.Delivered;
        Remaining = mission.Remaining;
        string[] legacyDestination = mission.Destination.Split(" / ", 2, StringSplitOptions.TrimEntries);
        System = mission.DestinationSystem.Length > 0 ? mission.DestinationSystem : legacyDestination[0];
        string station =
            mission.DestinationStation.Length > 0
                ? mission.DestinationStation
                : legacyDestination.ElementAtOrDefault(1) ?? "";
        Destination = string.Join(" @ ", new[] { station, System }.Where(value => value.Length > 0));
        HasSystem = System.Length > 0;
        bool hasExpiry = DateTimeOffset.TryParse(
            mission.Expiry,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTimeOffset expiration
        );
        expiry = hasExpiry ? expiration : null;
        IsComplete = mission.Delivered >= mission.Required || mission.Status == "Completed";
        Reward = $"{mission.Reward:N0} CR";
        RewardPerTon = mission.Required > 0 ? $"{mission.Reward / (double)mission.Required:N0} CR/t" : "";
        FindHotspotsCommand = new WorkspaceCommand(findHotspots);
    }

    public long Id { get; }
    public string Commodity { get; }
    public int Required { get; }
    public int OnBoard { get; }
    public int Delivered { get; }
    public int Remaining { get; }
    public string System { get; }
    public string Destination { get; }
    public bool HasSystem { get; }
    public bool IsActive => mission.IsActiveAt(now);
    public bool IsComplete { get; }
    public string Status => mission.Status == "Active" && !IsActive ? "Expired" : mission.Status;
    public string TimeRemaining => expiry is { } end ? FormatTimeRemaining(end - now) : "No expiry reported";
    public string Reward { get; }
    public string RewardPerTon { get; }
    public string CargoProgress => $"On board: {OnBoard:N0}/{Required:N0} t";
    public string DeliveryProgress => $"Delivered: {Delivered:N0}/{Required:N0} t";
    public string Needed => $"{Remaining:N0} t needed";
    public double CargoPercentage => Required > 0 ? Math.Clamp(OnBoard * 100d / Required, 0, 100) : 0;
    public double DeliveryPercentage => Required > 0 ? Math.Clamp(Delivered * 100d / Required, 0, 100) : 0;
    public ICommand FindHotspotsCommand { get; }
    public bool IsTracked
    {
        get => tracked;
        set
        {
            if (Set(ref tracked, value))
            {
                setTracked(value);
            }
        }
    }

    public void UpdateTime(DateTimeOffset value)
    {
        now = value;
        Changed(nameof(TimeRemaining));
        Changed(nameof(IsActive));
        Changed(nameof(Status));
    }

    private static string FormatTimeRemaining(TimeSpan remaining) =>
        remaining switch
        {
            { TotalSeconds: <= 0 } => "Expired",
            { TotalDays: >= 1 } => $"{(int)remaining.TotalDays}d {remaining.Hours}h remaining",
            _ => $"{(int)remaining.TotalHours}h {remaining.Minutes}m remaining",
        };
}
