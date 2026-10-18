using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed partial class ColonizationShoppingPresentationTests
{
    [AvaloniaFact]
    public async Task EmbeddedMarketHasTwentyRowsFullWidthTagsAndNoSelectionHighlight()
    {
        using var http = new HttpClient(new RavenHandler());
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithExternalNetworkClient(http)
        );
        ColonizationMarketQuote[] quotes = Enumerable
            .Range(1, 30)
            .Select(id => ColonizationShoppingViewModelTests.Quote(id, "steel", 100))
            .ToArray();
        main.Colonization.MarketSearchFactory = () => new ColonizationShoppingViewModelTests.SearchStub(quotes);
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Example Cmdr");
        main.Colonization.UpdateSystemContext("Sol", null, 1);
        main.Colonization.Projects[0].IsShoppingSelected = true;
        main.Colonization.SelectedWorkspaceTabIndex = 1;
        ColonizationShoppingViewModel model = main.Colonization.MarketSearchWorkspace!;
        await model.SearchAsync();
        Assert.Equal(30, model.Results.Count);
        foreach (ShoppingMarketRowViewModel row in model.Results)
        {
            row.IsExpanded = false;
        }
        var view = new ColonizationView { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = 1180,
            Height = 900,
        };
        try
        {
            window.Show();
            using WriteableBitmap? initial = window.CaptureRenderedFrame();
            Assert.NotNull(initial);
            ColonizationShoppingView market = Assert.Single(
                view.GetVisualDescendants().OfType<ColonizationShoppingView>()
            );
            ListBox results = market.FindControl<ListBox>("ShoppingResults")!;
            Assert.InRange(results.Bounds.Height, 560, 720);
            Assert.True(results.Bounds.Width > market.FindControl<Grid>("ShoppingResultsHeader")!.Bounds.Width + 100);
            Assert.Equal(1180, window.Width);
            ListBoxItem collapsed = results.GetVisualDescendants().OfType<ListBoxItem>().First();
            Assert.True(
                20 * collapsed.Bounds.Height + 1 <= results.Bounds.Height,
                $"Twenty rows need {20 * collapsed.Bounds.Height + 1} pixels, past the {results.Bounds.Height} viewport."
            );
            TextBlock instruction = market.FindControl<TextBlock>("ShoppingListInstructions")!;
            Assert.Contains("click again", instruction.Text!, StringComparison.OrdinalIgnoreCase);
            results.SelectedIndex = 0;
            model.Results[0].IsExpanded = true;
            using WriteableBitmap? selected = window.CaptureRenderedFrame();
            Assert.NotNull(selected);
            ListBoxItem item = results.GetVisualDescendants().OfType<ListBoxItem>().First();
            ContentPresenter presenter = item.GetVisualDescendants().OfType<ContentPresenter>().First();
            Assert.True(presenter.Background is null || presenter.Background is ISolidColorBrush { Color.A: 0 });
            ScrollBar bar = results
                .GetVisualDescendants()
                .OfType<ScrollBar>()
                .Single(scroll => scroll.Orientation == Avalonia.Layout.Orientation.Vertical);
            double barLeft = bar.TranslatePoint(default, results)!.Value.X;
            Button supply = results
                .GetVisualDescendants()
                .OfType<Button>()
                .First(button => button.Classes.Contains("supply-button"));
            Assert.True(supply.TranslatePoint(new Point(supply.Bounds.Width, 0), results)!.Value.X < barLeft);
            selected.Save(
                Path.Combine(Path.GetTempPath(), "srvsurvey-refined-market.png"),
                PngBitmapEncoderOptions.Default
            );
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SystemEditorFieldsStaySingleLineAndKeepBodyNumberReadable()
    {
        using var http = new HttpClient(
            new RavenHandler
            {
                SystemRecord = new ColonizationSystemRecord
                {
                    Name = "Sol",
                    SystemAddress = 42,
                    Architect = "Example Cmdr",
                    Bodies = [],
                    Sites =
                    [
                        new()
                        {
                            Id = "one",
                            Name = "Emem Munitions Hub",
                            BodyNumber = 36,
                            BuildType = "military_settlement_medium",
                            MarketId = 4315444579,
                            Status = ColonizationSystemSiteStatus.Complete,
                        },
                    ],
                },
            }
        );
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithExternalNetworkClient(http)
        );
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Example Cmdr");
        main.Colonization.UpdateSystemContext("Sol", null, 42);
        await main.Colonization.SystemEditor.LoadAsync();
        Assert.True(main.Colonization.SystemEditor.IsLoaded);
        main.Colonization.SelectedWorkspaceTabIndex = 2;
        var view = new ColonizationView { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = 1180,
            Height = 1000,
        };
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            ListBox rows = view.GetVisualDescendants()
                .OfType<ListBox>()
                .Single(list => ReferenceEquals(list.ItemsSource, main.Colonization.SystemEditor.Sites));
            ListBoxItem row = rows.GetVisualDescendants().OfType<ListBoxItem>().First();
            Assert.InRange(row.Bounds.Height, 36, 52);
            NumericUpDown body = Assert.Single(row.GetVisualDescendants().OfType<NumericUpDown>());
            Assert.InRange(body.Bounds.Width, 100, 110);
            Assert.InRange(body.Bounds.Height, 30, 40);
            Assert.Equal(36, body.Value);
            TextBox number = body.GetVisualDescendants().OfType<TextBox>().First();
            Assert.True(number.Bounds.Width >= 35);
            frame.Save(
                Path.Combine(Path.GetTempPath(), "srvsurvey-refined-system-editor.png"),
                PngBitmapEncoderOptions.Default
            );
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task PlannedProjectPreviewShowsCatalogNeedsWithoutALiveBuildRequest()
    {
        using var http = new HttpClient(
            new RavenHandler
            {
                ActiveProjects = [],
                SystemRecord = new ColonizationSystemRecord
                {
                    Name = "Sol",
                    SystemAddress = 42,
                    Architect = "Example Cmdr",
                    Sites =
                    [
                        new()
                        {
                            Id = "p",
                            Name = "Test Project",
                            BuildType = "asteroid",
                            Status = ColonizationSystemSiteStatus.Plan,
                        },
                    ],
                },
            }
        );
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithExternalNetworkClient(http)
        );
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Example Cmdr");
        main.Colonization.UpdateSystemContext("Sol", null, 42);
        ColonizationProjectRowViewModel plan = Assert.Single(main.Colonization.Projects);
        using ColonizationProjectPreviewViewModel preview = main.Colonization.CreateProjectPreview(
            plan.Project.BuildId
        );
        await preview.RefreshAsync();
        Assert.Equal("Test Project (Plan)", preview.Title);
        Assert.NotEmpty(preview.Rows);
        using ColonizationProjectPreviewViewModel combined = main.Colonization.CreateCombinedReport();
        await combined.RefreshAsync();
        Assert.NotEmpty(combined.Rows);
    }
}
