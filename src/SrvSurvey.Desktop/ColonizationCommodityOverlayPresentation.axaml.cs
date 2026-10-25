using Avalonia.Controls;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop;

public sealed partial class ColonizationCommodityOverlayPresentation : UserControl, IOverlayPanelSizePolicy
{
    public bool AutoHeight => true;

    public ColonizationCommodityOverlayPresentation()
    {
        InitializeComponent();
    }
}
