using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class OverlaySettingsView : UserControl
{
    public OverlaySettingsView()
    {
        InitializeComponent();
    }

    private void BeginVrAdjustment_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            _ = viewModel.BeginVrAdjustment();
        }
    }

    private async void ExportHumanSiteTemplates_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null
            || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export the settlement template catalog",
                SuggestedFileName = "humanSiteTemplates.json",
                FileTypeChoices =
                [
                    new FilePickerFileType("JSON catalog")
                    {
                        Patterns = ["*.json"],
                        MimeTypes = ["application/json"],
                    },
                ],
            });
        if (file is not null)
        {
            await viewModel.HumanSite.TemplateAuthor.ExportAsync(
                file.Path.LocalPath);
        }
    }
}
