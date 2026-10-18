using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class MiningWorkspaceViewModelTests
{
    /// <summary>Reading cargo repeatedly and changing unrelated mining text must preserve bound row collections.</summary>
    [Fact]
    public void OverlayRefreshRetainsUnchangedMiningRows()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            using var workspace = new MiningWorkspaceViewModel(
                directory,
                new Resolver(),
                new BookmarksViewModel(directory)
            );
            using var cargo = new MiningCargoOverlayViewModel(workspace);
            using var activity = new MiningActivityOverlayViewModel(workspace, false);
            var context = new JournalSessionState();
            JournalEventEnvelope load = FiregroupsWorkspaceViewModelTests.Event(
                """{"event":"LoadGame","FID":"F-OVERLAY-TEST","Commander":"Test","Ship":"python"}"""
            );
            context.Apply(load);
            var status = new EliteStatus { Flags = StatusFlags.InMainShip };
            CargoSnapshot snapshot = new(
                DateTimeOffset.UtcNow,
                "Cargo",
                "Ship",
                3,
                [new CargoItem("platinum", "Platinum", 3, 0)]
            );
            workspace.Apply(new(null, [], status, null, null, null, [], false), context, snapshot, status);
            IReadOnlyList<MiningCargoOverlayItemViewModel> cargoRows = cargo.Items;
            IReadOnlyList<MiningProspectOverlayRowViewModel> prospects = activity.Prospects;
            IReadOnlyList<MiningNotice> refined = activity.Refined;
            Assert.Single(cargoRows);
            Assert.Same(cargoRows, cargo.Items);
            workspace.Apply(new(null, [], status, null, null, null, [], false), context, snapshot, status);
            Assert.Same(cargoRows, cargo.Items);
            Assert.Same(prospects, activity.Prospects);
            Assert.Same(refined, activity.Refined);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
