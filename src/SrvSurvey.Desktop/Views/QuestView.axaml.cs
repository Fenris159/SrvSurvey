using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class QuestView : UserControl
{
    public QuestView()
    {
        InitializeComponent();
    }

    private async void ImportDevelopmentFolder_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null
            || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select folder containing quest definition files",
                AllowMultiple = false,
            });
        var folder = folders.FirstOrDefault();
        if (folder is not null)
        {
            await viewModel.QuestWorkspace.Developer.ImportFolderAsync(
                folder.Path.LocalPath);
        }
    }
}
