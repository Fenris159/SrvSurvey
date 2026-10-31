using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class CarrierRoutesView : UserControl
{
    public CarrierRoutesView()
    {
        InitializeComponent();
        AttachedToVisualTree += RefreshSharedLibrary;
    }

    private async void RefreshSharedLibrary(object? sender, VisualTreeAttachmentEventArgs eventArgs)
    {
        if (DataContext is RouteManagerViewModel { HasProfile: true, IsBusy: false, IsDialogVisible: false } manager)
        {
            await manager.RefreshAsync();
            if (IsEffectivelyVisible && ReferenceEquals(DataContext, manager))
            {
                manager.Workspace.SelectCarrierAutoCopyTarget();
            }
        }
    }

    private void OpenOverlaySettings_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is RouteManagerViewModel manager && TopLevel.GetTopLevel(this) is MainWindow window)
        {
            window.OpenWorkspaceOverlaySettings(
                manager.Workspace.IsSquadronCarrierWorkspace
                    ? OverlaySettingsCategory.SquadronCarrierRoutes
                    : OverlaySettingsCategory.FleetCarrierRoutes
            );
        }
    }

    private async void ImportRoutes_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (
            DataContext is not RouteManagerViewModel manager
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
                    Title = "Import saved fleet-carrier routes",
                    AllowMultiple = true,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("SrvSurvey fleet-carrier route files")
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
            await manager.ImportAsync(paths);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or NotSupportedException
                        or InvalidOperationException
            )
        {
            manager.ReportFilePickerError("import", exception);
        }
    }

    private async void ExportRoutes_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (
            DataContext is not RouteManagerViewModel manager
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage
        )
        {
            return;
        }

        try
        {
            IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = "Export selected fleet-carrier routes", AllowMultiple = false }
            );
            string? path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
            if (!string.IsNullOrWhiteSpace(path))
            {
                await manager.ExportSelectedAsync(path);
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
            manager.ReportFilePickerError("export", exception);
        }
    }

    private async void ExportSpanshRoutes_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is RouteManagerViewModel manager)
        {
            await ExportWithPickerAsync(
                manager,
                "Export selected fleet-carrier routes as Spansh JSON",
                manager.ExportSelectedSpanshAsync,
                "Spansh export"
            );
        }
    }

    private async void ExportCsvRoutes_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is RouteManagerViewModel manager)
        {
            await ExportWithPickerAsync(
                manager,
                "Export selected fleet-carrier routes as CSV",
                manager.ExportSelectedCsvAsync,
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
}
