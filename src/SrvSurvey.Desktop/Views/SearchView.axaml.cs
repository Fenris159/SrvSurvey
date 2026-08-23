using Avalonia.Controls;
using Avalonia.Input.Platform;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Runtime;

namespace SrvSurvey.Desktop.Views;

public sealed partial class SearchView : UserControl
{
    public SearchView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => ConnectClipboard();
        DetachedFromVisualTree += (_, _) => DisconnectClipboard();
        DataContextChanged += (_, _) => ConnectClipboard();
    }

    private void ConnectClipboard()
    {
        if (DesktopExternalEffectPolicy.IsAllowed
            && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.NearestSystems.SetPlatformServices(
                WriteClipboardAsync,
                LaunchUriAsync);
        }
    }

    private void DisconnectClipboard()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.NearestSystems.SetPlatformServices(null, null);
        }
    }

    private async Task WriteClipboardAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard
            ?? throw new InvalidOperationException(
                "The desktop clipboard is not available.");
        await clipboard.SetTextAsync(text);
        await clipboard.FlushAsync();
    }

    private Task<bool> LaunchUriAsync(Uri uri)
    {
        var launcher = TopLevel.GetTopLevel(this)?.Launcher
            ?? throw new InvalidOperationException(
                "The desktop link launcher is not available.");
        return launcher.LaunchUriAsync(uri);
    }
}
