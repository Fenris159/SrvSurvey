using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SrvSurvey.Core.Network;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class DiagnosticsView : UserControl
{
    private DiagnosticsLogViewModel? connectedViewModel;
    private JournalInspectorViewModel? connectedInspector;
    private ReleaseUpdateViewModel? connectedReleaseUpdates;

    public DiagnosticsView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => ConnectPlatformServices();
        DetachedFromVisualTree += (_, _) => DisconnectPlatformServices();
        DataContextChanged += (_, _) => ConnectPlatformServices();
    }

    internal void ScrollToApplicationUpdates()
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                var origin = ApplicationUpdatesSection.TranslatePoint(
                    default,
                    DiagnosticsPageScroller);
                if (origin is null)
                {
                    ApplicationUpdatesSection.BringIntoView();
                    return;
                }

                var maximumOffset = Math.Max(
                    0,
                    DiagnosticsPageScroller.Extent.Height
                        - DiagnosticsPageScroller.Viewport.Height);
                var targetOffset = Math.Clamp(
                    DiagnosticsPageScroller.Offset.Y + origin.Value.Y,
                    0,
                    maximumOffset);
                DiagnosticsPageScroller.Offset = new Vector(
                    DiagnosticsPageScroller.Offset.X,
                    targetOffset);
            },
            DispatcherPriority.Loaded);
    }

    private void ConnectPlatformServices()
    {
        DisconnectPlatformServices();
        if (DataContext is MainWindowViewModel viewModel)
        {
            connectedViewModel = viewModel.DiagnosticsLog;
            connectedInspector = viewModel.JournalInspector;
            connectedReleaseUpdates = viewModel.ReleaseUpdates;
            connectedViewModel.SetPlatformServices(
                WriteClipboardAsync,
                LaunchDirectoryAsync);
            connectedInspector.SetClipboardWriter(WriteClipboardAsync);
            connectedReleaseUpdates.SetUriLauncher(LaunchUriAsync);
        }
    }

    private void DisconnectPlatformServices()
    {
        connectedViewModel?.SetPlatformServices(null, null);
        connectedInspector?.SetClipboardWriter(null);
        connectedReleaseUpdates?.SetUriLauncher(null);
        connectedViewModel = null;
        connectedInspector = null;
        connectedReleaseUpdates = null;
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

    private Task<bool> LaunchUriAsync(Uri uri)
    {
        var launcher = TopLevel.GetTopLevel(this)?.Launcher
            ?? throw new InvalidOperationException(
                "The desktop launcher is not available.");
        return launcher.LaunchUriAsync(uri);
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
        var file = files.Count > 0 ? files[0] : null;
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
            await launcher.LaunchUriAsync(WellKnownUris.EdGalaxyVisitedStars);
        }
    }
}
