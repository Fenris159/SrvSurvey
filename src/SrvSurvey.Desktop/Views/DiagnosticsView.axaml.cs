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
    private bool applicationUpdatesAlignmentPending;
    private int applicationUpdatesAlignmentAttempts;

    public DiagnosticsView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => ConnectPlatformServices();
        DetachedFromVisualTree += (_, _) =>
        {
            CancelApplicationUpdatesAlignment();
            DisconnectPlatformServices();
        };
        DataContextChanged += (_, _) => ConnectPlatformServices();
    }

    internal void ScrollToApplicationUpdates()
    {
        applicationUpdatesAlignmentPending = true;
        applicationUpdatesAlignmentAttempts = 0;
        LayoutUpdated -= OnApplicationUpdatesLayoutUpdated;
        LayoutUpdated += OnApplicationUpdatesLayoutUpdated;
        Dispatcher.UIThread.Post(
            TryAlignApplicationUpdates,
            DispatcherPriority.Loaded);
    }

    private void OnApplicationUpdatesLayoutUpdated(object? sender, EventArgs eventArgs)
    {
        TryAlignApplicationUpdates();
    }

    private void TryAlignApplicationUpdates()
    {
        if (!applicationUpdatesAlignmentPending
            || !IsVisible
            || DiagnosticsPageScroller.Viewport.Height <= 0)
        {
            return;
        }

        var origin = ApplicationUpdatesAnchor.TranslatePoint(
            default,
            DiagnosticsPageScroller);
        if (origin is null)
        {
            return;
        }

        if (Math.Abs(origin.Value.Y) <= 0.5
            || applicationUpdatesAlignmentAttempts >= 3)
        {
            CancelApplicationUpdatesAlignment();
            return;
        }

        applicationUpdatesAlignmentAttempts++;
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
        Dispatcher.UIThread.Post(
            TryAlignApplicationUpdates,
            DispatcherPriority.Background);
    }

    private void CancelApplicationUpdatesAlignment()
    {
        applicationUpdatesAlignmentPending = false;
        LayoutUpdated -= OnApplicationUpdatesLayoutUpdated;
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

    private async void ReleaseNotes_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || !viewModel.ReleaseUpdates.HasReleaseNotes
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var dialog = new ReleaseNotesDialog(
            $"SrvSurvey-XP {viewModel.ReleaseUpdates.LatestVersion}",
            viewModel.ReleaseUpdates.ReleaseNotes);
        await dialog.ShowDialog(owner);
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
