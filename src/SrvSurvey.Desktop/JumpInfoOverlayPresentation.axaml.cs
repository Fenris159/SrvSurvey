using Avalonia.Controls;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop;

public sealed partial class JumpInfoOverlayPresentation : UserControl, IOverlayPanelSizePolicy
{
    public bool AutoHeight => true;

    /// <summary>Loads the Next Jump presentation and enables height measurement from its current content.</summary>
    public JumpInfoOverlayPresentation()
    {
        InitializeComponent();
    }
}
