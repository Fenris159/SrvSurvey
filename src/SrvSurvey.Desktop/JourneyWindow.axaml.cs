using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Runtime;

namespace SrvSurvey.Desktop;

public sealed partial class JourneyWindow : Window
{
    private readonly JourneyWorkspaceViewModel viewModel;

    public JourneyWindow()
        : this(CreateDesignViewModel())
    {
    }

    public JourneyWindow(JourneyWorkspaceViewModel viewModel)
    {
        this.viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
        if (DesktopExternalEffectPolicy.IsAllowed)
        {
            viewModel.SetDirectoryLauncher(LaunchDirectoryAsync);
        }
        Closed += OnClosed;
    }

    private async void Preferences_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await viewModel.SetPreferencesAsync(
            AlwaysOnTopCheckBox.IsChecked == true,
            GalacticTimeCheckBox.IsChecked == true);
    }

    private void Close_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Close();
    }

    private void CurrentStart_Click(object? sender, RoutedEventArgs eventArgs)
    {
        viewModel.UseCurrentStart = true;
    }

    private void PriorStart_Click(object? sender, RoutedEventArgs eventArgs)
    {
        viewModel.UseCurrentStart = false;
    }

    private async void ScreenshotListBox_DoubleTapped(
        object? sender,
        TappedEventArgs eventArgs)
    {
        if (ScreenshotListBox.SelectedItem is string path
            && DesktopExternalEffectPolicy.IsAllowed
            && File.Exists(path))
        {
            await Launcher.LaunchFileInfoAsync(new FileInfo(path));
        }
    }

    private Task<bool> LaunchDirectoryAsync(DirectoryInfo directory)
    {
        return Launcher.LaunchDirectoryInfoAsync(directory);
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        Closed -= OnClosed;
        viewModel.SetDirectoryLauncher(null);
    }

    private static JourneyWorkspaceViewModel CreateDesignViewModel()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-Journey-Design");
        return new JourneyWorkspaceViewModel(
            new JourneyService(
                new JourneyStore(temporaryDirectory),
                new JourneyJournalHistoryReader(temporaryDirectory),
                new CommanderProfileStore(temporaryDirectory),
                new ExobiologyReferenceCatalog([])),
            new EmptySystemResolver(),
            new SystemNoteStore(temporaryDirectory),
            new SystemNotesSettingsStore(temporaryDirectory));
    }

    private sealed class EmptySystemResolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
        }
    }
}
