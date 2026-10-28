using Avalonia.Controls;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop;

public sealed partial class SphericalSearchOverlayPresentation : UserControl, IOverlayPanelSizePolicy
{
    public bool AutoWidth => true;

    public bool AutoHeight => true;

    public bool FollowPlacementAnchors => true;

    public SphericalSearchOverlayPresentation()
    {
        InitializeComponent();
    }
}
