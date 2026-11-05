using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    /// <summary>Roster changes retain checked identities and never include newly discovered carriers implicitly.</summary>
    [Fact]
    public void CarrierCheckboxSelectionSurvivesRenamingReorderingAndAddedCarriersWithoutWidening()
    {
        var selection = new ColonizationCarrierSelectionViewModel();
        selection.UpdateCarriers(SevenCheckboxCarriers());
        selection.ClearCommand.Execute(null);
        foreach (long id in new long[] { 3_700_000_001, 3_700_000_003, 3_700_000_007 })
        {
            selection.Items.Single(item => item.MarketId == id).IsSelected = true;
        }
        selection.UpdateCarriers([
            new() { MarketId = 3_700_000_008, Name = "New carrier" },
            .. SevenCheckboxCarriers()
                .Reverse()
                .Select(carrier => carrier with { DisplayName = "Renamed " + carrier.Name }),
        ]);
        Assert.Equal(
            new long[] { 3_700_000_001, 3_700_000_003, 3_700_000_007 },
            selection.ResolveCarriers().Select(carrier => carrier.MarketId).Order()
        );
        Assert.Equal("3 carriers selected", selection.SelectionNote);
        Assert.False(selection.Items.Single(item => item.MarketId == 3_700_000_008).IsSelected);
        selection.UpdateCarriers(SevenCheckboxCarriers().Where(carrier => carrier.MarketId != 3_700_000_003).ToArray());
        Assert.Equal(new long[] { 3_700_000_001, 3_700_000_007 }, selection.CaptureSelectedIds().Order());
        Assert.Equal("2 carriers selected", selection.SelectionNote);
    }

    [Fact]
    public async Task ThreeCheckedCarriersDriveLoadedCargoMarketNeedsBulkLinksAndCommanderPersistence()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new([Project("build-1", "Port", 1_000)], [], null, SevenCheckboxCarriers()),
        };
        using (ColonizationViewModel model = Create(client))
        {
            model.IsEnabled = true;
            await model.SetCommanderAsync("Test Cmdr");
            CheckThreeCarriers(model.CarrierSelection);
            Assert.Equal("30.00%", Assert.Single(model.Projects).CarrierCoverageText);
            using ColonizationShoppingViewModel shopping = model.CreateShopping(
                model.Projects.Select(row => row.Project).ToArray(),
                new ColonizationShoppingViewModelTests.SearchStub([])
            );
            Assert.Equal(700, Assert.Single(shopping.Needs).Remaining);
            Assert.Equal(
                new long[] { 3_700_000_001, 3_700_000_003, 3_700_000_007 },
                shopping.CarrierSelection.CaptureSelectedIds().Order()
            );
            model.ProjectBuildFilter = model.ProjectBuildOptions.Single(option =>
                option.Value == ColonizationProjectBuildFilter.Building
            );
            await model.LinkCarrierToSelectedProjectsAsync();
            Assert.Equal(
                new long[] { 3_700_000_001, 3_700_000_003, 3_700_000_007 },
                client.ProjectCarrierRequests.Select(request => request.MarketId).Order()
            );
            Assert.Equal(
                new long[] { 3_700_000_001, 3_700_000_003, 3_700_000_007 },
                Assert.Single(model.Projects).Project.LinkedFleetCarriers.Select(carrier => carrier.MarketId).Order()
            );
            Assert.Equal(300, Assert.Single(model.CommodityOverlay.Plan.Rows).OnFleetCarriers);
            Assert.Equal(700, model.CommodityOverlay.Plan.FleetCarrierDeficit);
            shopping.CarrierSelection.Items.Single(item => item.MarketId == 3_700_000_003).IsSelected = false;
            Assert.Equal(800, Assert.Single(shopping.Needs).Remaining);
            Assert.Equal("20.00%", Assert.Single(model.Projects).CarrierCoverageText);
            Assert.Equal("2 carriers selected", model.CarrierSelection.SelectionNote);
        }
        using ColonizationViewModel reopened = Create(client);
        await reopened.SetCommanderAsync("Test Cmdr");
        Assert.Equal(
            new long[] { 3_700_000_001, 3_700_000_007 },
            reopened.CarrierSelection.CaptureSelectedIds().Order()
        );
        Assert.Equal("20.00%", Assert.Single(reopened.Projects).CarrierCoverageText);
        await reopened.SetCommanderAsync("Other Cmdr");
        Assert.Equal(7, reopened.CarrierSelection.SelectedCount);
        await reopened.SetCommanderAsync("Test Cmdr");
        Assert.Equal(
            new long[] { 3_700_000_001, 3_700_000_007 },
            reopened.CarrierSelection.CaptureSelectedIds().Order()
        );
    }

    [Fact]
    public async Task ClearedCarrierCheckboxesStayEmptyAcrossRestartAndNeverExpandToAll()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new([Project("build-1", "Port", 1_000)], [], null, SevenCheckboxCarriers()),
        };
        using (ColonizationViewModel model = Create(client))
        {
            model.IsEnabled = true;
            await model.SetCommanderAsync("Test Cmdr");
            model.CarrierSelection.ClearCommand.Execute(null);
            model.ProjectBuildFilter = model.ProjectBuildOptions.Single(option =>
                option.Value == ColonizationProjectBuildFilter.Building
            );
            Assert.Equal("0 carriers selected", model.CarrierSelection.SelectionNote);
            Assert.Equal("0.00%", Assert.Single(model.Projects).CarrierCoverageText);
            Assert.False(model.LinkCarrierToSelectedProjectsCommand.CanExecute(null));
            await model.LinkCarrierToSelectedProjectsAsync();
            Assert.Empty(client.ProjectCarrierRequests);
            using ColonizationShoppingViewModel shopping = model.CreateShopping(
                model.Projects.Select(row => row.Project).ToArray(),
                new ColonizationShoppingViewModelTests.SearchStub([])
            );
            Assert.Equal(1_000, Assert.Single(shopping.Needs).Remaining);
        }
        using ColonizationViewModel reopened = Create(client);
        await reopened.SetCommanderAsync("Test Cmdr");
        Assert.Empty(reopened.CarrierSelection.ResolveCarriers());
        Assert.Equal("0 carriers selected", reopened.CarrierSelection.SelectionNote);
    }

    [Fact]
    public async Task CarrierCheckboxSubsetSurvivesMarketProjectTagChanges()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new(
                [Project("first", "First", 1_000), Project("second", "Second", 1_000)],
                [],
                null,
                SevenCheckboxCarriers()
            ),
        };
        using ColonizationViewModel model = Create(client);
        model.MarketSearchFactory = () => new ColonizationShoppingViewModelTests.SearchStub([]);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        CheckThreeCarriers(model.CarrierSelection);
        model.SelectedWorkspaceTabIndex = 1;
        Assert.NotNull(model.MarketSearchWorkspace);
        Assert.Equal(1_700, Assert.Single(model.MarketSearchWorkspace.Needs).Remaining);
        model.Projects.Single(row => row.Project.BuildId == "second").IsShoppingSelected = false;
        Assert.Equal(700, Assert.Single(model.MarketSearchWorkspace.Needs).Remaining);
        Assert.Equal(
            new long[] { 3_700_000_001, 3_700_000_003, 3_700_000_007 },
            model.MarketSearchWorkspace.CarrierSelection.CaptureSelectedIds().Order()
        );
        model.Projects.Single(row => row.Project.BuildId == "second").IsShoppingSelected = true;
        Assert.Equal(1_700, Assert.Single(model.MarketSearchWorkspace.Needs).Remaining);
        Assert.Equal("3 carriers selected", model.CarrierSelection.SelectionNote);
    }

    /// <summary>Selects the hand-picked first, third and seventh carriers used by cargo and persistence scenarios.</summary>
    private static void CheckThreeCarriers(ColonizationCarrierSelectionViewModel selection)
    {
        selection.ClearCommand.Execute(null);
        foreach (long id in new long[] { 3_700_000_001, 3_700_000_003, 3_700_000_007 })
        {
            selection.Items.Single(item => item.MarketId == id).IsSelected = true;
        }
    }

    private static ColonizationFleetCarrier[] SevenCheckboxCarriers() =>
        Enumerable
            .Range(1, 7)
            .Select(index => new ColonizationFleetCarrier
            {
                MarketId = 3_700_000_000L + index,
                Name = $"ABC-00{index}",
                DisplayName = $"Carrier {index}",
                Cargo = new() { ["steel"] = 100 },
            })
            .ToArray();
}

