using Avalonia.Controls;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop;

public sealed partial class MassacreMissionsOverlayPresentation
    : UserControl,
        IOverlayPanelSizePolicy,
        IOverlayPanelViewportAware
{
    public bool AutoHeight => true;

    void IOverlayPanelViewportAware.SetAvailableHeight(double height) => MissionPanel.MaxHeight = height;

    public MassacreMissionsOverlayPresentation()
    {
        InitializeComponent();
    }
}
