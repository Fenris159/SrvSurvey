using Avalonia.Controls;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop;

public sealed partial class OverlayCategorySettingsWindow : Window
{
    public OverlayCategorySettingsWindow()
    {
        InitializeComponent();
    }

    public OverlayCategorySettingsWindow(
        OverlaySettingsCategoryDefinition definition,
        MainWindowViewModel viewModel)
        : this()
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(viewModel);
        Title = $"{definition.DisplayName} overlay settings";
        DataContext = viewModel;
        CategorySettingsContent.Content = new OverlaySettingsView(
            definition.Category)
        {
            DataContext = viewModel,
        };
    }
}
