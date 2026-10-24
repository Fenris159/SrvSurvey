using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class MiningWorkspaceViewModelTests
{
    /// <summary>Projects lowercase journal symbols as capitalized commodity names on the live notification overlay.</summary>
    [Fact]
    public void MiningNotificationsCapitalizeRefinedCommodityNames()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            using var workspace = new MiningWorkspaceViewModel(
                directory,
                new Resolver(),
                new BookmarksViewModel(directory),
                clock: new Clock()
            );
            var context = new JournalSessionState();
            JournalEventEnvelope load = FiregroupsWorkspaceViewModelTests.Event(
                """{"event":"LoadGame","FID":"F-NOTICE-TEST","Commander":"Test","Ship":"python"}"""
            );
            context.Apply(load);
            var status = new EliteStatus { Flags = StatusFlags.InMainShip };
            workspace.Apply(new(null, [load], status, null, null, null, [], true), context, null, status);
            workspace.StartCommand.Execute(null);
            workspace.Settings.Thresholds.Clear();
            using var overlay = new MiningActivityOverlayViewModel(workspace, false);
            string[] commodities = ["gold", "silver", "palladium"];
            JournalEventEnvelope[] events = commodities
                .Select(commodity =>
                    FiregroupsWorkspaceViewModelTests.Event(
                        $$"""{"event":"MiningRefined","timestamp":"2026-09-06T12:00:01Z","Type":"{{commodity}}"}"""
                    )
                )
                .ToArray();

            JournalEventEnvelope prospect = FiregroupsWorkspaceViewModelTests.Event(
                """
                {"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:00:01Z",
                 "Materials":[{"Name":"gold","Proportion":35},{"Name":"silver","Proportion":25},
                              {"Name":"palladium","Proportion":20}],"MotherlodeMaterial":"lowtemperaturediamond"}
                """
            );
            workspace.Apply(
                new(null, [prospect, .. events], status, null, null, null, [], false),
                context,
                null,
                status
            );

            Assert.Equal(["Palladium ×1", "Silver ×1", "Gold ×1"], overlay.Refined.Select(notice => notice.Text));
            MiningProspectOverlayRowViewModel row = Assert.Single(overlay.Prospects);
            Assert.Equal(
                ["Gold 35.0%", "Silver 25.0%", "Palladium 20.0%", "Core: Low Temperature Diamonds"],
                row.Materials.Select(material => material.Text)
            );
            Assert.Contains(
                "Gold 35.0% · Silver 25.0% · Palladium 20.0% · Core: Low Temperature Diamonds",
                row.Summary
            );
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    /// <summary>Unchanged refreshes retain rows, while a commander change clears the previous cargo.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverlayRefreshRetainsUnchangedMiningRows(bool includeCommanderEvent)
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

            JournalEventEnvelope nextCommander = FiregroupsWorkspaceViewModelTests.Event(
                """{"event":"LoadGame","FID":"F-OTHER-CMDR","Commander":"Other","Ship":"python"}"""
            );
            context.Apply(nextCommander);
            workspace.Apply(
                new(null, includeCommanderEvent ? [nextCommander] : [], status, null, null, null, [], false),
                context,
                null,
                status
            );
            Assert.Empty(workspace.Cargo);
            Assert.Empty(cargo.Items);
            Assert.False(cargo.HasItems);
            Assert.Equal(0, cargo.FillPercentage);
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
