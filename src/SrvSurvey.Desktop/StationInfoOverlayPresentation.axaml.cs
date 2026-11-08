using Avalonia.Controls;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop;

public sealed partial class StationInfoOverlayPresentation : UserControl, IOverlayPanelSizePolicy
{
    public bool AutoHeight => true;

    public bool FollowPlacementAnchors => true;

    public StationInfoOverlayPresentation()
    {
        InitializeComponent();
    }
}
