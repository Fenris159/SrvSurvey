using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void ScrollToLegacyImport_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        LegacyImportSection.BringIntoView();
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
        var folder = folders.Count > 0 ? folders[0] : null;
        if (folder is not null)
        {
            viewModel.LegacyProfileSourcePath = folder.Path.LocalPath;
        }
    }

    private async void ChooseJournalFolder_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        var folder = await ChooseFolderAsync(
            "Choose the Elite Dangerous journal folder");
        if (folder is not null && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.JournalSettings.DirectoryPath = folder;
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

    private async void ChooseCodexCacheFolder_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        var folder = await ChooseFolderAsync(
            "Choose the Codex image cache folder");
        if (folder is not null && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CodexImages.CacheDirectory = folder;
        }
    }

    private async void ChooseLocalFloraFolder_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        var folder = await ChooseFolderAsync(
            "Choose the local flora image folder");
        if (folder is not null && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CodexImages.LocalFloraDirectory = folder;
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
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    private async void OpenGreenGasGiantGuide_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await OpenSettingsUriAsync(
            new Uri(RavenColonialClient.WebsiteUri, "#ggg"),
            "the Raven Colonial Green Gas Giant guide");
    }

    private async void OpenInaraApiKeyPage_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await OpenSettingsUriAsync(
            new Uri("https://inara.cz/elite/cmdr-settings-api/"),
            "the Inara API key page");
    }

    private async Task OpenSettingsUriAsync(Uri uri, string description)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher
                ?? throw new InvalidOperationException(
                    "The desktop link launcher is not available.");
            var launched = await launcher.LaunchUriAsync(uri);
            viewModel.ReportSettingsLinkResult(description, launched);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or NotSupportedException)
        {
            viewModel.ReportSettingsLinkResult(
                description,
                false,
                exception.Message);
        }
    }

}