public sealed partial class ColonizationProjectEditorViewModelTests
{
    /// <summary>Manual and planned creation wait for a real build ID before linking the three checked carrier identities.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CarrierCheckboxCreationLinksOnlyTheCheckedSubsetAfterTheBuildIdExists(bool fromPlan)
    {
        var response = new TaskCompletionSource<ColonizationProject?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new StubRavenColonialClient { CreationResponse = response.Task, CreationStarted = started };
        ColonizationProject? installed = null;
        ColonizationProjectEditorViewModel editor = await PrepareCarrierCreationAsync(
            client,
            fromPlan,
            project =>
            {
                installed = project;
                return Task.CompletedTask;
            }
        );
        editor.UpdateCarrierOptions(
            Enumerable
                .Range(1, 7)
                .Select(index => new ColonizationFleetCarrier
                {
                    MarketId = 3_700_000_000L + index,
                    Name = $"ABC-00{index}",
                })
                .ToArray()
        );
        editor.IncludeCarrierLinks = true;
        editor.CarrierSelection.ClearCommand.Execute(null);
        foreach (long id in new long[] { 3_700_000_001, 3_700_000_003, 3_700_000_007 })
        {
            editor.CarrierSelection.Items.Single(item => item.MarketId == id).IsSelected = true;
        }
        await editor.ReviewAsync();
        Assert.Contains("Link 3 carriers after creation.", editor.ConfirmationSummary);
        Task publish = editor.ConfirmCreateAsync();
        await started.Task;
        int premature = client.CreatedCarrierLinks.Count;
        response.SetResult(CreatedCarrierBuild());
        await publish;
        Assert.Equal(0, premature);
        Assert.Equal(
            new[]
            {
                ("created-build", 3_700_000_001L),
                ("created-build", 3_700_000_003L),
                ("created-build", 3_700_000_007L),
            },
            client.CreatedCarrierLinks.OrderBy(link => link.MarketId)
        );
        Assert.NotNull(installed);
        Assert.Equal(
            new long[] { 3_700_000_001, 3_700_000_003, 3_700_000_007 },
            installed.LinkedFleetCarriers.Select(carrier => carrier.MarketId).Order()
        );
    }
}
