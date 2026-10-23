using Avalonia.Controls;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop;

public sealed partial class JumpInfoOverlayPresentation : UserControl, IOverlayPanelSizePolicy
{
    public bool AutoHeight => true;

    public JumpInfoOverlayPresentation()
    {
        InitializeComponent();
    }
}
