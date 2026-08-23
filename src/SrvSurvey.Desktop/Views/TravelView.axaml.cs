using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Runtime;

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
        if (DesktopExternalEffectPolicy.IsAllowed
            && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Route.SetClipboardWriter(WriteClipboardAsync);
            viewModel.FleetCarrierRoute.SetClipboardWriter(WriteClipboardAsync);
        }
    }

    private void DisconnectClipboard()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Route.SetClipboardWriter(null);
            viewModel.FleetCarrierRoute.SetClipboardWriter(null);
        }
    }

    private async void PasteTarget_Click(object? sender, RoutedEventArgs eventArgs)
    {
        string? text = null;
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
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

    private async void ImportRoutes_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        try
        {
            var files = await storage.OpenFilePickerAsync(
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
                });
            var paths = files
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .ToArray();
            await viewModel.RouteManager.ImportAsync(paths);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or InvalidOperationException)
        {
            viewModel.RouteManager.ReportFilePickerError("import", exception);
        }
    }

    private async void ExportRoutes_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        try
        {
            var folders = await storage.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Export selected routes",
                    AllowMultiple = false,
                });
            var path = folders.Count > 0
                ? folders[0].TryGetLocalPath()
                : null;
            if (!string.IsNullOrWhiteSpace(path))
            {
                await viewModel.RouteManager.ExportSelectedAsync(path);
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or InvalidOperationException)
        {
            viewModel.RouteManager.ReportFilePickerError("export", exception);
        }
    }

    private async void ExportSpanshRoutes_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await ExportWithPickerAsync(
                viewModel.RouteManager,
                "Export selected routes as Spansh JSON",
                viewModel.RouteManager.ExportSelectedSpanshAsync,
                "Spansh export");
        }
    }

    private async void ExportCsvRoutes_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await ExportWithPickerAsync(
                viewModel.RouteManager,
                "Export selected routes as CSV",
                viewModel.RouteManager.ExportSelectedCsvAsync,
                "CSV export");
        }
    }

    private async void ImportFleetCarrierRoutes_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        try
        {
            var files = await storage.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Import saved fleet-carrier routes",
                    AllowMultiple = true,
                    FileTypeFilter =
                    [
                        new FilePickerFileType(
                            "SrvSurvey fleet-carrier route files")
                        {
                            Patterns = ["*.json"],
                            MimeTypes = ["application/json"],
                        },
                    ],
                });
            var paths = files
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .ToArray();
            await viewModel.FleetCarrierRouteManager.ImportAsync(paths);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or InvalidOperationException)
        {
            viewModel.FleetCarrierRouteManager.ReportFilePickerError(
                "import",
                exception);
        }
    }

    private async void ExportFleetCarrierRoutes_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        try
        {
            var folders = await storage.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Export selected fleet-carrier routes",
                    AllowMultiple = false,
                });
            var path = folders.Count > 0
                ? folders[0].TryGetLocalPath()
                : null;
            if (!string.IsNullOrWhiteSpace(path))
            {
                await viewModel.FleetCarrierRouteManager.ExportSelectedAsync(path);
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or InvalidOperationException)
        {
            viewModel.FleetCarrierRouteManager.ReportFilePickerError(
                "export",
                exception);
        }
    }

    private async void ExportSpanshFleetCarrierRoutes_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await ExportWithPickerAsync(
                viewModel.FleetCarrierRouteManager,
                "Export selected fleet-carrier routes as Spansh JSON",
                viewModel.FleetCarrierRouteManager.ExportSelectedSpanshAsync,
                "Spansh export");
        }
    }

    private async void ExportCsvFleetCarrierRoutes_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await ExportWithPickerAsync(
                viewModel.FleetCarrierRouteManager,
                "Export selected fleet-carrier routes as CSV",
                viewModel.FleetCarrierRouteManager.ExportSelectedCsvAsync,
                "CSV export");
        }
    }

    private async Task ExportWithPickerAsync(
        RouteManagerViewModel manager,
        string title,
        Func<string, Task> export,
        string operation)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        try
        {
            var folders = await storage.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = title,
                    AllowMultiple = false,
                });
            var path = folders.Count > 0
                ? folders[0].TryGetLocalPath()
                : null;
            if (!string.IsNullOrWhiteSpace(path))
            {
                await export(path);
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or InvalidOperationException)
        {
            manager.ReportFilePickerError(operation, exception);
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
}
