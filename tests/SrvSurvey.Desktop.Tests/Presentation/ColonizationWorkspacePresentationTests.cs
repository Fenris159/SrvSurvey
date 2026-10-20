using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed partial class ColonizationShoppingPresentationTests
{
    [AvaloniaTheory]
    [InlineData(820)]
    [InlineData(1180)]
    public async Task EmbeddedMarketUsesProjectTagsReadableWishListTagsAndAResultsSummaryRow(int width)
    {
        using var http = new HttpClient(new RavenHandler());
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithExternalNetworkClient(http)
        );
        main.Colonization.MarketSearchFactory = () =>
            new ColonizationShoppingViewModelTests.SearchStub([
                ColonizationShoppingViewModelTests.Quote(1, "steel", 100),
                ColonizationShoppingViewModelTests.Quote(1, "water", 100),
                ColonizationShoppingViewModelTests.Quote(2, "steel", 200),
                ColonizationShoppingViewModelTests.Quote(2, "water", 200),
            ]);
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Example Cmdr");
        main.Colonization.UpdateSystemContext("Sol", null, 1);
        var view = new ColonizationView { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 1000,
        };
        window.Resources["RavenControlAccentBrush"] = new SolidColorBrush(Color.Parse("#F5F5F5"));
        window.Resources["RavenTagFillBrush"] = new SolidColorBrush(Color.Parse("#B8AA7B"));
        window.Resources["RavenAccentForegroundBrush"] = new SolidColorBrush(Color.Parse("#0A0A0A"));
        window.Resources["RavenSecondaryFillBrush"] = new SolidColorBrush(Color.Parse("#262626"));
        window.Resources["RavenInteractiveHoverBrush"] = new SolidColorBrush(Color.Parse("#262626"));
        window.Resources["RavenTextBrush"] = new SolidColorBrush(Color.Parse("#E5E5E5"));
        using var coordinator = new ColonizationProjectPreviewWindowCoordinator(main.Colonization, window);
        try
        {
            window.Show();
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            Border[] cards = view.FindControl<StackPanel>("ProjectsPage")!.Children.OfType<Border>().ToArray();
            Assert.Equal(3, cards.Length);
            Assert.Contains(
                cards[0].GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "Create a project"
            );
            Assert.Contains(
                cards[1].GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "My build projects"
            );
            Assert.Contains(cards[2].GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "LIVE DEPOT");
            view.FindControl<TabControl>("ColonizationTabs")!.SelectedIndex = 1;
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            SetProjectTagSelection(view, 1, false);
            ColonizationShoppingViewModel search = main.Colonization.MarketSearchWorkspace!;
            await search.SearchAsync();
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            ColonizationShoppingView market = Assert.Single(
                view.GetVisualDescendants().OfType<ColonizationShoppingView>()
            );
            Button copy = market.FindControl<Button>("CopyShoppingNeeds")!;
            Button criteria = market.FindControl<Button>("ChangeShoppingCriteria")!;
            Button wishlist = market.FindControl<Button>("OpenShoppingWishList")!;
            TextBlock total = market.FindControl<TextBlock>("RemainingShoppingNeeds")!;
            Assert.Equal("110 tonnes remaining across 2 commodities", total.Text);
            Assert.Same(total.Parent, wishlist.Parent);
            Assert.Equal(criteria.Padding, copy.Padding);
            Assert.Equal(criteria.Padding, wishlist.Padding);
            Assert.DoesNotContain("link", copy.Classes);
            Grid headers = market.FindControl<Grid>("ShoppingResultsHeader")!;
            Assert.True(
                total.TranslatePoint(default, market)!.Value.Y < headers.TranslatePoint(default, market)!.Value.Y
            );
            ShoppingSupplyViewModel steel = search
                .Results[0]
                .AvailableSupplies.Single(supply => supply.Need.Commodity == "steel");
            LeftClick(
                window,
                market
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .Single(button => button.Command == steel.ToggleWishListCommand)
            );
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            ShoppingSupplyViewModel chosen = search
                .Results[0]
                .AvailableSupplies.Single(supply => supply.Need.Commodity == "steel");
            Assert.True(chosen.IsWishListed);
            Button chosenTag = market
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Command == chosen.ToggleWishListCommand);
            Assert.Contains("wished", chosenTag.Classes);
            Assert.Equal(
                Color.Parse("#B8AA7B"),
                Assert.IsType<ISolidColorBrush>(chosenTag.Background, exactMatch: false).Color
            );
            TextBlock chosenLabel = Assert.Single(chosenTag.GetVisualDescendants().OfType<TextBlock>());
            Assert.Equal(
                Color.Parse("#0A0A0A"),
                Assert.IsType<ISolidColorBrush>(chosenLabel.Foreground, exactMatch: false).Color
            );
            LeftClick(window, wishlist);
            Assert.NotNull(coordinator.WishListWindow);
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
                frame.Save(
                    Path.Combine(Path.GetTempPath(), $"srvsurvey-market-tab-{width}.png"),
                    PngBitmapEncoderOptions.Default
                );
            }
            LeftClick(window, chosenTag);
            Assert.Empty(search.CaptureState().WishList);
            Assert.Equal("Steel: 60, Water: 50", search.NeedsText);
        }
        finally
        {
            window.Close();
        }
    }

    private static void SetProjectTagSelection(ColonizationView view, int index, bool selected)
    {
        view.FindControl<ItemsControl>("MarketSearchProjects")!
            .GetVisualDescendants()
            .OfType<Avalonia.Controls.Primitives.ToggleButton>()
            .ElementAt(index)
            .IsChecked = selected;
    }
}
