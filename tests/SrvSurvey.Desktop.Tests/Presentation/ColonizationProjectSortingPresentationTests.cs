using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed partial class ColonizationShoppingPresentationTests
{
    [AvaloniaFact]
    public async Task ProjectHeaderClicksReorderRenderedRowsAndShowOnlyTheActiveArrow()
    {
        using var http = new HttpClient(
            new RavenHandler
            {
                ActiveProjects =
                [
                    new()
                    {
                        BuildId = "a",
                        BuildName = "Alpha",
                        BuildType = "zeta-type",
                        SystemName = "Sol",
                        MaximumRequired = 10_000,
                        RemainingRequired = 1_000,
                        Commodities = new() { ["steel"] = 1_000 },
                    },
                    new()
                    {
                        BuildId = "b",
                        BuildName = "Bravo",
                        BuildType = "alpha-type",
                        SystemName = "Achenar",
                        MaximumRequired = 10_000,
                        RemainingRequired = 9_000,
                        Commodities = new() { ["water"] = 9_000 },
                    },
                ],
                LinkedCarriers =
                [
                    new()
                    {
                        MarketId = 42,
                        Cargo = new() { ["steel"] = 100, ["water"] = 4_000 },
                    },
                ],
            }
        );
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithExternalNetworkClient(http)
        );
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Example Cmdr");
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
            Grid headers = view.FindControl<Grid>("ProjectTableHeaders")!;
            headers.BringIntoView();
            using (WriteableBitmap? initial = window.CaptureRenderedFrame())
            {
                Assert.NotNull(initial);
            }
            Button[] buttons = headers.Children.OfType<Button>().ToArray();
            Assert.Equal(5, buttons.Length);
            Assert.Equal("↓", HeaderArrow(view.FindControl<Button>("ProjectSortProgress")!).Text);
            foreach (Button button in buttons)
            {
                Point click = button.TranslatePoint(new Point(4, button.Bounds.Height / 2), window)!.Value;
                window.MouseDown(click, MouseButton.Left);
                window.MouseUp(click, MouseButton.Left);
                using (WriteableBitmap? ascending = window.CaptureRenderedFrame())
                {
                    Assert.NotNull(ascending);
                }
                string[] expected = button.Name is "ProjectSortProject" or "ProjectSortLoaded"
                    ? ["a", "b"]
                    : ["b", "a"];
                Assert.Equal(expected, RenderedProjects(view));
                Assert.Equal("↑", HeaderArrow(button).Text);
                Assert.All(
                    buttons.Where(other => other != button),
                    other => Assert.Equal(string.Empty, HeaderArrow(other).Text)
                );

                window.MouseDown(click, MouseButton.Left);
                window.MouseUp(click, MouseButton.Left);
                using WriteableBitmap? descending = window.CaptureRenderedFrame();
                Assert.NotNull(descending);
                Assert.Equal(expected.Reverse(), RenderedProjects(view));
                Assert.Equal("↓", HeaderArrow(button).Text);
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static TextBlock HeaderArrow(Button button) =>
        Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(button.Content).Children[1]);

    private static IEnumerable<string> RenderedProjects(ColonizationView view) =>
        view.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("colonization-project-card"))
            .Select(border => Assert.IsType<ColonizationProjectRowViewModel>(border.DataContext).Project.BuildId);
}
