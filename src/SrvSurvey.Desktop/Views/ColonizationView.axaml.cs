using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class ColonizationView : UserControl
{
    public ColonizationView()
    {
        InitializeComponent();
    }

    private async void OpenRavenApiKeyPage_Click(
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
            if (!await launcher.LaunchUriAsync(
                    new Uri(RavenColonialClient.WebsiteUri, "user")))
            {
                throw new InvalidOperationException(
                    "The default browser declined the request.");
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or NotSupportedException)
        {
            viewModel.Colonization.ReportLinkFailure(exception.Message);
        }
    }

    private async void OpenRavenBuilds_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher
                ?? throw new InvalidOperationException(
                    "The desktop link launcher is not available.");
            if (!await launcher.LaunchUriAsync(
                    new Uri(RavenColonialClient.WebsiteUri, "build")))
            {
                throw new InvalidOperationException(
                    "The default browser declined the request.");
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or UriFormatException
                or NotSupportedException)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.Colonization.ReportLinkFailure(exception.Message);
            }
        }
    }

    private async void OpenCreatedProject_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || string.IsNullOrWhiteSpace(
                viewModel.Colonization.ProjectEditor.CreatedProjectId))
        {
            return;
        }

        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher
                ?? throw new InvalidOperationException(
                    "The desktop link launcher is not available.");
            var buildId = Uri.EscapeDataString(
                viewModel.Colonization.ProjectEditor.CreatedProjectId);
            if (!await launcher.LaunchUriAsync(new Uri(
                    RavenColonialClient.WebsiteUri,
                    $"#build={buildId}")))
            {
                throw new InvalidOperationException(
                    "The default browser declined the request.");
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or UriFormatException
                or NotSupportedException)
        {
            viewModel.Colonization.ProjectEditor.ReportLinkFailure(
                exception.Message);
        }
    }

    private async void OpenRavenSystem_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || viewModel.Colonization.SystemEditor.LoadedSystemAddress
                is not { } systemAddress)
        {
            return;
        }

        await OpenSystemEditorUriAsync(
            new Uri(RavenColonialClient.WebsiteUri, $"#sys={systemAddress}"),
            viewModel);
    }

    private async void OpenRavenUpdaterGuide_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await OpenSystemEditorUriAsync(
                new Uri(
                    "https://github.com/njthomson/SrvSurvey/wiki/Colonisation-System-Update-Tool"),
                viewModel);
        }
    }

    private async void OpenRavenVisualizer_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await OpenSystemEditorUriAsync(
                new Uri(RavenColonialClient.WebsiteUri, "vis"),
                viewModel);
        }
    }

    private async Task OpenSystemEditorUriAsync(
        Uri uri,
        MainWindowViewModel viewModel)
    {
        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher
                ?? throw new InvalidOperationException(
                    "The desktop link launcher is not available.");
            if (!await launcher.LaunchUriAsync(uri))
            {
                throw new InvalidOperationException(
                    "The default browser declined the request.");
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or UriFormatException
                or NotSupportedException)
        {
            viewModel.Colonization.SystemEditor.ReportLinkFailure(
                exception.Message);
        }
    }
}
