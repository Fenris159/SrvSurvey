using System.ComponentModel;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class MiningMissionsOverlayViewModel : WorkspaceObservable, IDisposable
{
    private readonly MiningWorkspaceViewModel? workspace;
    private readonly IReadOnlyList<MiningMissionCardViewModel> preview;
    private readonly IReadOnlyList<MiningMissionDestinationViewModel> previewDestinations;

    public MiningMissionsOverlayViewModel(MiningWorkspaceViewModel? workspace, bool useShortTags = true)
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
        previewDestinations = MiningMissionDestinationViewModel.Create(preview, useShortTags, 512);
        workspace?.PropertyChanged += OnChanged;
    }

    public IReadOnlyList<MiningMissionCardViewModel> Missions => workspace?.TrackedMissionCards ?? preview;
    public IReadOnlyList<MiningMissionDestinationViewModel> Destinations =>
        workspace?.TrackedMissionDestinations ?? previewDestinations;
    public int VisibleDestinationLimit => Math.Clamp(workspace?.MissionDestinationLimit ?? 3, 1, 20);
    public bool HasMissions => Missions.Count > 0;

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MiningWorkspaceViewModel.MissionCards))
        {
            Changed(nameof(Missions));
            Changed(nameof(HasMissions));
            Changed(nameof(Destinations));
            Changed(nameof(VisibleDestinationLimit));
        }
    }

    public void Dispose()
    {
        workspace?.PropertyChanged -= OnChanged;
    }
}
