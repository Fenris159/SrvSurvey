using Avalonia.Headless.XUnit;
using SrvSurvey.Desktop;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class DiagnosticsUpdateNavigationTests
{
    [AvaloniaFact]
    public void UpdateNavigationSelectsUpdatesTabBeforeShowingDiagnostics()
    {
        using var viewModel = new MainWindowViewModel(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"));
        var window = new MainWindow(viewModel);

        try
        {
            window.NavigateToReleaseUpdates();

            Assert.True(viewModel.IsDiagnosticsSelected);
            Assert.Equal(
                DiagnosticsWorkspaceTab.Updates,
                viewModel.SelectedDiagnosticsTab);
            Assert.Equal("Updates", viewModel.DiagnosticsTabTitle);
            Assert.True(viewModel.IsDiagnosticsUpdatesSelected);
            Assert.False(viewModel.IsDiagnosticsSourceSelected);
        }
        finally
        {
            window.Close();
        }
    }
}
