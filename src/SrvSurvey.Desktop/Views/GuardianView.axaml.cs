using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class GuardianView : UserControl
{
    public GuardianView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => ConnectClipboard();
        DetachedFromVisualTree += (_, _) => DisconnectClipboard();
        DataContextChanged += (_, _) => ConnectClipboard();
    }

    private void ConnectClipboard()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Guardian.SetClipboardWriter(WriteClipboardAsync);
        }
    }

    private void DisconnectClipboard()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Guardian.SetClipboardWriter(null);
        }
    }

    private async Task WriteClipboardAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard
            ?? throw new InvalidOperationException(
                "The desktop clipboard is not available.");
        await clipboard.SetTextAsync(text);
        await clipboard.FlushAsync();
    }

    private async void CopySystem_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.Guardian.CopySystemNameAsync();
        }
    }

    private async void CopyAddress_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.Guardian.CopySystemAddressAsync();
        }
    }

    private async void CopyPosition_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.Guardian.CopyGalacticPositionAsync();
        }
    }

    private async void CopySurface_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.Guardian.CopySurfaceLocationAsync();
        }
    }

    private async void OpenRuinsGuide_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await OpenGuideAsync(
            "https://canonn.science/codex/ram-tahs-mission/",
            "mission 1");
    }

    private async void OpenLogsGuide_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await OpenGuideAsync(
            "https://canonn.science/codex/ram-tah-decrypting-the-guardian-logs/",
            "mission 2");
    }

    private async void OpenCanonn_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await OpenSelectedSiteLinkAsync(
            viewModel => viewModel.SelectedCanonnUri,
            "Canonn Signals");
    }

    private async void OpenSpansh_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await OpenSelectedSiteLinkAsync(
            viewModel => viewModel.SelectedSpanshUri,
            "Spansh");
    }

    private async void OpenEdsm_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await OpenSelectedSiteLinkAsync(
            viewModel => viewModel.SelectedEdsmUri,
            "EDSM");
    }

    private async void ExportGuardianTemplate_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Guardian template catalog",
            SuggestedFileName = "guardianSiteTemplates.json",
            DefaultExtension = "json",
            FileTypeChoices =
            [
                new FilePickerFileType("JSON catalog")
                {
                    Patterns = ["*.json"],
                },
            ],
        });
        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await viewModel.Guardian.TemplateAuthoring.ExportAsync(path);
        }
    }

    private async void CopyShareBundle_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.Guardian.CopyShareArchivePathAsync();
        }
    }

    private async void OpenShareFolder_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || viewModel.Guardian.ShareArchivePath is not string archivePath)
        {
            return;
        }

        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher
                ?? throw new InvalidOperationException(
                    "The desktop launcher is not available.");
            var directory = new DirectoryInfo(Path.GetDirectoryName(archivePath)!);
            var launched = await launcher.LaunchDirectoryInfoAsync(directory);
            viewModel.Guardian.ReportShareLaunch(launched
                ? "Opened the Guardian survey bundle folder."
                : "The Guardian survey bundle folder could not be opened.");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or IOException
                or NotSupportedException
                or UnauthorizedAccessException)
        {
            viewModel.Guardian.ReportShareLaunch(
                "The Guardian survey bundle folder could not be opened: "
                + exception.Message);
        }
    }

    private async void OpenShareDiscord_Click(
        object? sender,
        RoutedEventArgs eventArgs)
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
            var launched = await launcher.LaunchUriAsync(
                new Uri("https://discord.gg/9PhBwwDAbV"));
            viewModel.Guardian.ReportShareLaunch(launched
                ? "Opened the Guardian survey Discord invite."
                : "The Guardian survey Discord invite could not be opened.");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or NotSupportedException)
        {
            viewModel.Guardian.ReportShareLaunch(
                "The Guardian survey Discord invite could not be opened: "
                + exception.Message);
        }
    }

    private async Task OpenGuideAsync(string address, string label)
    {
        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher
                ?? throw new InvalidOperationException(
                    "The desktop link launcher is not available.");
            await launcher.LaunchUriAsync(new Uri(address));
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or UriFormatException
                or NotSupportedException)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.RamTah.ReportGuideLaunchFailure(
                    $"The {label} guide could not be opened: {exception.Message}");
            }
        }
    }

    private async Task OpenSelectedSiteLinkAsync(
        Func<GuardianViewModel, Uri?> addressSelector,
        string label)
    {
        if (DataContext is not MainWindowViewModel main
            || addressSelector(main.Guardian) is not { } address)
        {
            return;
        }

        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher
                ?? throw new InvalidOperationException(
                    "The desktop link launcher is not available.");
            var launched = await launcher.LaunchUriAsync(address);
            main.Guardian.ReportSelectedSiteLaunch(launched
                ? $"Opened the selected system at {label}."
                : $"The selected system could not be opened at {label}.");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or UriFormatException
                or NotSupportedException)
        {
            main.Guardian.ReportSelectedSiteLaunch(
                $"The selected system could not be opened at {label}: "
                    + exception.Message);
        }
    }
}
