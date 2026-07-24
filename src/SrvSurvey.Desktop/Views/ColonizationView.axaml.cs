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

    private async void OpenRavenBuilds_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher
                ?? throw new InvalidOperationException(
                    "The desktop link launcher is not available.");
            await launcher.LaunchUriAsync(
                new Uri(RavenColonialClient.WebsiteUri, "build"));
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
            await launcher.LaunchUriAsync(new Uri(
                RavenColonialClient.WebsiteUri,
                $"#build={buildId}"));
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
}
