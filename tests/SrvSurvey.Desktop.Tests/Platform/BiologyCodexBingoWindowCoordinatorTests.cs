using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class BiologyCodexBingoWindowCoordinatorTests
{
    /// <summary>Searching from Bingo must keep the same window and selection until the user closes it.</summary>
    [AvaloniaFact]
    public async Task FindNearestKeepsBingoOpenUntilExplicitlyClosed()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SrvSurvey-bingo-lifetime-" + Guid.NewGuid().ToString("N"));
        var store = new CommanderCodexStore(directory);
        var catalog = ExobiologyReferenceCatalog.LoadEmbedded();
        using var model = new BiologyCodexBingoViewModel(
            store,
            catalog,
            new CanonnCodexChallengeImporter(new CanonnCodexChallengeClient(), store, catalog),
            new CommanderCodexJournalImporter(directory, store),
            new CodexDiscoveryLocationClient()
        );
        var owner = new Window();
        int searches = 0;
        using var coordinator = new BiologyCodexBingoWindowCoordinator(
            model,
            owner,
            _ =>
            {
                searches++;
                return Task.CompletedTask;
            }
        );
        try
        {
            owner.Show();
            await coordinator.ShowOrActivateAsync();
            BiologyCodexBingoWindow window = Assert.Single(owner.OwnedWindows.OfType<BiologyCodexBingoWindow>());
            model.SelectedNode = model.RootNodes[0].Find("entry:2310101");
            Assert.NotNull(model.SelectedNode);

            await model.FindNearestAsync();

            Assert.Equal(1, searches);
            Assert.True(coordinator.IsVisible);
            Assert.True(window.IsVisible);
            Assert.Same(window, Assert.Single(owner.OwnedWindows));
            // Parent and missing-entry actions retain the same window as exact-entry searches.
            model.SelectedNode = FindParent(model.RootNodes[0]);
            Assert.NotNull(model.SelectedNode);
            await model.FindNearestAsync();
            await model.FindMissingVariantsAsync();
            Assert.Equal(3, searches);
            Assert.True(window.IsVisible);
            Assert.Same(window, Assert.Single(owner.OwnedWindows));
            window.Close();
            Assert.False(coordinator.IsVisible);
        }
        finally
        {
            coordinator.Dispose();
            owner.Close();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static CodexBingoTreeNodeViewModel? FindParent(CodexBingoTreeNodeViewModel node) =>
        node.Definition.Entry is null
        && node.Children.Count > 1
        && node.Children.All(child => child.Definition.Entry is not null)
            ? node
            : node.Children.Select(FindParent).FirstOrDefault(parent => parent is not null);
}
