using System.ComponentModel;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class MiningMissionsOverlayViewModel : WorkspaceObservable, IDisposable
{
    private readonly MiningWorkspaceViewModel? workspace;
    private readonly IReadOnlyList<MiningMissionCardViewModel> preview;

    public MiningMissionsOverlayViewModel(MiningWorkspaceViewModel? workspace)
    {
        this.workspace = workspace;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        preview = workspace is null ? new[]
            {
                new MiningMission
                {
                    Id = 1,
                    Commodity = "Bromellite",
                    Required = 236,
                    OnBoard = 73,
                    DestinationSystem = "Ngolona",
                    DestinationStation = "Huss Port",
                    Reward = 32077200,
                    Expiry = now.AddDays(6).AddHours(20).ToString("O"),
                },
                new MiningMission
                {
                    Id = 2,
                    Commodity = "Praseodymium",
                    Required = 57,
                    Delivered = 12,
                    OnBoard = 18,
                    DestinationSystem = "Ngolona",
                    DestinationStation = "Huss Port",
                    Reward = 648094,
                    Expiry = now.AddDays(6).AddHours(23).ToString("O"),
                },
            }.Select(mission => new MiningMissionCardViewModel(mission, now, _ => { }, () => { })).ToArray() : [];
        workspace?.PropertyChanged += OnChanged;
    }

    public IReadOnlyList<MiningMissionCardViewModel> Missions => workspace?.TrackedMissionCards ?? preview;
    public bool HasMissions => Missions.Count > 0;

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MiningWorkspaceViewModel.MissionCards))
        {
            Changed(nameof(Missions));
            Changed(nameof(HasMissions));
        }
    }

    public void Dispose()
    {
        workspace?.PropertyChanged -= OnChanged;
    }
}
