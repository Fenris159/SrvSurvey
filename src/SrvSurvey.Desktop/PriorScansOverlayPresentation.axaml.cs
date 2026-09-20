using Avalonia.Controls;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop;

public sealed partial class PriorScansOverlayPresentation : UserControl, IOverlayPanelSizeAware
{
    public PriorScansOverlayPresentation()
    {
        InitializeComponent();
    }

    void IOverlayPanelSizeAware.SetPanelSizeOverrideActive(bool active)
    {
        PriorScansScroller.MaxHeight = active ? double.PositiveInfinity : 200d;
    }
}
