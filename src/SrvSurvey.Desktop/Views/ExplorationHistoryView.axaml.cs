using Avalonia.Controls;
using Avalonia.Input.Platform;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class ExplorationHistoryView : UserControl
{
    private ExplorationHistoryViewModel? connected;

    public ExplorationHistoryView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => Connect();
        DetachedFromVisualTree += (_, _) => Disconnect();
        DataContextChanged += (_, _) => Connect();
    }

    private void Connect()
    {
        Disconnect();
        if (DataContext is ExplorationHistoryViewModel model && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            connected = model;
            connected.SetClipboardWriter(async text =>
            {
                await clipboard.SetTextAsync(text);
                await clipboard.FlushAsync();
            });
        }
    }

    private void Disconnect()
    {
        connected?.SetClipboardWriter(null);
        connected = null;
    }
}
