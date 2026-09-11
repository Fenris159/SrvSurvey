using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SrvSurvey.Core.Mining;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class MiningView : UserControl
{
    public MiningView() => InitializeComponent();

    private void SearchPane_Click(object? sender, RoutedEventArgs e)
    {
        SearchPage.IsVisible = true;
        LocalPane.IsVisible = false;
    }

    private void LocalPane_Click(object? sender, RoutedEventArgs e)
    {
        SearchPage.IsVisible = false;
        LocalPane.IsVisible = true;
    }

    private MiningWorkspaceViewModel? Model => (DataContext as MainWindowViewModel)?.MiningWorkspace;

    private void DeleteReport_Click(object? sender, RoutedEventArgs e) => Model?.DeleteSelectedReport();

    private void UndoReport_Click(object? sender, RoutedEventArgs e) => Model?.UndoDeleteReport();

    private async void ImportReports_Click(object? sender, RoutedEventArgs e) =>
        await WithFiles(
            async (vm, storage) =>
            {
                var files = await storage.OpenFilePickerAsync(
                    new FilePickerOpenOptions
                    {
                        Title = "Import EliteMining or SrvSurvey session CSV",
                        AllowMultiple = false,
                    }
                );
                if (files.Count == 0)
                {
                    return;
                }

                var file = files[0];
                await using var stream = await file.OpenReadAsync();
                using var reader = new StreamReader(stream);
                vm.ImportReports(await reader.ReadToEndAsync());
            }
        );

    private void Threshold_Click(object? sender, RoutedEventArgs e) => Model?.SetThreshold(false);

    private void RemoveThreshold_Click(object? sender, RoutedEventArgs e) => Model?.SetThreshold(true);

    private void AdjustQuality_Click(object? sender, RoutedEventArgs e) =>
        Model?.AdjustQuality((sender as Control)?.Tag as string == "minus" ? -1 : 1);

    private void Refinery_Click(object? sender, RoutedEventArgs e) => Model?.SaveRefineryEstimate();

    private void SavePreset_Click(object? sender, RoutedEventArgs e) => Model?.SaveAnnouncementPreset();

    private void LoadPreset_Click(object? sender, RoutedEventArgs e) => Model?.LoadAnnouncementPreset();

    private async void LoadVoices_Click(object? sender, RoutedEventArgs e)
    {
        if (Model is { } vm)
        {
            await vm.LoadVoicesAsync();
        }
    }

    private void OpenScreenshot_Click(object? sender, RoutedEventArgs e)
    {
        if (Model is { } vm && (sender as Control)?.Tag is string path)
        {
            vm.Status = MiningAttachmentActions.Open(path);
        }
    }

    private void RemoveScreenshot_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is string path)
        {
            Model?.RemoveScreenshot(path);
        }
    }

    private async void ExportReport_Click(object? sender, RoutedEventArgs e) =>
        await WithFiles(
            async (vm, storage) =>
            {
                var mode = (sender as Control)?.Tag as string;
                IReadOnlyList<MiningSession> sessions = vm.SelectedSession is { } session ? [session] : [];
                if (mode is "all" or "allcsv")
                {
                    sessions = vm.History;
                }

                if (sessions.Count == 0)
                {
                    vm.Status = "Select a completed session first.";
                    return;
                }
                var extension = mode switch
                {
                    "csv" or "allcsv" => "csv",
                    "txt" => "txt",
                    _ => "html",
                };
                var file = await storage.SaveFilePickerAsync(
                    new FilePickerSaveOptions
                    {
                        Title = "Export mining report",
                        SuggestedFileName = "mining-report." + extension,
                        DefaultExtension = extension,
                    }
                );
                if (file is null)
                {
                    return;
                }

                var report = await Task.Run(() =>
                    extension switch
                    {
                        "csv" => MiningReport.Csv(sessions),
                        "txt" => MiningReport.Text(sessions),
                        _ => MiningReport.Html(sessions),
                    }
                );
                await WriteAsync(file, report);
                vm.Status = "Report exported. Open the HTML report in a browser to print or save as PDF.";
            }
        );

    private async void AttachScreenshots_Click(object? sender, RoutedEventArgs e) =>
        await WithFiles(
            async (vm, storage) =>
            {
                if (vm.SelectedSession is not { } session)
                {
                    vm.Status = "Select a completed session first.";
                    return;
                }
                var files = await storage.OpenFilePickerAsync(
                    new FilePickerOpenOptions
                    {
                        Title = "Attach session screenshots",
                        AllowMultiple = true,
                        FileTypeFilter = [FilePickerFileTypes.ImageAll],
                    }
                );
                foreach (var file in files)
                {
                    if (
                        file.TryGetLocalPath() is { } path
                        && !session.Screenshots.Contains(path, StringComparer.OrdinalIgnoreCase)
                    )
                    {
                        session.Screenshots.Add(path);
                    }
                }

                vm.SaveNotesCommand.Execute(null);
                vm.Status = $"{session.Screenshots.Count} screenshots attached.";
            }
        );

    private async void ImportJournals_Click(object? sender, RoutedEventArgs e) =>
        await WithFiles(
            async (vm, storage) =>
            {
                var files = await storage.OpenFilePickerAsync(
                    new FilePickerOpenOptions
                    {
                        Title = "Import earlier journals for this commander",
                        AllowMultiple = true,
                    }
                );
                var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
                if (paths.Length > 0)
                {
                    await vm.ImportJournalsAsync(paths);
                }
            }
        );

    private async void Backup_Click(object? sender, RoutedEventArgs e) =>
        await WithFiles(
            async (vm, storage) =>
            {
                var file = await storage.SaveFilePickerAsync(
                    new FilePickerSaveOptions
                    {
                        Title = "Mining backup",
                        SuggestedFileName = "mining-backup.zip",
                        DefaultExtension = "zip",
                    }
                );
                if (file is null)
                {
                    return;
                }

                var bytes = await vm.BackupPackageAsync();
                await using var output = await file.OpenWriteAsync();
                output.SetLength(0);
                await output.WriteAsync(bytes);
                vm.Status = "Mining backup exported.";
            }
        );

    private async void Restore_Click(object? sender, RoutedEventArgs e) =>
        await WithFiles(
            async (vm, storage) =>
            {
                var files = await storage.OpenFilePickerAsync(
                    new FilePickerOpenOptions
                    {
                        Title = "Restore mining backup (ZIP or JSON; previous data retained)",
                        AllowMultiple = false,
                    }
                );
                if (files.Count == 0)
                {
                    return;
                }

                var file = files[0];
                await using var stream = await file.OpenReadAsync();
                using var buffer = new MemoryStream();
                var chunk = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(chunk)) > 0)
                {
                    if (buffer.Length + read > 256 * 1024 * 1024)
                    {
                        throw new IOException("Mining backup exceeds 256 MB.");
                    }

                    await buffer.WriteAsync(chunk.AsMemory(0, read));
                }
                var bytes = buffer.ToArray();
                if (bytes.Length > 2 && bytes[0] == 'P' && bytes[1] == 'K')
                {
                    await vm.RestorePackageAsync(bytes);
                }
                else
                {
                    vm.Restore(System.Text.Encoding.UTF8.GetString(bytes));
                }
            }
        );

    private async Task WithFiles(Func<MiningWorkspaceViewModel, IStorageProvider, Task> action)
    {
        if (Model is not { } vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        try
        {
            await action(vm, storage);
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidOperationException
                        or NotSupportedException
                        or System.Text.Json.JsonException
                        or Microsoft.VisualBasic.FileIO.MalformedLineException
            )
        {
            vm.Status = ex.Message;
        }
    }

    private static async Task WriteAsync(IStorageFile file, string text)
    {
        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(text);
    }
}
