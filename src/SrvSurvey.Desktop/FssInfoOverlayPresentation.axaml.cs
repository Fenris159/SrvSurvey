using Avalonia.Controls;
using Avalonia.Layout;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop;

public sealed partial class FssInfoOverlayPresentation : UserControl, IOverlayPanelSizeAware
{
    public FssInfoOverlayPresentation()
    {
        InitializeComponent();
    }

    void IOverlayPanelSizeAware.SetPanelSizeOverrideActive(bool active)
    {
        if (active)
        {
            FssBodyScroller.MaxHeight = double.PositiveInfinity;
        }
        else
        {
            FssBodyScroller.ClearValue(Layoutable.MaxHeightProperty);
        }
    }
}
