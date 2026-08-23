using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Network;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Runtime;

namespace SrvSurvey.Desktop.Views;

public sealed partial class SettingsView : UserControl
{
    private const string SearchHighlightClass = "settings-search-highlight";
    private Control? highlightedControl;
    private int searchHighlightVersion;

    public SettingsView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => ClearSearchHighlight();
    }

    private async void ScrollToLegacyImport_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SettingsWorkspace.SelectCategory("data");
        }

        await Dispatcher.UIThread.InvokeAsync(
            static () => { },
            DispatcherPriority.Loaded);
        LegacyImportSection.BringIntoView();
        LegacyProfilePathTextBox.Focus();
    }

    private void OpenThemeWorkspace_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SelectedNavigation = viewModel.NavigationItems.Single(
                item => item.Key == "theme");
        }
    }

    private async void SettingsSearchResult_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is Button
            {
                DataContext: SettingsSearchResultViewModel result,
            })
        {
            await OpenSearchResultAsync(result);
        }
    }

    private async void SettingsSearchBox_KeyDown(
        object? sender,
        KeyEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        switch (eventArgs.Key)
        {
            case Key.Down:
                viewModel.SettingsWorkspace.MoveSearchSelection(1);
                eventArgs.Handled = true;
                break;
            case Key.Up:
                viewModel.SettingsWorkspace.MoveSearchSelection(-1);
                eventArgs.Handled = true;
                break;
            case Key.Enter:
                {
                    var result = viewModel.SettingsWorkspace.SelectedSearchResult;
                    if (result is not null)
                    {
                        await OpenSearchResultAsync(result);
                    }

                    eventArgs.Handled = true;
                    break;
                }
            case Key.Escape:
                viewModel.SettingsWorkspace.ClearSearch();
                SettingsSearchBox.Focus();
                eventArgs.Handled = true;
                break;
        }
    }

    private async Task OpenSearchResultAsync(
        SettingsSearchResultViewModel result)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.SettingsWorkspace.ActivateSearchResult(result);
        await Dispatcher.UIThread.InvokeAsync(
            static () => { },
            DispatcherPriority.Loaded);

        if (result.TargetControlName == nameof(ShortcutBindingsExpander))
        {
            ShortcutBindingsExpander.IsExpanded = true;
        }

        var target = this.FindControl<Control>(result.TargetControlName);
        var highlight = this.FindControl<Control>(result.HighlightControlName);
        target?.BringIntoView();
        target?.Focus();
        if (highlight is null)
        {
            return;
        }

        ClearSearchHighlight();
        var highlightVersion = searchHighlightVersion;
        highlightedControl = highlight;
        highlight.Classes.Add(SearchHighlightClass);
        await Task.Delay(TimeSpan.FromSeconds(1.4), CancellationToken.None);
        if (highlightVersion == searchHighlightVersion)
        {
            ClearSearchHighlight();
        }
    }

    private void ClearSearchHighlight()
    {
        searchHighlightVersion++;
        highlightedControl?.Classes.Remove(SearchHighlightClass);
        highlightedControl = null;
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
            WellKnownUris.InaraCommanderApiSettings,
            "the Inara API key page");
    }

    private async void ConfigureEddnSharing_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner
            || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var dialog = new EddnIntegrationDialog(viewModel.NetworkPrivacy);
        await dialog.ShowDialog<bool>(owner);
    }

    private async Task OpenSettingsUriAsync(Uri uri, string description)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        try
        {
            DesktopExternalEffectPolicy.ThrowIfDisabled();
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
