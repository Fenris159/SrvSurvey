using Avalonia.Controls;

namespace SrvSurvey.Desktop;

public sealed partial class NotificationOverlayPresentation : UserControl
{
    /// <summary>Creates the bounded notification list shared by live overlays and editor previews.</summary>
    public NotificationOverlayPresentation()
    {
        InitializeComponent();
    }

    /// <summary>Keeps the newest messages visible when the list or its available height changes.</summary>
    private void OnMessagesScrollChanged(object? sender, ScrollChangedEventArgs eventArgs)
    {
        if (eventArgs.ExtentDelta.Y != 0 || eventArgs.ViewportDelta.Y != 0)
        {
            MessagesScroller.ScrollToEnd();
        }
    }
}
