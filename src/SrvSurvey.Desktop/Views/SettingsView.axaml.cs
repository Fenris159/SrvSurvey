using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void ChooseLegacyProfileFolder_Click(
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
                Title = "Choose the original SrvSurvey profile folder",
                AllowMultiple = false,
            });
        var folder = folders.FirstOrDefault();
        if (folder is not null)
        {
            viewModel.LegacyProfileSourcePath = folder.Path.LocalPath;
        }
    }

    private async void ChooseScreenshotSourceFolder_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        var folder = await ChooseFolderAsync(
            "Choose the Elite Dangerous screenshot folder");
        if (folder is not null && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ScreenshotProcessing.SourceFolder = folder;
        }
    }

    private async void ChooseScreenshotTargetFolder_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        var folder = await ChooseFolderAsync(
            "Choose the converted screenshot folder");
        if (folder is not null && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ScreenshotProcessing.TargetFolder = folder;
        }
    }

    private async Task<string?> ChooseFolderAsync(string title)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            return null;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
            });
        return folders.FirstOrDefault()?.Path.LocalPath;
    }
}
