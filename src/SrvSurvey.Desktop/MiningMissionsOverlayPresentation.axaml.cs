using Avalonia.Controls;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop;

public sealed partial class MiningMissionsOverlayPresentation : UserControl, IOverlayPanelSizePolicy
{
    public bool AutoHeight => true;

    public MiningMissionsOverlayPresentation()
    {
        InitializeComponent();
    }
}
