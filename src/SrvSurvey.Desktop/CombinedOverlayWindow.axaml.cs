using Avalonia.Controls;

namespace SrvSurvey.Desktop;

public sealed partial class CombinedOverlayWindow : Window
{
    public CombinedOverlayWindow()
    {
        InitializeComponent();
    }

    internal void Add(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        OverlayCanvas.Children.Add(control);
    }

    internal void Remove(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        OverlayCanvas.Children.Remove(control);
    }
}
