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
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed partial class ColonizationShoppingPresentationTests
{
    [AvaloniaFact]
    public void MarketDataSourceCheckboxesAppearAfterArrivalAndControlSearchAvailability()
    {
        using var model = new ColonizationShoppingViewModel(ColonizationLocalMarketTests.Projects(), () => "Sol");
        var window = new ColonizationShoppingTestWindow(model) { Width = 1180 };
        ApplySupplyTheme(window, RavenThemeCatalog.Get("monochrome-dark"));
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            StackPanel sources = window.FindControl<StackPanel>("MarketDataSources")!;
            TextBlock arrival = Assert.Single(
                window.GetVisualDescendants().OfType<TextBlock>(),
                block => block.Text == "Arrival ls (0 = any)"
            );
            Control arrivalField = Assert.IsType<Control>(arrival.Parent, exactMatch: false);
            Assert.Same(arrivalField.Parent, sources.Parent);
            Assert.Equal(arrivalField.Bounds.Y, sources.Bounds.Y);
            Assert.True(sources.Bounds.X >= arrivalField.Bounds.Right);
            CheckBox spansh = window.FindControl<CheckBox>("UseSpanshMarkets")!;
            CheckBox ardent = window.FindControl<CheckBox>("UseArdentMarkets")!;
            Assert.True(spansh.IsChecked);
            Assert.True(ardent.IsChecked);
            spansh.IsChecked = false;
            Assert.False(model.UseSpansh);
            Assert.True(model.CanSearch);
            ardent.IsChecked = false;
            Assert.False(model.UseArdent);
            Assert.False(model.CanSearch);
            spansh.IsChecked = true;
            Assert.True(model.CanSearch);
            frame.Save(
                Path.Combine(Path.GetTempPath(), "srvsurvey-market-data-sources.png"),
                PngBitmapEncoderOptions.Default
            );
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task OutdatedLocalMarketsHaveVisibleWarningsAndCanBeHiddenWithoutAnotherRequest()
    {
        var search = new ColonizationLocalMarketTests.LocalSearch(
            (_, _) =>
                Task.FromResult(
                    new ColonizationMarketSearchResult(
                        [
                            ColonizationLocalMarketTests.Quote(1, "steel", 100) with
                            {
                                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-103),
                            },
                        ],
                        []
                    )
                )
        );
        using var model = new ColonizationShoppingViewModel(
            ColonizationLocalMarketTests.Projects(),
            () => "Sol",
            search
        );
        await model.SearchAsync();
        model.ShowSearchCriteria = true;
        var window = new ColonizationShoppingTestWindow(model) { Width = 1180 };
        ApplySupplyTheme(window, RavenThemeCatalog.Get("monochrome-dark"));
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            TextBlock warning = Assert.Single(
                window.GetVisualDescendants().OfType<TextBlock>(),
                block => block.Text == "Outdated" && block.IsEffectivelyVisible
            );
            Assert.Contains("Verify stock", Assert.IsType<string>(ToolTip.GetTip(warning)));
            CheckBox include = window.FindControl<CheckBox>("IncludeOutdatedLocalMarkets")!;
            Assert.True(include.IsChecked);
            include.IsChecked = false;
            Assert.False(model.IncludeOutdatedLocalMarkets);
            Assert.Empty(model.Results);
            Assert.Single(search.Queries);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("blue-light")]
    [InlineData("blue-dark")]
    [InlineData("orange-dark")]
    [InlineData("green-light")]
    [InlineData("green-dark")]
    [InlineData("monochrome-dark")]
    public async Task LocalMarketButtonUsesTheExistingProgressBarAndSecondaryHighlight(string themeKey)
    {
        var pending = new TaskCompletionSource<ColonizationMarketSearchResult>();
        var search = new ColonizationLocalMarketTests.LocalSearch((_, _) => pending.Task);
        using var model = new ColonizationShoppingViewModel(
            ColonizationLocalMarketTests.Projects(),
            () => "Sol",
            search
        );
        var finished = new TaskCompletionSource();
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(model.IsBusy) && !model.IsBusy)
            {
                finished.TrySetResult();
            }
        };
        RavenThemeDefinition theme = RavenThemeCatalog.Get(themeKey);
        var window = new ColonizationShoppingTestWindow(model) { Width = 1180 };
        ApplySupplyTheme(window, theme);
        try
        {
            window.Show();
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            ToggleButton toggle = window.FindControl<ToggleButton>("ExcludeLocalCommodities")!;
            Button selectNone = window.FindControl<Button>("SelectNoShoppingNeeds")!;
            Assert.Equal("Exclude Local Commodities", toggle.Content);
            Assert.True(toggle.Bounds.Left > selectNone.Bounds.Right);
            Assert.Equal(selectNone.FontSize, toggle.FontSize);
            Assert.Equal(selectNone.Padding, toggle.Padding);
            LeftClick(window, toggle);
            Assert.True(model.IsBusy);
            Assert.False(toggle.IsEnabled);
            Assert.True(Assert.Single(window.GetVisualDescendants().OfType<ProgressBar>()).IsVisible);
            pending.SetResult(new([ColonizationLocalMarketTests.Quote(1, "steel", 100)], []));
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using WriteableBitmap? selectedFrame = window.CaptureRenderedFrame();
            Assert.NotNull(selectedFrame);
            Assert.True(toggle.IsChecked);
            Assert.Equal(Color.Parse(theme.SecondaryFillColor), SupplyBrushColor(toggle.Background));
            Assert.Equal(Color.Parse(theme.TextColor), SupplyBrushColor(toggle.Foreground));
            ContentPresenter presenter = toggle.GetVisualDescendants().OfType<ContentPresenter>().First();
            Assert.Equal(Color.Parse(theme.SecondaryFillColor), SupplyBrushColor(presenter.Background));
            ToggleButton steel = window
                .FindControl<ItemsControl>("ShoppingNeeds")!
                .GetVisualDescendants()
                .OfType<ToggleButton>()
                .Single(tag => tag.DataContext is ShoppingNeedViewModel need && need.Need.Commodity == "steel");
            Assert.False(steel.IsChecked);
            Assert.False(steel.IsEnabled);
            if (themeKey == "monochrome-dark")
            {
                selectedFrame.Save(
                    Path.Combine(Path.GetTempPath(), "srvsurvey-exclude-local-commodities.png"),
                    PngBitmapEncoderOptions.Default
                );
            }
            LeftClick(window, toggle);
            Assert.False(toggle.IsChecked);
            Assert.True(model.Needs.Single(need => need.Need.Commodity == "steel").IsSelected);
            Assert.True(steel.IsEnabled);
            Assert.Equal(2, search.Queries.Count);
        }
        finally
        {
            window.Close();
        }
    }
}
