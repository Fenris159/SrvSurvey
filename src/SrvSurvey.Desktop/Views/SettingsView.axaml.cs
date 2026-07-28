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

    private void BeginVrAdjustment_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            _ = viewModel.BeginVrAdjustment();
        }
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
        return folders.FirstOrDefault()?.Path.LocalPath;
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
