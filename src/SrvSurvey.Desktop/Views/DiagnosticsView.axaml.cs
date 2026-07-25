using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class DiagnosticsView : UserControl
{
    private DiagnosticsLogViewModel? connectedViewModel;

    public DiagnosticsView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => ConnectPlatformServices();
        DetachedFromVisualTree += (_, _) => DisconnectPlatformServices();
        DataContextChanged += (_, _) => ConnectPlatformServices();
    }

    private void ConnectPlatformServices()
    {
        DisconnectPlatformServices();
        if (DataContext is MainWindowViewModel viewModel)
        {
            connectedViewModel = viewModel.DiagnosticsLog;
            connectedViewModel.SetPlatformServices(
                WriteClipboardAsync,
                LaunchDirectoryAsync);
        }
    }

    private void DisconnectPlatformServices()
    {
        connectedViewModel?.SetPlatformServices(null, null);
        connectedViewModel = null;
    }

    private async Task WriteClipboardAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard
            ?? throw new InvalidOperationException(
                "The desktop clipboard is not available.");
        await clipboard.SetTextAsync(text);
        await clipboard.FlushAsync();
    }

    private Task<bool> LaunchDirectoryAsync(DirectoryInfo directory)
    {
        var launcher = TopLevel.GetTopLevel(this)?.Launcher
            ?? throw new InvalidOperationException(
                "The desktop launcher is not available.");
        return launcher.LaunchDirectoryInfoAsync(directory);
    }
}
