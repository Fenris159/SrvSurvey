using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ColonizationOverlayStabilityTests
{
    private static readonly string[] CommodityNames =
    [
        "Aluminium",
        "Building Fabricators",
        "Computer Components",
        "Copper",
        "Food Cartridges",
        "Fruit and Vegetables",
        "Military Grade Fabrics",
        "Polymers",
        "Power Generators",
        "Steel",
        "Surface Stabilisers",
        "Survival Equipment",
    ];

    /// <summary>Unchanged cargo and routine game-status updates must keep the live panel and its columns stationary.</summary>
    [AvaloniaTheory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(true, 1.5)]
    public void StationaryCargoRetainsGeometryAcrossHostPolls(bool refreshStatus, double scaling)
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        viewModel.ApplyPreferences(ColonizationOverlayPreferences.Default with { ShowFleetCarrierDelta = true });
        ColonizationCommodityPlan plan = CreatePlan();
        var status = new EliteStatus { Flags = StatusFlags.InMainShip, GuiFocus = GuiFocus.InternalPanel };
        viewModel.Apply(plan, status);
        var registry = new OverlayWindowRegistry();
        using var host = new HostedOverlayTestHarness(registry);
        host.Prepare = window =>
        {
            window.SetRenderScaling(scaling);
            return new OverlayPreparationResult(true, true, "Prepared");
        };
        using var coordinator = new ColonizationCommodityOverlayCoordinator(viewModel, host.Session);
        Window window = Assert.Single(host.PreparedWindows);
        Settle(window);
        Size initialSize = window.ClientSize;
        var sizeChanges = new List<Size>();
        window.SizeChanged += (_, _) => sizeChanges.Add(window.ClientSize);
        var observations = new List<string>();
        for (int index = 0; index < 12; index++)
        {
            if (refreshStatus)
            {
                viewModel.Apply(
                    plan with
                    {
                        Rows = plan.Rows.ToArray(),
                        ProjectNames = plan.ProjectNames.ToArray(),
                    },
                    status
                );
            }
            host.Tick();
            Settle(window);
            Grid columns = window
                .GetVisualDescendants()
                .OfType<Grid>()
                .Single(grid => grid.Name == "CommodityColumnHeaders");
            observations.Add(
                $"{window.Position}/{window.ClientSize}/{columns.Bounds}/{string.Join(',', columns.ColumnDefinitions.Select(column => column.ActualWidth))}"
            );
        }
        Assert.True(observations.Distinct().Count() == 1, string.Join(Environment.NewLine, observations));
        Assert.True(
            sizeChanges.All(size => size == initialSize),
            $"Transient window resizing: {string.Join(';', sizeChanges)}"
        );
    }

    [AvaloniaFact]
    public void SavedPanelHeightDoesNotPreventShrinkingWithTheCommodityList()
    {
        const string plotter = "PlotBuildCommodities";
        var model = new ColonizationCommodityOverlayViewModel();
        model.ApplyPreferences(ColonizationOverlayPreferences.Default with { CollapseCoveredGroups = false });
        var window = new ColonizationCommodityOverlayWindow(model);
        var layout = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                [plotter] = OverlayLayoutCatalog.GetRequired(plotter).DefaultPlacement with
                {
                    SizeOverride = new(500, 800),
                },
            },
            null,
            null
        );
        try
        {
            OverlayThemeResources.Apply(window, layout, plotter, new OverlayWindowRegistry());
            model.Apply(CreatePlan(), null);
            window.Show();
            Settle(window);
            double expanded = window.ClientSize.Height;
            model.Apply(CreatePlan() with { Rows = CreatePlan().Rows.Take(1).ToArray() }, null);
            Settle(window);
            Assert.True(
                window.ClientSize.Height < expanded - 50,
                $"Height stayed at {window.ClientSize.Height} after removing rows from {expanded}."
            );
        }
        finally
        {
            window.Close();
        }
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static ColonizationCommodityPlan CreatePlan() =>
        new()
        {
            Title = "Chomsky Enterprise",
            ProjectNames = ["Chomsky Enterprise"],
            FleetCarriers = [new ColonizationFleetCarrier { MarketId = 1, Name = "DIRE ECLIPSE (N4W-T0Z)" }],
            TotalRemaining = 8261,
            Rows = CommodityNames
                .Select(
                    (name, index) =>
                        new ColonizationCommodityPlanRow
                        {
                            Commodity = name,
                            DisplayName = name,
                            Category = index < 4 ? "Metals" : "Chemicals",
                            Needed = 2414 + index,
                            OnFleetCarriers = 100,
                        }
                )
                .ToArray(),
        };
}
