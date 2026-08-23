using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Network;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Runtime;

namespace SrvSurvey.Desktop.Views;

public sealed partial class ColonizationView : UserControl
{
    private const string DesktopLinkLauncherUnavailable =
        "The desktop link launcher is not available.";
    private const string DefaultBrowserDeclined =
        "The default browser declined the request.";

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
            DesktopExternalEffectPolicy.ThrowIfDisabled();
            var launcher = TopLevel.GetTopLevel(this)?.Launcher
                ?? throw new InvalidOperationException(
                    DesktopLinkLauncherUnavailable);
            if (!await launcher.LaunchUriAsync(
                    new Uri(RavenColonialClient.WebsiteUri, "user")))
            {
                throw new InvalidOperationException(
                    DefaultBrowserDeclined);
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
            DesktopExternalEffectPolicy.ThrowIfDisabled();
            var launcher = TopLevel.GetTopLevel(this)?.Launcher
                ?? throw new InvalidOperationException(
                    DesktopLinkLauncherUnavailable);
            if (!await launcher.LaunchUriAsync(
                    new Uri(RavenColonialClient.WebsiteUri, "build")))
            {
                throw new InvalidOperationException(
                    DefaultBrowserDeclined);
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
            DesktopExternalEffectPolicy.ThrowIfDisabled();
            var launcher = TopLevel.GetTopLevel(this)?.Launcher
                ?? throw new InvalidOperationException(
                    DesktopLinkLauncherUnavailable);
            var buildId = Uri.EscapeDataString(
                viewModel.Colonization.ProjectEditor.CreatedProjectId);
            if (!await launcher.LaunchUriAsync(new Uri(
                    RavenColonialClient.WebsiteUri,
                    $"#build={buildId}")))
            {
                throw new InvalidOperationException(
                    DefaultBrowserDeclined);
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
                WellKnownUris.ColonisationWiki,
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
            DesktopExternalEffectPolicy.ThrowIfDisabled();
            var launcher = TopLevel.GetTopLevel(this)?.Launcher
                ?? throw new InvalidOperationException(
                    DesktopLinkLauncherUnavailable);
            if (!await launcher.LaunchUriAsync(uri))
            {
                throw new InvalidOperationException(
                    DefaultBrowserDeclined);
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
