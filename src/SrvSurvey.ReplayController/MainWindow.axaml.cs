using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.ReplayController;

public sealed partial class MainWindow : Window
{
    private ReplayControllerViewModel ViewModel =>
        DataContext as ReplayControllerViewModel
        ?? throw new InvalidOperationException(
            "The replay controller view model is unavailable.");

    public MainWindow()
    {
        InitializeComponent();
        var managedRoot = Path.Combine(
            AppDataPaths.ResolveCurrent().DataDirectory,
            "diagnostic-replays");
        DataContext = new ReplayControllerViewModel(managedRoot);
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
        var path = files.Count > 0
            ? files[0].TryGetLocalPath()
            : null;
        if (!string.IsNullOrWhiteSpace(path))
        {
            await ViewModel.ImportAsync(path);
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
        var path = files.Count > 0
            ? files[0].TryGetLocalPath()
            : null;
        if (!string.IsNullOrWhiteSpace(path))
        {
            ViewModel.SrvSurveyExecutablePath = path;
        }
    }

    private async void OpenReplayFolder_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await OpenDirectoryAsync(ViewModel.SessionDirectory);
    }

    private async void OpenLogsFolder_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await OpenDirectoryAsync(ViewModel.LogsDirectory);
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
        var viewModel = ViewModel;
        DataContext = null;
        await viewModel.DisposeAsync();
    }
}
