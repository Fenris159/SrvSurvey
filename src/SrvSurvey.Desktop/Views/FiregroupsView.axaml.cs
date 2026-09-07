using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class FiregroupsView : UserControl
{
    private OverlayCategorySettingsWindow? settingsWindow;
    public FiregroupsView() => InitializeComponent();
    private void OverlaySettings_Click(object? sender, RoutedEventArgs e)
    {
        if (settingsWindow is not null) { settingsWindow.Activate(); return; }
        if (DataContext is not MainWindowViewModel model || TopLevel.GetTopLevel(this) is not Window owner) return;
        var definition = OverlaySettingsCategoryCatalog.All.Single(c => c.Category == OverlaySettingsCategory.Firegroups);
        settingsWindow = new OverlayCategorySettingsWindow(definition, model);
        settingsWindow.Closed += (_, _) => settingsWindow = null;
        settingsWindow.Show(owner);
    }
}
