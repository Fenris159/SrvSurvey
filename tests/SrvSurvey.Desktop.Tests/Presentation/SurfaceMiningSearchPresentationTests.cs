using System.Net;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class SurfaceMiningSearchPresentationTests
{
    [AvaloniaFact]
    public void SharedAcquireBranchesFollowMiningRowsWhenBodiesExpand()
    {
        SurfaceMiningSystemRowViewModel sharedFirst = new(
            "Terminus",
            10,
            [new SurfaceBodyLine(["IRI"], "A 1", new HashSet<string>(["IRI"]))]
        );
        SurfaceMiningSystemRowViewModel sharedSecond = new(
            "Terminus",
            10,
            [
                new SurfaceBodyLine(["MON"], "B 1", new HashSet<string>(["MON"])),
                new SurfaceBodyLine(["MON"], "B 2", new HashSet<string>(["MON"])),
            ]
        );
        SurfaceMiningSystemRowViewModel later = new(
            "Beta",
            15,
            [new SurfaceBodyLine(["MON"], "C 1", new HashSet<string>(["MON"]))]
        );
        SurfaceSellRowViewModel first = SharedSellRow("First Sell", [sharedFirst]);
        SurfaceSellRowViewModel second = SharedSellRow("Second Sell", [sharedSecond, later]);
        PowerplayAcquireClusterViewModel cluster = Assert.Single(
            PowerplayAcquireClusterViewModel.Group([first, second])
        );
        cluster.SellNodes[1].SelectCommand.Execute(null);
        var view = new PowerplayAcquireSharedCluster { DataContext = cluster };
        var window = new Window
        {
            Content = view,
            Width = 1450,
            Height = 700,
        };
        try
        {
            window.Show();
            using WriteableBitmap? initial = window.CaptureRenderedFrame();
            Avalonia.Controls.Shapes.Path active = view.FindControl<Avalonia.Controls.Shapes.Path>("ActiveLinks")!;
            Assert.NotNull(active.Data);
            double before = active.Data.Bounds.Bottom;

            PowerplayAcquireMiningNode terminus = Assert.Single(
                cluster.MiningSystems,
                node => node.System == "Terminus"
            );
            terminus.ToggleCommand.Execute(null);
            using WriteableBitmap? expanded = window.CaptureRenderedFrame();

            Assert.True(active.Data.Bounds.Bottom > before);
        }
        finally
        {
            window.Close();
        }
    }

    private static SurfaceSellRowViewModel SharedSellRow(
        string system,
        IReadOnlyList<SurfaceMiningSystemRowViewModel> mining
    ) =>
        new(
            system,
            "10 ly",
            [
                new AcquireStationViewModel(
                    "Port",
                    "Large",
                    "",
                    "",
                    [new AcquireQuoteViewModel("MON", "1 CR", "1 Demand")]
                ),
            ],
            mining,
            10,
            1
        );

    [AvaloniaFact]
    public async Task AcquireSplitHeadersStayAttachedAndResultsScrollInsideTheWorkspace()
    {
        using var http = new HttpClient(new SurfaceResultHandler());
        using MainWindowViewModel model = MainWindowViewModelTestBuilder.Create(null, _ => { });
        model.MineMap.UseSurfaceSearch(new MiningSearchClient(http));
        model.MineMap.SelectedTab = 4;
        model.MineMap.SurfaceSearch.Reference = "Timbalderis";
        model.MineMap.SurfaceSearch.Materials.Add("Diamond");
        await model.MineMap.SurfaceSearch.SearchAsync();
        Assert.True(model.MineMap.SurfaceSearch.HasRows);
        SurfaceSellRowViewModel sell = Assert.Single(model.MineMap.SurfaceSearch.Rows);
        Assert.Equal(5, sell.VisibleSystems.Count);
        Assert.Equal(29, sell.Systems.Count);
        Assert.True(sell.Systems[0].HasAdditionalBodies);

        var view = new MineMapView { DataContext = model };
        var window = new Window
        {
            Content = view,
            Width = 1600,
            Height = 1000,
        };
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            SurfaceMiningSplitResults split = view.FindControl<SurfaceMiningSplitResults>("SurfaceSearchSplitResults")!;
            Grid header = split.FindControl<Grid>("SplitHeader")!;
            ScrollViewer results = view.FindControl<ScrollViewer>("SurfaceSearchResultsScroller")!;
            StackPanel table = split.ResultsTableControl;
            AcquireSplitRow row = Assert.Single(view.GetVisualDescendants().OfType<AcquireSplitRow>());
            Grid rowGrid = Assert.IsType<Grid>(row.Content);
            Grid sellHeader = Assert.IsType<Grid>(header.Children[0]);
            Grid miningHeader = Assert.IsType<Grid>(header.Children[1]);
            Border sellCell = Assert.IsType<Border>(rowGrid.Children[0]);
            Border sellDistanceHeader = Assert.IsType<Border>(sellHeader.Children[1]);
            Border miningDistanceHeader = Assert.IsType<Border>(miningHeader.Children[1]);
            Grid sellRow = sellCell
                .GetVisualDescendants()
                .OfType<Grid>()
                .First(grid => grid.DataContext is SurfaceSellRowViewModel && grid.ColumnDefinitions.Count == 3);
            Grid miningRow = row.GetVisualDescendants()
                .OfType<Grid>()
                .First(grid =>
                    grid.DataContext is SurfaceMiningSystemRowViewModel && grid.ColumnDefinitions.Count == 3
                );

            Assert.InRange(Math.Abs(header.Bounds.Width - row.Bounds.Width), 0, 1);
            Assert.True(
                Math.Abs(sellHeader.Bounds.Width - sellCell.Bounds.Width) <= 1,
                $"Header {header.Bounds.Width}, row {row.Bounds.Width}, row grid {rowGrid.Bounds.Width}, sell header {sellHeader.Bounds.Width}, sell cell {sellCell.Bounds.Width}"
            );
            Assert.InRange(Math.Abs(miningHeader.Bounds.X - (header.Bounds.Width * 0.46)), 0, 1);
            Assert.Equal("DST REF", Assert.IsType<TextBlock>(sellDistanceHeader.Child).Text);
            Assert.Contains(
                miningDistanceHeader.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "DST SELL"
            );
            Assert.InRange(Math.Abs(sellDistanceHeader.Bounds.Width - sellRow.Children[1].Bounds.Width), 0, 1);
            Assert.InRange(Math.Abs(miningDistanceHeader.Bounds.Width - miningRow.Children[1].Bounds.Width), 0, 1);
            Assert.True(
                sellDistanceHeader.Bounds.Width
                    >= Assert.IsType<TextBlock>(sellDistanceHeader.Child).DesiredSize.Width + 12
            );
            Assert.InRange(Math.Abs(table.Bounds.Width - results.Viewport.Width), 0, 2);
            Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Mining 1");
            Border matchedBadge = row.GetVisualDescendants()
                .OfType<Border>()
                .First(border =>
                    border.Classes.Contains("commodity-code")
                    && border.Classes.Contains("matched")
                    && border.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "DIA")
                );
            Assert.Equal(
                Color.Parse("#E8FFFF"),
                Assert.IsAssignableFrom<ISolidColorBrush>(matchedBadge.Background).Color
            );
            Assert.True(sellCell.Bounds.Height < row.Bounds.Height);
            Assert.Contains(
                view.GetVisualDescendants().OfType<Button>(),
                button => button.Content is "Show all Systems" && button.IsEffectivelyVisible
            );
            double collapsedHeight = row.Bounds.Height;
            double sellHeight = sellCell.Bounds.Height;
            string? output = Environment.GetEnvironmentVariable("SRVSURVEY_SURFACE_SEARCH_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                using FileStream stream = File.Create(output);
                frame!.Save(stream, PngBitmapEncoderOptions.Default);
            }

            sell.Systems[0].ToggleCommand.Execute(null);
            using WriteableBitmap? bodiesExpandedFrame = window.CaptureRenderedFrame();
            string? expandedOutput = Environment.GetEnvironmentVariable(
                "SRVSURVEY_SURFACE_SEARCH_EXPANDED_RENDER_OUTPUT"
            );
            if (!string.IsNullOrWhiteSpace(expandedOutput))
            {
                using FileStream stream = File.Create(expandedOutput);
                bodiesExpandedFrame!.Save(stream, PngBitmapEncoderOptions.Default);
            }
            Assert.Equal(["DIA"], sell.Systems[0].VisibleBodies[1].Codes);
            Assert.StartsWith("2:", sell.Systems[0].VisibleBodies[1].Details);
            Assert.True(row.Bounds.Height > collapsedHeight);
            AcquireTreeLink firstLink = row.GetVisualDescendants().OfType<AcquireTreeLink>().First();
            Border horizontalLink = firstLink.FindControl<Border>("HorizontalLine")!;
            double linkY = horizontalLink.TranslatePoint(new Point(0, 0), row)!.Value.Y;
            double targetBottom = sellCell.TranslatePoint(new Point(0, sellCell.Bounds.Height), row)!.Value.Y;
            Assert.True(linkY <= targetBottom + 1, $"Link at {linkY}, sell box ends at {targetBottom}");
            Assert.True(double.IsFinite(firstLink.FirstAnchorY));
            Assert.InRange(Math.Abs(firstLink.FirstAnchorY - sellCell.Bounds.Height / 2), 0, 2);
            sell.ToggleAllCommand.Execute(null);
            using WriteableBitmap? expandedFrame = window.CaptureRenderedFrame();
            Assert.Equal(29, sell.VisibleSystems.Count);
            Assert.InRange(Math.Abs(sellCell.Bounds.Height - sellHeight), 0, 1);

            window.Width = 700;
            using WriteableBitmap? narrowFrame = window.CaptureRenderedFrame();
            Assert.True(results.Extent.Width > results.Viewport.Width);
            ScrollViewer page = results.GetVisualAncestors().OfType<ScrollViewer>().Last();
            Assert.True(page.Extent.Height > page.Viewport.Height);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EveryStationCommodityCodeUsesTheSameBadge()
    {
        var stationList = new MiningStationList
        {
            Stations = new[]
            {
                new AcquireStationViewModel(
                    "Gold Port",
                    "L",
                    "Distance: 150 ls",
                    "",
                    [
                        new AcquireQuoteViewModel("PER", "920,000 CR", "1,000 Demand", true, true),
                        new AcquireQuoteViewModel("THR", "770,000 CR", "2,000 Demand", false, true),
                    ]
                ),
            },
        };
        var window = new Window
        {
            Content = stationList,
            Width = 600,
            Height = 300,
        };
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();

            string[] badgeCodes = stationList
                .GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("commodity-code"))
                .Select(border => border.GetVisualDescendants().OfType<TextBlock>().Single().Text!)
                .ToArray();
            Assert.Equal(["PER", "THR"], badgeCodes);
            Border unavailable = stationList
                .GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Classes.Contains("unavailable"));
            Assert.Equal("PER", unavailable.GetVisualDescendants().OfType<TextBlock>().Single().Text);
            Border thorium = stationList
                .GetVisualDescendants()
                .OfType<Border>()
                .Single(border =>
                    border.Classes.Contains("material")
                    && border.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "THR")
                );
            Assert.Equal("#7FA86B", Assert.IsType<AcquireQuoteViewModel>(thorium.DataContext).ColorHex);
            Assert.Equal(Color.Parse("#7FA86B"), Assert.IsAssignableFrom<ISolidColorBrush>(thorium.Background).Color);
            Assert.NotEqual(
                Color.Parse("#8BC34A"),
                Assert.IsAssignableFrom<ISolidColorBrush>(unavailable.Background).Color
            );
            Assert.Contains(
                unavailable.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>(),
                path => path.IsVisible
            );
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class SurfaceResultHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            object payload = path switch
            {
                "/api/bodies/search" => new
                {
                    results = Enumerable
                        .Range(1, 30)
                        .Select(index => new
                        {
                            name = $"Mining {Math.Max(1, index - 1)} {(index == 2 ? 2 : 1)}",
                            system_name = $"Mining {Math.Max(1, index - 1)}",
                            subtype = "Rocky body",
                            gravity = 0.3,
                            distance = Math.Max(1, index - 1) * 1.2,
                            distance_to_arrival = 100,
                        }),
                },
                _ when path.EndsWith("/commodities", StringComparison.Ordinal) => new[]
                {
                    new { commodityName = "Diamond", avgSellPrice = 100_000 },
                },
                _ => new[]
                {
                    new
                    {
                        systemName = "Sell System",
                        stationName = "Gold Port",
                        stationType = "Coriolis",
                        maxLandingPadSize = 3,
                        sellPrice = 200_000,
                        demand = 1_000,
                        stock = 0,
                        updatedAt = DateTimeOffset.UtcNow,
                        distance = 12.4,
                        distanceToArrival = 150,
                        marketId = 9,
                        commodityName = "Diamond",
                    },
                },
            };
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload)),
                }
            );
        }
    }
}
