using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed partial class ColonizationShoppingPresentationTests
{
    [AvaloniaTheory]
    [InlineData(680)]
    [InlineData(1180)]
    public async Task KiloReviewManyCommodityTagsWrapWithinTheViewport(int width)
    {
        ColonizationBuildCost build = ColonizationBuildCatalog.LoadEmbedded().FindByBuildType("asteroid")!;
        ColonizationProject project = ColonizationShoppingViewModelTests.Projects()[0] with
        {
            Commodities = build.CommodityCosts.ToDictionary(pair => pair.Key, pair => pair.Value),
        };
        using var model = new ColonizationShoppingViewModel(
            [project],
            () => "Sol",
            new ColonizationShoppingViewModelTests.SearchStub(
                project
                    .Commodities.Select(pair => ColonizationShoppingViewModelTests.Quote(1, pair.Key, pair.Value))
                    .ToArray()
            )
        );
        await model.SearchAsync();
        model.Results[0].IsExpanded = true;
        var window = new ColonizationShoppingTestWindow(model) { Width = width, Height = 900 };
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            ItemsControl supplies = window
                .GetVisualDescendants()
                .OfType<ItemsControl>()
                .Single(control => control.Classes.Contains("market-supplies"));
            Button[] tags = supplies
                .GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.Classes.Contains("supply-button"))
                .ToArray();
            Assert.True(tags.Length > 10);
            Assert.True(
                tags.Select(tag => Math.Round(tag.TranslatePoint(default, supplies)!.Value.Y)).Distinct().Count() > 1
            );
            Assert.True(supplies.Bounds.Width < width);
            Assert.All(
                tags,
                tag => Assert.True(tag.TranslatePoint(new Point(tag.Bounds.Width, 0), window)!.Value.X <= width)
            );
            frame.Save(
                Path.Combine(Path.GetTempPath(), $"srvsurvey-kilo-tags-{width}.png"),
                Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default
            );
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task KiloReviewManyUnconfirmedDeliveriesKeepRecoveryAndTabsReachable()
    {
        string directory = Path.Combine(Path.GetTempPath(), "srvsurvey-kilo-recovery-" + Guid.NewGuid().ToString("N"));
        var paths = new AppDataPaths(
            Path.Combine(directory, "config"),
            Path.Combine(directory, "data"),
            Path.Combine(directory, "cache"),
            []
        );
        new ColonizationSettingsStore(paths.UiSettingsPath).SavePendingContributions(
            Enumerable
                .Range(0, 40)
                .Select(index => new ColonizationPendingContribution(
                    "Test Cmdr|F123|True",
                    "build-" + index,
                    "Test Cmdr",
                    new() { ["steel"] = 10 },
                    "delivery-" + index,
                    true
                ))
                .ToArray()
        );
        using var http = new HttpClient(new RavenHandler());
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithAppDataPaths(paths).WithExternalNetworkClient(http)
        );
        main.Colonization.IsEnabled = true;
        main.Colonization.SetCommanderProfile("F123", true, "test-key");
        await main.Colonization.SetCommanderAsync("Test Cmdr");
        Assert.Equal(40, main.Colonization.UnconfirmedContributions.Count);
        var view = new ColonizationView { DataContext = main };
        var window = new Window
        {
            Width = 900,
            Height = 700,
            Content = view,
        };
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            TabControl tabs = view.FindControl<TabControl>("ColonizationTabs")!;
            Assert.True(
                tabs.Bounds.Height >= 150,
                $"Recovery left only {tabs.Bounds.Height} pixels for workspace tabs."
            );
            Button retry = view.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.Content, "Retry selected deliveries"));
            ScrollViewer recovery = Assert.IsType<ScrollViewer>(
                retry.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault(),
                exactMatch: false
            );
            Assert.True(recovery.Extent.Height > recovery.Viewport.Height);
            recovery.ScrollToEnd();
            using WriteableBitmap? scrolled = window.CaptureRenderedFrame();
            Assert.NotNull(scrolled);
            Assert.InRange(retry.TranslatePoint(default, window)!.Value.Y, 0, window.Height - retry.Bounds.Height);
            scrolled.Save(
                Path.Combine(Path.GetTempPath(), "srvsurvey-kilo-recovery.png"),
                Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default
            );
        }
        finally
        {
            window.Close();
            await main.DisposeAsync();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
