using Avalonia.Controls;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop;

public sealed partial class FleetCarrierRouteOverlayPresentation : UserControl, IOverlayPanelSizePolicy
{
    public bool AutoHeight => true;

    public FleetCarrierRouteOverlayPresentation()
    {
        InitializeComponent();
    }
}
