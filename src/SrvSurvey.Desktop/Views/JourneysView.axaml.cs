using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
                TopLevel.GetTopLevel(this)!.Launcher.LaunchDirectoryInfoAsync(directory)
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
            ? model.SetPreferencesAsync(model.AlwaysOnTop, GalacticTimeCheckBox.IsChecked == true)
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
                IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(options);
                if (file is null)
                {
                    return null;
                }
                try
                {
                    return new JourneyReportDestination(await file.OpenWriteAsync(), file);
                }
                catch
                {
                    file.Dispose();
                    throw;
                }
            });
        }
    }

    internal async Task ExportHtmlAsync(Func<FilePickerSaveOptions, Task<JourneyReportDestination?>> openDestination)
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
                new FilePickerSaveOptions
                {
                    Title = "Export journey report",
                    SuggestedFileName = report.FileName,
                    DefaultExtension = "html",
                    FileTypeChoices = [new FilePickerFileType("HTML report") { Patterns = ["*.html"] }],
                }
            );
            if (destination is null)
            {
                model.SetExportStatus("Journey report export canceled.");
                return;
            }

            using IDisposable? owner = destination.Owner;
            await using Stream stream = destination.Stream;
            if (stream.CanSeek)
            {
                stream.SetLength(0);
                stream.Position = 0;
            }
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(report.Html);
            model.SetExportStatus(
                "Journey report exported. Open the HTML file in a browser to read, share, or print it."
            );
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidOperationException
                        or NotSupportedException
            )
        {
            model.SetExportStatus("The journey report could not be saved: " + exception.Message);
        }
    }
}

internal sealed record JourneyReportDestination(Stream Stream, IDisposable? Owner = null);
