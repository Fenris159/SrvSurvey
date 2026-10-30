using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Runtime;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class JourneysView : UserControl
{
    public JourneysView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => ConfigureDirectoryLauncher();
        DetachedFromVisualTree += (_, _) => Model?.SetDirectoryLauncher(null);
        DataContextChanged += (_, _) => ConfigureDirectoryLauncher();
    }

    private JourneyWorkspaceViewModel? Model => DataContext as JourneyWorkspaceViewModel;

    private void ConfigureDirectoryLauncher()
    {
        if (DesktopExternalEffectPolicy.IsAllowed)
        {
            Model?.SetDirectoryLauncher(directory =>
                TopLevel.GetTopLevel(this) is { } top
                    ? top.Launcher.LaunchDirectoryInfoAsync(directory)
                    : Task.FromResult(false)
            );
        }
    }

    private void Workspace_SizeChanged(object? sender, SizeChangedEventArgs args)
    {
        bool compact = Bounds.Width < 900;
        JourneyLayout.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "230,*");
        JourneyLayout.RowDefinitions = new RowDefinitions(compact ? "180,*" : "*");
        Grid.SetColumn(JourneyReadout, compact ? 0 : 1);
        Grid.SetRow(JourneyReadout, compact ? 1 : 0);
        bool narrowVisits = Bounds.Width < 760;
        VisitLayout.ColumnDefinitions = new ColumnDefinitions(narrowVisits ? "*" : "235,*");
        VisitLayout.RowDefinitions = new RowDefinitions(narrowVisits ? "160,*" : "*");
        Grid.SetColumn(VisitInspector, narrowVisits ? 0 : 1);
        Grid.SetRow(VisitInspector, narrowVisits ? 1 : 0);
    }

    private void Journeys_Click(object? sender, RoutedEventArgs args)
    {
        if (Model is { } model)
        {
            model.ShowArchived = false;
        }
    }

    private void Archive_Click(object? sender, RoutedEventArgs args)
    {
        if (Model is { } model)
        {
            model.ShowArchived = true;
        }
    }

    private async void Preferences_Click(object? sender, RoutedEventArgs args)
    {
        await SavePreferencesAsync();
    }

    internal Task SavePreferencesAsync() =>
        Model is { } model
            ? model.SetPreferencesAsync(AlwaysOnTopCheckBox.IsChecked == true, GalacticTimeCheckBox.IsChecked == true)
            : Task.CompletedTask;

    private void CurrentStart_Click(object? sender, RoutedEventArgs args)
    {
        if (Model is { } model)
        {
            model.UseCurrentStart = true;
        }
    }

    private void PriorStart_Click(object? sender, RoutedEventArgs args)
    {
        if (Model is { } model)
        {
            model.UseCurrentStart = false;
        }
    }

    private async void ScreenshotListBox_DoubleTapped(object? sender, TappedEventArgs args)
    {
        if (
            ScreenshotListBox.SelectedItem is string path
            && DesktopExternalEffectPolicy.IsAllowed
            && File.Exists(path)
            && TopLevel.GetTopLevel(this) is { } top
        )
        {
            await top.Launcher.LaunchFileInfoAsync(new FileInfo(path));
        }
    }

    private async void ExportHtml_Click(object? sender, RoutedEventArgs args)
    {
        if (DesktopExternalEffectPolicy.IsAllowed && TopLevel.GetTopLevel(this) is { } top)
        {
            await ExportHtmlAsync(async options =>
            {
                IReadOnlyList<IStorageFolder> folders = await top.StorageProvider.OpenFolderPickerAsync(options);
                if (folders.Count == 0)
                {
                    return null;
                }
                IStorageFolder folder = folders[0];
                try
                {
                    string path =
                        folder.TryGetLocalPath()
                        ?? throw new NotSupportedException("Choose a local folder to save the report safely.");
                    return new JourneyReportDestination(path, folder);
                }
                catch
                {
                    folder.Dispose();
                    throw;
                }
            });
        }
    }

    internal async Task ExportHtmlAsync(Func<FolderPickerOpenOptions, Task<JourneyReportDestination?>> openDestination)
    {
        if (Model is not { } model || !model.CanExportReport)
        {
            return;
        }

        try
        {
            JourneyHtmlExport? report = await model.CreateHtmlReportAsync();
            if (report is null)
            {
                return;
            }

            JourneyReportDestination? destination = await openDestination(
                new FolderPickerOpenOptions
                {
                    Title = "Choose a destination for the journey report folder",
                    AllowMultiple = false,
                }
            );
            if (destination is null)
            {
                model.SetExportStatus("Journey report export canceled.");
                return;
            }

            using IDisposable? owner = destination.Owner;
            await model.SaveHtmlReportAsync(report, destination.Path);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidOperationException
                        or NotSupportedException
                        or ArgumentException
            )
        {
            model.SetExportStatus("The journey report could not be saved: " + exception.Message);
        }
    }
}

internal sealed record JourneyReportDestination(string Path, IDisposable? Owner = null);
