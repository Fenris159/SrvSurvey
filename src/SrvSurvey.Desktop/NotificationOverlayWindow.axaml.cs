using Avalonia.Controls;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class NotificationOverlayWindow : Window
{
    public NotificationOverlayWindow()
        : this(new NotificationViewModel(new NotificationSettingsStore(
            AppDataPaths.ResolveCurrent().UiSettingsPath)))
    {
    }

    public NotificationOverlayWindow(NotificationViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
