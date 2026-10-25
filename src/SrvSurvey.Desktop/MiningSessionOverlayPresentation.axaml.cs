using Avalonia.Controls;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop;

public sealed partial class MiningSessionOverlayPresentation
    : UserControl,
        IOverlayPanelSizePolicy,
        IOverlayPanelViewportAware
{
    public bool AutoHeight => true;

    void IOverlayPanelViewportAware.SetAvailableHeight(double height) => SessionPanel.MaxHeight = height;

    public MiningSessionOverlayPresentation()
    {
        InitializeComponent();
    }
}
