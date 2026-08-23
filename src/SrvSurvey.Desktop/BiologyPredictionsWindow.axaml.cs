using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Runtime;

namespace SrvSurvey.Desktop;

public sealed partial class BiologyPredictionsWindow : Window
{
    private readonly BiologyPredictionsViewModel viewModel;

    public BiologyPredictionsWindow()
        : this(CreateDesignViewModel())
    {
    }

    public BiologyPredictionsWindow(BiologyPredictionsViewModel viewModel)
    {
        this.viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
        if (DesktopExternalEffectPolicy.IsAllowed)
        {
            viewModel.SetUriLauncher(LaunchUriAsync);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        viewModel.SetUriLauncher(null);
        base.OnClosed(e);
    }

    private void Close_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Close();
    }

    private Task<bool> LaunchUriAsync(Uri uri)
    {
        return Launcher.LaunchUriAsync(uri);
    }

    private static BiologyPredictionsViewModel CreateDesignViewModel()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-BiologyPredictions-Design");
        var settingsPath = Path.Combine(temporaryDirectory, "ui-settings.json");
        return new BiologyPredictionsViewModel(
            new SystemSurveyViewModel(
                new SystemSurveySettingsStore(settingsPath)),
            new BiologyPredictionsSettingsStore(settingsPath));
    }
}
