using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
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

    private async void ChooseVisitedStarsCache_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null
            || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Choose Elite VisitedStarsCache.dat",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Elite visited-stars cache")
                    {
                        Patterns = ["VisitedStarsCache.dat"],
                    },
                ],
            });
        var file = files.FirstOrDefault();
        if (file is not null)
        {
            viewModel.VisitedStarsCache.TargetPath = file.Path.LocalPath;
        }
    }

    private async void OpenVisitedStarsWebsite_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is not null)
        {
            await launcher.LaunchUriAsync(
                new Uri("https://edgalaxy.net/visitedstars"));
        }
    }
}
