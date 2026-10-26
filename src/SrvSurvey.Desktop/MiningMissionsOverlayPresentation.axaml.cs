using Avalonia.Controls;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop;

public sealed partial class MiningMissionsOverlayPresentation
    : UserControl,
        IOverlayPanelSizePolicy,
        IOverlayPanelViewportAware
{
    public bool AutoWidth => true;

    public bool AutoHeight => true;

    void IOverlayPanelViewportAware.SetAvailableHeight(double height) => MissionPanel.MaxHeight = height;

    public MiningMissionsOverlayPresentation()
    {
        InitializeComponent();
    }
}
