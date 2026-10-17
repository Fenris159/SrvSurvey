using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed partial class ColonizationShoppingPresentationTests
{
    [AvaloniaTheory]
    [InlineData(1180)]
    [InlineData(820)]
    [InlineData(680)]
    [InlineData(640)]
    public async Task RendersInteractiveNeedsSupplyColorsAndSearchControls(int width)
    {
        var search = new ColonizationShoppingViewModelTests.SearchStub([
            ColonizationShoppingViewModelTests.Quote(1, "steel", 100),
            ColonizationShoppingViewModelTests.Quote(1, "water", 20),
            ColonizationShoppingViewModelTests.Quote(2, "water", 80),
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
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(
                Path.Combine(Path.GetTempPath(), $"srvsurvey-shopping-preview-{width}.png"),
                PngBitmapEncoderOptions.Default
            );
            Assert.Equal("Sol", window.FindControl<TextBox>("ShoppingReferenceSystem")!.Text);
            Assert.Equal("100", window.FindControl<TextBox>("ShoppingRadius")!.Text);
            Assert.Equal(2, window.FindControl<ItemsControl>("ShoppingResults")!.ItemCount);
            Assert.False(model.ShowSearchCriteria);
            Assert.Same(
                window.FindControl<Button>("RefreshMarketSearch")!.Parent,
                window.FindControl<ToggleButton>("ShowShortSupply")!.Parent
            );
            Grid header = window.FindControl<Grid>("ShoppingResultsHeader")!;
            Assert.InRange(header.Bounds.Width, 1, 790);
            Grid row = window
                .GetVisualDescendants()
                .OfType<Grid>()
                .First(grid => grid.Classes.Contains("market-summary"));
            Assert.InRange(row.Bounds.Height, 1, 28);
            Assert.True(row.Bounds.Width <= header.Bounds.Width + 1);
            Button station = window
                .GetVisualDescendants()
                .OfType<Button>()
                .First(button => button.Command == model.Results[0].ToggleExpandedCommand);
            station.Command!.Execute(null);
            Assert.False(model.Results[0].IsExpanded);
            station.Command.Execute(null);
            Button expand = window.FindControl<Button>("ExpandAllMarkets")!;
            expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.All(model.Results, result => Assert.False(result.IsExpanded));
            expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.All(model.Results, result => Assert.True(result.IsExpanded));
            Button distance = window
                .GetVisualDescendants()
                .OfType<Button>()
                .First(button => Equals(button.Tag, "Distance"));
            distance.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(20, model.Results[0].Distance);
            distance.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            using WriteableBitmap? resultFrame = window.CaptureRenderedFrame();
            Assert.NotNull(resultFrame);
            resultFrame.Save(
                Path.Combine(Path.GetTempPath(), $"srvsurvey-shopping-results-{width}.png"),
                PngBitmapEncoderOptions.Default
            );

            TextBlock[] supplies = window
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(text => text.Classes.Contains("supply"))
                .ToArray();
            Assert.Contains(
                supplies,
                supply =>
                    supply.Classes.Contains("enough") && supply.Text!.StartsWith("Steel", StringComparison.Ordinal)
            );
            Assert.Contains(supplies, supply => !supply.Classes.Contains("enough") && supply.Text == "Water: 20");
            ToggleButton shortSupply = window.FindControl<ToggleButton>("ShowShortSupply")!;
            Assert.True(shortSupply.IsChecked);
            LeftClick(window, shortSupply);
            Assert.False(model.ShowShortSupply);
            Assert.All(
                model.Results.SelectMany(result => result.AvailableSupplies),
                supply => Assert.True(supply.IsEnough)
            );
            ComboBox sort = window.FindControl<ComboBox>("ShoppingSort")!;
            sort.SelectedItem = "Most commodities available";
            Assert.Equal("Most commodities available", model.Sort);
            sort.SelectedItem = "Distance";
            using (WriteableBitmap? filteredFrame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(filteredFrame);
            }
            Assert.All(
                window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Classes.Contains("supply")),
                text => Assert.Contains("enough", text.Classes)
            );
            LeftClick(window, shortSupply);
            Assert.True(model.ShowShortSupply);
            ToggleButton[] toggles = window
                .GetVisualDescendants()
                .OfType<ToggleButton>()
                .Where(toggle => toggle.DataContext is ShoppingNeedViewModel)
                .ToArray();
            Assert.Equal(2, toggles.Length);
            toggles[1].IsChecked = false;
            Assert.Single(model.Results);
            Assert.False(model.Needs[1].IsSelected);
            Assert.Equal(1, search.Calls);
            Button refresh = window.FindControl<Button>("RefreshMarketSearch")!;
            Assert.True(refresh.IsEffectivelyVisible);
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, search.Calls);
            model.ShowSearchCriteria = true;
            TextBox radius = window.FindControl<TextBox>("ShoppingRadius")!;
            radius.Text = "25";
            Assert.Single(model.Results);
            Button run = window.FindControl<Button>("RunMarketSearch")!;
            Assert.Equal("Refresh", run.Content);
            run.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(25, search.LastQuery!.Radius);
            model.ShowSearchCriteria = true;
            using WriteableBitmap? reopened = window.CaptureRenderedFrame();
            Assert.NotNull(reopened);
            Button current = Assert.Single(
                window.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, "Use current location")
            );
            window.FindControl<TextBox>("ShoppingReferenceSystem")!.Text = "Achenar";
            current.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Sol", model.ReferenceSystem);
        }
        finally
        {
            window.Close();
        }
        Assert.False(model.CanSearch);
    }

    [AvaloniaFact]
    public async Task RendersCompactTableWithLongStationAndSystemNames()
    {
        string[] stations =
        [
            "Galileo",
            "Daedalus",
            "Gorbachev",
            "Abraham Lincoln",
            "Burnell Station",
            "Feynman Terminal",
            "Boulle Gateway",
            "Richardson's Base",
            "Hutton Orbital",
            "Millerport",
        ];
        string[] systems =
        [
            "Sol",
            "Sol",
            "Sol",
            "Sol",
            "Sol",
            "Wolf 359",
            "Wolf 359",
            "Ross 128",
            "Alpha Centauri",
            "Col 285 Sector BA-P c6-18",
        ];
        ColonizationMarketQuote[] quotes = stations
            .SelectMany(
                (station, index) =>
                    new[]
                    {
                        ColonizationShoppingViewModelTests.Quote(index + 1, "steel", index % 3 == 0 ? 80 : 300) with
                        {
                            Station = station,
                            System = systems[index],
                            Distance = index * 3.7,
                            Arrival = 120 + index * 570,
                        },
                        ColonizationShoppingViewModelTests.Quote(index + 1, "water", index % 2 == 0 ? 20 : 500) with
                        {
                            Station = station,
                            System = systems[index],
                            Distance = index * 3.7,
                            Arrival = 120 + index * 570,
                        },
                    }
            )
            .ToArray();
        var search = new ColonizationShoppingViewModelTests.SearchStub(quotes)
        {
            Run = (_, _) => Task.FromResult(new ColonizationMarketSearchResult(quotes, [])),
        };
        using var model = new ColonizationShoppingViewModel(
            ColonizationShoppingViewModelTests.Projects(),
            () => "Sol",
            search
        );
        await model.SearchAsync();
        model.Results[2].IsExpanded = true;
        var window = new ColonizationShoppingWindow(model) { Height = 560 };
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(
                Path.Combine(Path.GetTempPath(), "srvsurvey-shopping-compact-table.png"),
                PngBitmapEncoderOptions.Default
            );
            Grid header = window.FindControl<Grid>("ShoppingResultsHeader")!;
            Assert.InRange(header.Bounds.Width, 1, 790);
            Assert.InRange(window.FindControl<ListBox>("ShoppingResults")!.Bounds.Width, 1, 790);
            Assert.Contains(
                window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "Col 285 Sector BA-P c6-18"
            );
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task BoundsLargeResultListsAndLongProviderWarnings()
    {
        ColonizationMarketQuote[] quotes = Enumerable
            .Range(1, 2000)
            .Select(id => ColonizationShoppingViewModelTests.Quote(id, "steel", 100))
            .ToArray();
        var search = new ColonizationShoppingViewModelTests.SearchStub(quotes)
        {
            Run = (_, _) =>
                Task.FromResult(
                    new ColonizationMarketSearchResult(
                        quotes,
                        Enumerable.Repeat("Provider result limit reached. Narrow the radius.", 60).ToArray()
                    )
                ),
        };
        using var model = new ColonizationShoppingViewModel(
            ColonizationShoppingViewModelTests.Projects(),
            () => "Sol",
            search
        );
        await model.SearchAsync();
        var window = new ColonizationShoppingWindow(model);
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            ListBox results = window.FindControl<ListBox>("ShoppingResults")!;
            results.BringIntoView();
            using WriteableBitmap? scrolled = window.CaptureRenderedFrame();
            Assert.NotNull(scrolled);
            scrolled.Save(
                Path.Combine(Path.GetTempPath(), "srvsurvey-shopping-large-list.png"),
                PngBitmapEncoderOptions.Default
            );
            Assert.Equal(2000, results.ItemCount);
            Assert.InRange(results.Bounds.Height, 1, 600);
            Assert.InRange(window.FindControl<ScrollViewer>("ShoppingWarnings")!.Bounds.Height, 1, 72);
            int realized = results.GetVisualDescendants().OfType<ListBoxItem>().Count();
            Assert.InRange(realized, 1, 100);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task OpensSelectedSnapshotAndClosesOnConsentCommanderAndOwnerChanges()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SrvSurvey-shopping-" + Guid.NewGuid().ToString("N"));
        using var http = new HttpClient(new RavenHandler());
        using var model = new ColonizationViewModel(
            new ColonizationSettingsStore(Path.Combine(directory, "ui.json")),
            new RavenColonialClient(http)
        );
        var owner = new Window();
        using var coordinator = new ColonizationProjectPreviewWindowCoordinator(model, owner);
        try
        {
            owner.Show();
            coordinator.ShowShopping([]);
            Assert.Null(coordinator.ShoppingWindow);
            model.IsEnabled = true;
            await model.SetCommanderAsync("Example Cmdr");
            model.Projects[0].IsShoppingSelected = true;
            model.OpenShopping();
            ColonizationShoppingWindow first = coordinator.ShoppingWindow!;
            Assert.True(first.IsVisible);
            Assert.Equal("First", ((ColonizationShoppingViewModel)first.DataContext!).ProjectSummary);
            model.OpenShopping();
            Assert.True(first.IsVisible);
            Assert.Same(first, coordinator.ShoppingWindow);
            Assert.Equal("Reopen Search", model.SearchMarketsButtonText);
            first.WindowState = WindowState.Minimized;
            model.OpenShopping();
            Assert.Equal(WindowState.Normal, first.WindowState);
            var shopping = (ColonizationShoppingViewModel)first.DataContext;
            shopping.ReferenceSystem = "Sol";
            shopping.RadiusText = "25";
            first.Close();
            model.OpenShopping();
            Assert.NotSame(first, coordinator.ShoppingWindow);
            var reopened = (ColonizationShoppingViewModel)coordinator.ShoppingWindow!.DataContext!;
            Assert.Equal("Sol", reopened.ReferenceSystem);
            Assert.Equal("25", reopened.RadiusText);
            model.Projects[1].IsShoppingSelected = true;
            model.OpenShopping();
            Assert.NotSame(reopened, coordinator.ShoppingWindow.DataContext);
            Assert.Equal(
                "First, Second",
                ((ColonizationShoppingViewModel)coordinator.ShoppingWindow.DataContext!).ProjectSummary
            );
            model.IsEnabled = false;
            Assert.Null(coordinator.ShoppingWindow);
            model.IsEnabled = true;
            await model.RefreshAsync();
            model.Projects[0].IsShoppingSelected = true;
            model.OpenShopping();
            await model.SetCommanderAsync(null);
            Assert.Null(coordinator.ShoppingWindow);
            await model.SetCommanderAsync("Example Cmdr");
            model.Projects[0].IsShoppingSelected = true;
            model.OpenShopping();
            owner.Close();
            Assert.Null(coordinator.ShoppingWindow);
            coordinator.ShowShopping(ColonizationShoppingViewModelTests.Projects());
            Assert.Null(coordinator.ShoppingWindow);
        }
        finally
        {
            owner.Close();
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public async Task ProjectShoppingCheckboxEnablesSearchButtonWithoutChangingShow()
    {
        using var http = new HttpClient(new RavenHandler());
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithExternalNetworkClient(http)
        );
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Example Cmdr");
        IReadOnlyList<ColonizationProject>? selected = null;
        main.Colonization.SetShoppingOpener(projects => selected = projects);
        var view = new Views.ColonizationView { DataContext = main };
        var owner = new Window
        {
            Content = view,
            Width = 1400,
            Height = 1000,
        };
        try
        {
            owner.Show();
            using WriteableBitmap? frame = owner.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Button search = view.FindControl<Button>("SearchMarketsButton")!;
            Button wishList = view.FindControl<Button>("OpenWishListButton")!;
            Assert.Equal("Wish List", wishList.Content);
            Assert.Same(search.Parent, wishList.Parent);
            Assert.True(wishList.IsEnabled);
            Assert.False(search.IsEnabled);
            CheckBox shopping = view.GetVisualDescendants()
                .OfType<CheckBox>()
                .First(box =>
                    box.DataContext is ColonizationProjectRowViewModel
                    && box.GetValue(Avalonia.Automation.AutomationProperties.NameProperty)
                        == "Select project for shopping"
                );
            shopping.IsChecked = true;
            Assert.True(search.IsEnabled);
            search.Command!.Execute(null);
            Assert.Equal("1", Assert.Single(selected!).BuildId);
            Assert.False(main.Colonization.HasUnsavedProjectVisibility);
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task ShoppingSearchUsesTheApplicationNetworkTransport()
    {
        var handler = new RavenHandler();
        using var http = new HttpClient(handler);
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithExternalNetworkClient(http)
        );
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Example Cmdr");
        handler.MarketSources.Clear();
        using ColonizationShoppingViewModel shopping = main.Colonization.CreateShopping(
            main.Colonization.Projects.Select(row => row.Project).ToArray()
        );
        shopping.ReferenceSystem = "Sol";
        await shopping.SearchAsync();
        Assert.Contains("spansh.co.uk", handler.MarketSources);
        Assert.Contains("api.ardent-insight.com", handler.MarketSources);
        Assert.Empty(shopping.Warnings);
    }

    [AvaloniaFact]
    public void DesignerWindowClosesWithoutStartingRequests()
    {
        var window = new ColonizationShoppingWindow();
        window.Show();
        Assert.False(window.FindControl<Button>("RunMarketSearch")!.IsEnabled);
        window.Close();
    }

    private sealed class RavenHandler : HttpMessageHandler
    {
        public ConcurrentBag<string> MarketSources { get; } = [];
        public IReadOnlyList<ColonizationFleetCarrier> LinkedCarriers { get; init; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request.RequestUri!.Host is "spansh.co.uk" or "api.ardent-insight.com")
            {
                MarketSources.Add(request.RequestUri.Host);
                string response = request.Method == HttpMethod.Post ? "{\"count\":0,\"results\":[]}" : "[]";
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(response, Encoding.UTF8, "application/json"),
                    }
                );
            }
            string path = request.RequestUri.AbsolutePath;
            string json = path switch
            {
                _ when path.EndsWith("/active", StringComparison.Ordinal) => JsonSerializer.Serialize(
                    ColonizationShoppingViewModelTests.Projects()
                ),
                _ when path.EndsWith("/primary", StringComparison.Ordinal) => "null",
                _ when path.EndsWith("/fc/all", StringComparison.Ordinal) => JsonSerializer.Serialize(LinkedCarriers),
                _ when path.StartsWith("/api/fc/", StringComparison.Ordinal) => JsonSerializer.Serialize(
                    LinkedCarriers.FirstOrDefault(carrier =>
                        path.EndsWith("/" + carrier.MarketId, StringComparison.Ordinal)
                    )
                ),
                _ => "[]",
            };
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
