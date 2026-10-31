using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SrvSurvey.Desktop.Runtime;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class TravelView : UserControl
{
    public TravelView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => ConnectClipboard();
        DetachedFromVisualTree += (_, _) => DisconnectClipboard();
        DataContextChanged += (_, _) => ConnectClipboard();
    }

    private void ConnectClipboard()
    {
        if (DesktopExternalEffectPolicy.IsAllowed && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Route.SetClipboardWriter(WriteClipboardAsync);
            viewModel.FleetCarrierRoute.SetClipboardWriter(WriteClipboardAsync);
            viewModel.SquadronCarrierRoute.SetClipboardWriter(WriteClipboardAsync);
        }
    }

    private void DisconnectClipboard()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Route.SetClipboardWriter(null);
            viewModel.FleetCarrierRoute.SetClipboardWriter(null);
            viewModel.SquadronCarrierRoute.SetClipboardWriter(null);
        }
    }

    private async void PasteTarget_Click(object? sender, RoutedEventArgs eventArgs)
    {
        string? text = null;
        try
        {
            IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
            {
                text = await clipboard.TryGetTextAsync();
            }
        }
        catch (Exception)
        {
            // The view model reports the unavailable clipboard as invalid input.
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.GroundTarget.ApplyPastedTextAsync(text);
        }
    }

    private async void ImportRoutes_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (
            DataContext is not MainWindowViewModel viewModel
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage
        )
        {
            return;
        }

        try
        {
            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Import saved routes",
                    AllowMultiple = true,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("SrvSurvey route files")
                        {
                            Patterns = ["*.json"],
                            MimeTypes = ["application/json"],
                        },
                    ],
                }
            );
            string[] paths = files
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .ToArray();
            await viewModel.RouteManager.ImportAsync(paths);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or NotSupportedException
                        or InvalidOperationException
            )
        {
            viewModel.RouteManager.ReportFilePickerError("import", exception);
        }
    }

    private async void ExportRoutes_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (
            DataContext is not MainWindowViewModel viewModel
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage
        )
        {
            return;
        }

        try
        {
            IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = "Export selected routes", AllowMultiple = false }
            );
            string? path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
            if (!string.IsNullOrWhiteSpace(path))
            {
                await viewModel.RouteManager.ExportSelectedAsync(path);
            }
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or NotSupportedException
                        or InvalidOperationException
            )
        {
            viewModel.RouteManager.ReportFilePickerError("export", exception);
        }
    }

    private async void ExportSpanshRoutes_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await ExportWithPickerAsync(
                viewModel.RouteManager,
                "Export selected routes as Spansh JSON",
                viewModel.RouteManager.ExportSelectedSpanshAsync,
                "Spansh export"
            );
        }
    }

    private async void ExportCsvRoutes_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await ExportWithPickerAsync(
                viewModel.RouteManager,
                "Export selected routes as CSV",
                viewModel.RouteManager.ExportSelectedCsvAsync,
                "CSV export"
            );
        }
    }

    private async Task ExportWithPickerAsync(
        RouteManagerViewModel manager,
        string title,
        Func<string, Task> export,
        string operation
    )
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        try
        {
            IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = title, AllowMultiple = false }
            );
            string? path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
            if (!string.IsNullOrWhiteSpace(path))
            {
                await export(path);
            }
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or NotSupportedException
                        or InvalidOperationException
            )
        {
            manager.ReportFilePickerError(operation, exception);
        }
    }

    private async Task WriteClipboardAsync(string text)
    {
        IClipboard clipboard =
            TopLevel.GetTopLevel(this)?.Clipboard
            ?? throw new InvalidOperationException("The desktop clipboard is not available.");
        await clipboard.SetTextAsync(text);
        await clipboard.FlushAsync();
    }
}
