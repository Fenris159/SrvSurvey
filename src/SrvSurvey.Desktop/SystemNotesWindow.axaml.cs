using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Runtime;

namespace SrvSurvey.Desktop;

public sealed partial class SystemNotesWindow : Window
{
    private readonly SystemNotesViewModel viewModel;

    public SystemNotesWindow()
        : this(new SystemNotesViewModel(
            new SystemNoteStore(Path.GetTempPath()),
            new SystemNotesSettingsStore(Path.GetTempPath())))
    {
    }

    public SystemNotesWindow(SystemNotesViewModel viewModel)
    {
        this.viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
        if (DesktopExternalEffectPolicy.IsAllowed)
        {
            viewModel.SetPlatformServices(
                LaunchUriAsync,
                LaunchDirectoryAsync);
        }
        Closed += OnClosed;
    }

    private async void AlwaysOnTop_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await viewModel.SetAlwaysOnTopAsync(
            AlwaysOnTopCheckBox.IsChecked == true);
    }

    private async void Save_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (await viewModel.SaveAsync())
        {
            Close();
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Close();
    }

    private Task<bool> LaunchUriAsync(Uri uri)
    {
        return Launcher.LaunchUriAsync(uri);
    }

    private Task<bool> LaunchDirectoryAsync(DirectoryInfo directory)
    {
        return Launcher.LaunchDirectoryInfoAsync(directory);
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        Closed -= OnClosed;
        viewModel.SetPlatformServices(null, null);
    }
}
