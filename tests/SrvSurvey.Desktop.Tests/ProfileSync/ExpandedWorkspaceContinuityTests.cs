using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ProfileSync;

public sealed class ExpandedWorkspaceContinuityTests
{
    [Fact]
    public async Task SelectedExplorationColonizationAndDiagnosticsTabsSurviveBackupAndRestart()
    {
        using var source = new ProfileSyncTestRoot();
        using var target = new ProfileSyncTestRoot();
        await using (
            MainWindowViewModel original = MainWindowViewModelTestBuilder.Create(
                source.Path("data", "missing-journals"),
                builder => builder.WithAppDataPaths(source.Paths)
            )
        )
        {
            original.SelectedExplorationTabIndex = 1;
            original.Colonization.SelectedWorkspaceTabIndex = 2;
            original.SelectedDiagnosticsTab = DiagnosticsWorkspaceTab.Inspector;
        }

        target.Store.Apply(source.Store.Capture());
        await using MainWindowViewModel restored = MainWindowViewModelTestBuilder.Create(
            target.Path("data", "missing-journals"),
            builder => builder.WithAppDataPaths(target.Paths)
        );

        Assert.Equal(1, restored.SelectedExplorationTabIndex);
        Assert.Equal(2, restored.Colonization.SelectedWorkspaceTabIndex);
        Assert.Equal(DiagnosticsWorkspaceTab.Inspector, restored.SelectedDiagnosticsTab);
    }
}
