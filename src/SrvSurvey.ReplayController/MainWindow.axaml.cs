using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.ReplayController;

public sealed partial class MainWindow : Window
{
    private readonly ReplayControllerViewModel viewModel;

    public MainWindow()
    {
        InitializeComponent();
        var managedRoot = Path.Combine(
            AppDataPaths.ResolveCurrent().DataDirectory,
            "diagnostic-replays");
        viewModel = new ReplayControllerViewModel(managedRoot);
        DataContext = viewModel;
        Closed += OnClosed;
    }

    private async void ImportReplay_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Import an Elite journal or SrvSurvey replay package",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Journal replay evidence")
                    {
                        Patterns = ["*.log", "*.jsonl", "*.srvreplay"],
                    },
                ],
            });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await viewModel.ImportAsync(path);
        }
    }

    private async void ChooseExecutable_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Choose the SrvSurvey desktop executable",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("SrvSurvey executable")
                    {
                        Patterns = OperatingSystem.IsWindows()
                            ? ["SrvSurvey.Desktop.exe", "*.exe"]
                            : ["SrvSurvey.Desktop", "*"],
                    },
                ],
            });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            viewModel.SrvSurveyExecutablePath = path;
        }
    }

    private async void OpenReplayFolder_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await OpenDirectoryAsync(viewModel.SessionDirectory);
    }

    private async void OpenLogsFolder_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await OpenDirectoryAsync(viewModel.LogsDirectory);
    }

    private async Task OpenDirectoryAsync(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
        }
    }

    private async void OnClosed(object? sender, EventArgs eventArgs)
    {
        Closed -= OnClosed;
        await viewModel.DisposeAsync();
    }
}
