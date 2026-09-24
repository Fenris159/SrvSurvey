using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class AcquireSplitRowPresentationTests
{
    [AvaloniaFact]
    public void SharedAcquireMiningRowsStayAlignedWithSellCardsWhenAllSystemsAreVisible()
    {
        SurfaceSellRowViewModel[] rows =
        [
            Sell("Crucis Sector YE-A d146", ["Mine 1", "Mine 2", "Mine 3", "Mine 4"]),
            Sell("Sell B", ["Mine 4", "Mine 5", "Mine 6"]),
        ];
        PowerplayAcquireClusterViewModel cluster = Assert.Single(PowerplayAcquireClusterViewModel.Group(rows));
        var view = new PowerplayAcquireSharedCluster { DataContext = cluster };
        var window = new Window
        {
            Content = view,
            Width = 1600,
            Height = 900,
        };
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            string? renderOutput = Environment.GetEnvironmentVariable("SRVSURVEY_ACQUIRE_CLUSTER_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(renderOutput))
            {
                using FileStream stream = File.Create(renderOutput);
                frame!.Save(stream, PngBitmapEncoderOptions.Default);
            }

            Assert.Equal(6, cluster.VisibleMiningSystems.Count);
            Assert.DoesNotContain(
                view.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, "Show all Systems")
            );
            Border firstSell = view.GetVisualDescendants()
                .OfType<Border>()
                .First(border => border.Name == "SellLinkAnchor");
            TextBlock sellTitle = firstSell
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .First(text => text.Text?.StartsWith("Crucis Sector YE-A d146", StringComparison.Ordinal) == true);
            double titleRight = sellTitle.TranslatePoint(new Point(sellTitle.Bounds.Width, 0), firstSell)!.Value.X;
            Assert.True(titleRight <= firstSell.Bounds.Width, "The full sell-system title should fit inside its card.");
            Border firstMining = view.GetVisualDescendants()
                .OfType<Border>()
                .First(border => border.Name == "MiningLinkAnchor");
            Grid miningHeader = view.GetVisualDescendants()
                .OfType<Grid>()
                .First(grid =>
                    grid.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Mining System")
                    && grid.ColumnDefinitions.Count == 2
                );
            double sellTop = firstSell.TranslatePoint(new Point(0, 0), view)!.Value.Y;
            double miningTop = firstMining.TranslatePoint(new Point(0, 0), view)!.Value.Y;
            double miningHeaderTop = miningHeader.TranslatePoint(new Point(0, 0), view)!.Value.Y;
            Assert.InRange(miningHeaderTop - sellTop, -1, 2);
            Assert.InRange(miningTop - miningHeaderTop - miningHeader.Bounds.Height, -1, 2);

            Border secondSell = view.GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Name == "SellLinkAnchor")
                .ElementAt(1);
            Point click = secondSell.TranslatePoint(new Point(secondSell.Bounds.Width - 4, 16), window)!.Value;
            window.MouseDown(click, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(click, MouseButton.Left, RawInputModifiers.None);
            using WriteableBitmap? selected = window.CaptureRenderedFrame();
            string? selectedOutput = Environment.GetEnvironmentVariable(
                "SRVSURVEY_ACQUIRE_CLUSTER_SELECTED_RENDER_OUTPUT"
            );
            if (!string.IsNullOrWhiteSpace(selectedOutput))
            {
                using FileStream stream = File.Create(selectedOutput);
                selected!.Save(stream, PngBitmapEncoderOptions.Default);
            }
            Assert.Equal("Sell B", cluster.SelectedSellSystem);
            Assert.Contains("selected", secondSell.Classes);
            PowerplayAcquireMiningNode firstConnected = Assert.IsType<PowerplayAcquireMiningNode>(
                view.GetVisualDescendants()
                    .OfType<Border>()
                    .First(border => border.Name == "MiningLinkAnchor")
                    .DataContext
            );
            Assert.True(firstConnected.ConnectsTo("Sell B"));
            double selectedTop = secondSell.TranslatePoint(new Point(0, 0), view)!.Value.Y;
            double selectedMiningHeaderTop = miningHeader.TranslatePoint(new Point(0, 0), view)!.Value.Y;
            Assert.InRange(selectedMiningHeaderTop - selectedTop, -1, 2);

            Point titleClick = sellTitle.TranslatePoint(new Point(8, sellTitle.Bounds.Height / 2), window)!.Value;
            window.MouseDown(titleClick, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(titleClick, MouseButton.Left, RawInputModifiers.None);
            Assert.Equal("Crucis Sector YE-A d146", cluster.SelectedSellSystem);
        }
        finally
        {
            window.Close();
        }
    }

    private static SurfaceSellRowViewModel Sell(string target, string[] mines) =>
        new(
            target,
            "10 ly",
            [
                new AcquireStationViewModel(
                    "Station",
                    "L",
                    "12 ls",
                    "",
                    Enumerable
                        .Range(1, 7)
                        .Select(index => new AcquireQuoteViewModel("MON", $"{index} CR", "100 Demand"))
                        .ToArray()
                ),
            ],
            mines
                .Select(name => new SurfaceMiningSystemRowViewModel(
                    name,
                    2,
                    [new SurfaceBodyLine(["MON"], "Rocky body")]
                ))
                .ToArray(),
            10,
            1_000,
            new SurfaceSellRowOptions(
                [1_000],
                new SurfaceSellSystemDetails(
                    "Unoccupied",
                    "None",
                    ["Aisling Duval", "Felicia Winters", "Edmund Mahon", "Jerome Archer", "Yuri Grom", "Nakato Kaine"]
                )
            )
        );

    [AvaloniaFact]
    public void SurfaceResultLineRendersInsideSharedTreeRow()
    {
        var row = new AcquireSplitRow
        {
            DataContext = "Sell System",
            Lines = new[]
            {
                new SurfaceMiningSystemRowViewModel(
                    "Mining System",
                    4.2,
                    [new SurfaceBodyLine(["DIA"], "1: Rocky body")]
                ),
            },
            TargetTemplate = new FuncDataTemplate<string>((target, _) => new TextBlock { Text = target }),
            LineTemplate = new FuncDataTemplate<SurfaceMiningSystemRowViewModel>(
                (line, _) => new TextBlock { Text = line?.System }
            ),
        };
        var window = new Window
        {
            Content = row,
            Width = 900,
            Height = 300,
        };
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();

            Assert.NotNull(frame);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Mining System");
        }
        finally
        {
            window.Close();
        }
    }
}
