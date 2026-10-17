using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed partial class ColonizationShoppingPresentationTests
{
    [AvaloniaTheory]
    [InlineData(640)]
    [InlineData(1180)]
    public async Task BulkSelectionButtonsFilterExistingMarketsAndFitTheWindow(int width)
    {
        var search = new ColonizationShoppingViewModelTests.SearchStub([
            ColonizationShoppingViewModelTests.Quote(1, "steel", 100) with
            {
                Station = "Galileo",
            },
            ColonizationShoppingViewModelTests.Quote(2, "water", 50) with
            {
                Station = "Daedalus",
            },
        ]);
        using var model = new ColonizationShoppingViewModel(
            ColonizationShoppingViewModelTests.Projects(),
            () => "Sol",
            search
        );
        await model.SearchAsync();
        var window = new ColonizationShoppingWindow(model) { Width = width };
        try
        {
            window.Show();
            using WriteableBitmap? initial = window.CaptureRenderedFrame();
            Assert.NotNull(initial);
            Button all = window.FindControl<Button>("SelectAllShoppingNeeds")!;
            Button none = window.FindControl<Button>("SelectNoShoppingNeeds")!;
            foreach (Button button in new[] { all, none })
            {
                double right = button.TranslatePoint(new Point(button.Bounds.Width, 0), window)!.Value.X;
                Assert.InRange(right, 1, window.ClientSize.Width - 8);
            }
            LeftClick(window, none);
            Assert.Empty(model.Results);
            Assert.All(model.Needs, need => Assert.False(need.IsSelected));
            LeftClick(window, all);
            Assert.Equal(2, model.Results.Count);
            Assert.All(model.Needs, need => Assert.True(need.IsSelected));
            Assert.Equal(1, search.Calls);
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(
                Path.Combine(Path.GetTempPath(), $"srvsurvey-shopping-selection-buttons-{width}.png"),
                PngBitmapEncoderOptions.Default
            );
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CarrierDifferenceSelectorShowsAllLinkedChoicesAndRecalculatesTags()
    {
        using var model = new ColonizationShoppingViewModel(
            ColonizationShoppingViewModelTests.Projects(),
            () => "Sol",
            new ColonizationShoppingViewModelTests.SearchStub([
                ColonizationShoppingViewModelTests.Quote(1, "steel", 60) with
                {
                    Station = "Galileo",
                },
                ColonizationShoppingViewModelTests.Quote(1, "water", 200) with
                {
                    Station = "Galileo",
                },
            ])
        );
        model.UpdateCarriers([
            new()
            {
                MarketId = 42,
                Name = "N4W-T0Z",
                DisplayName = "Dire Eclipse",
                Owner = "Teammate",
                Cargo = new() { ["steel"] = 25 },
            },
        ]);
        await model.SearchAsync();
        var window = new ColonizationShoppingWindow(model) { Width = 700 };
        try
        {
            window.Show();
            ComboBox selector = window.FindControl<ComboBox>("ShoppingCarrier")!;
            Assert.True(selector.IsEffectivelyVisible);
            Assert.True(model.UseCarrierDifference);
            ToggleSwitch toggle = window.FindControl<ToggleSwitch>("CarrierDifferenceToggle")!;
            LeftClick(window, toggle);
            Assert.False(model.UseCarrierDifference);
            Assert.False(selector.IsEffectivelyVisible);
            LeftClick(window, toggle);
            Assert.True(model.UseCarrierDifference);
            Assert.True(selector.IsEffectivelyVisible);
            Assert.Equal("ALL", Assert.IsType<ColonizationCarrierChoice>(selector.SelectedItem).Label);
            Assert.Equal(2, selector.ItemCount);
            selector.SelectedItem = model.CarrierOptions[1];
            Assert.Contains("Steel: 75", model.NeedsText);
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(
                Path.Combine(Path.GetTempPath(), "srvsurvey-shopping-fixes-preview.png"),
                PngBitmapEncoderOptions.Default
            );
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task MonochromeNeedsHaveReadableSelectedTextAndSpaceBetweenTagRows()
    {
        using var model = new ColonizationShoppingViewModel(
            [
                ColonizationShoppingViewModelTests.Projects()[0] with
                {
                    Commodities = ColonizationManifestCommodity.All.Take(22).ToDictionary(item => item.Key, _ => 100),
                },
            ],
            () => "Sol"
        );
        var window = new ColonizationShoppingWindow(model) { Width = 640 };
        window.Resources["RavenControlAccentBrush"] = new SolidColorBrush(Color.Parse("#F5F5F5"));
        window.Resources["RavenAccentBrush"] = new SolidColorBrush(Color.Parse("#E6D59A"));
        window.Resources["RavenAccentForegroundBrush"] = new SolidColorBrush(Color.Parse("#0A0A0A"));
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(
                Path.Combine(Path.GetTempPath(), "srvsurvey-shopping-monochrome-preview.png"),
                PngBitmapEncoderOptions.Default
            );
            ToggleButton[] tags = window
                .FindControl<ItemsControl>("ShoppingNeeds")!
                .GetVisualDescendants()
                .OfType<ToggleButton>()
                .ToArray();
            ContentPresenter presenter = tags[0]
                .GetVisualDescendants()
                .OfType<ContentPresenter>()
                .First(item => Equals(item.Content, tags[0].Content));
            Assert.Equal(Color.Parse("#0A0A0A"), Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Foreground).Color);
            IGrouping<double, Rect>[] rows = tags.Select(tag => new Rect(
                    tag.TranslatePoint(default, window)!.Value,
                    tag.Bounds.Size
                ))
                .GroupBy(bounds => Math.Round(bounds.Y, 1))
                .OrderBy(group => group.Key)
                .ToArray();
            Assert.True(rows.Length > 1);
            for (int index = 1; index < rows.Length; index++)
            {
                Assert.True(
                    rows[index - 1].Max(bounds => bounds.Bottom) < rows[index].Key,
                    "Tag rows must have a visible gap."
                );
            }
            Assert.DoesNotContain(
                window.FindControl<ItemsControl>("ShoppingNeeds")!.GetVisualDescendants().OfType<TextBlock>(),
                block => block.Text == ", "
            );
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SearchUsesOneFullWidthLoadingBarWithCriteriaOpenOrClosed()
    {
        var response = new TaskCompletionSource<ColonizationMarketSearchResult>();
        var search = new ColonizationShoppingViewModelTests.SearchStub([]) { Run = (_, _) => response.Task };
        using var model = new ColonizationShoppingViewModel(
            ColonizationShoppingViewModelTests.Projects(),
            () => "Sol",
            search
        );
        var window = new ColonizationShoppingWindow(model);
        Task? pending = null;
        try
        {
            window.Show();
            pending = model.SearchAsync();
            window.UpdateLayout();
            ProgressBar loading = Assert.Single(
                window.GetVisualDescendants().OfType<ProgressBar>(),
                bar => bar.IsEffectivelyVisible
            );
            Assert.True(loading.Bounds.Width >= window.ClientSize.Width - 30);
            model.ShowSearchCriteria = false;
            window.UpdateLayout();
            Assert.True(loading.IsEffectivelyVisible);
        }
        finally
        {
            response.TrySetResult(new([], []));
            if (pending is not null)
            {
                await pending;
            }
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task RowBodyExpandsWithoutTreatingSystemLinksAsExpansionClicks()
    {
        using var model = new ColonizationShoppingViewModel(
            ColonizationShoppingViewModelTests.Projects(),
            () => "Sol",
            new ColonizationShoppingViewModelTests.SearchStub([
                ColonizationShoppingViewModelTests.Quote(1, "steel", 100),
            ])
        );
        await model.SearchAsync();
        var window = new ColonizationShoppingWindow(model);
        try
        {
            window.Show();
            window.UpdateLayout();
            ShoppingMarketRowViewModel row = model.Results[0];
            Grid summary = window
                .GetVisualDescendants()
                .OfType<Grid>()
                .First(grid => grid.Classes.Contains("market-summary"));
            TextBlock pad = summary.Children.OfType<TextBlock>().Single(text => Grid.GetColumn(text) == 1);
            LeftClick(window, pad);
            Assert.False(row.IsExpanded);
            LeftClick(window, pad);
            Assert.True(row.IsExpanded);
            Button system = summary.Children.OfType<Button>().Single(button => Grid.GetColumn(button) == 3);
            LeftClick(window, system);
            Assert.True(row.IsExpanded);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ArrivalColumnRemainsReachableAfterShrinkingAndExpanding()
    {
        ColonizationMarketQuote[] quotes = Enumerable
            .Range(1, 30)
            .Select(id =>
                ColonizationShoppingViewModelTests.Quote(id, "steel", 100) with
                {
                    Station = "Kawczynski Geograf Port Long Station",
                    System = "Col 285 Sector BA-P c6-18",
                    Arrival = 1_234_567_890,
                }
            )
            .ToArray();
        using var model = new ColonizationShoppingViewModel(
            ColonizationShoppingViewModelTests.Projects(),
            () => "Sol",
            new ColonizationShoppingViewModelTests.SearchStub(quotes)
        );
        await model.SearchAsync();
        var window = new ColonizationShoppingWindow(model);
        try
        {
            window.Show();
            foreach (int width in new[] { 1100, 640, 900, 640, 1100 })
            {
                window.Width = width;
                Dispatcher.UIThread.RunJobs();
                using WriteableBitmap? frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                ListBox list = window.FindControl<ListBox>("ShoppingResults")!;
                Grid row = list.GetVisualDescendants()
                    .OfType<Grid>()
                    .First(grid => grid.Classes.Contains("market-summary"));
                TextBlock arrival = row.Children.OfType<TextBlock>().Single(text => Grid.GetColumn(text) == 5);
                double right = arrival.TranslatePoint(new Point(arrival.Bounds.Width, 0), list)!.Value.X;
                Assert.True(
                    right <= list.Bounds.Width - 8,
                    $"The Arrival column is clipped at window width {width}: {right} > {list.Bounds.Width - 8}."
                );
            }
        }
        finally
        {
            window.Close();
        }
    }
}
