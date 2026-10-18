using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Behaviors;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Tests.Platform;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed partial class ColonizationShoppingPresentationTests
{
    [AvaloniaFact]
    public async Task LeftClicksAllocateTonnesUpdateTheTreeAndRestoreNeedsWhenRemoved()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SrvSurvey-wishlist-" + Guid.NewGuid().ToString("N"));
        using var http = new HttpClient(new RavenHandler());
        using var model = new ColonizationViewModel(
            new ColonizationSettingsStore(Path.Combine(directory, "ui.json")),
            new RavenColonialClient(http)
        );
        model.MarketSearchFactory = () =>
            new ColonizationShoppingViewModelTests.SearchStub([
                ColonizationShoppingViewModelTests.Quote(1, "steel", 60),
                ColonizationShoppingViewModelTests.Quote(1, "water", 20),
                ColonizationShoppingViewModelTests.Quote(2, "steel", 500),
                ColonizationShoppingViewModelTests.Quote(2, "water", 80),
            ]);
        var owner = new Window();
        using var coordinator = new ColonizationProjectPreviewWindowCoordinator(model, owner);
        using var overlayHost = new HostedOverlayTestHarness(new OverlayWindowRegistry());
        using var overlay = new ColonizationWishListOverlayCoordinator(model.WishList, overlayHost.Session);
        try
        {
            owner.Show();
            model.IsEnabled = true;
            await model.SetCommanderAsync("Example Cmdr");
            model.UpdateSystemContext("Sol", null, 1);
            foreach (ColonizationProjectRowViewModel project in model.Projects)
            {
                project.IsShoppingSelected = true;
            }
            model.OpenShopping();
            ColonizationShoppingViewModel search = model.MarketSearchWorkspace!;
            var window = new ColonizationShoppingTestWindow(search);
            window.Show(owner);
            await search.SearchAsync();
            model.OpenWishListCommand.Execute(null);
            ColonizationWishListWindow wish = coordinator.WishListWindow!;
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            ShoppingMarketRowViewModel first = search.Results[0];
            Button system = window
                .GetVisualDescendants()
                .OfType<Button>()
                .First(button => button.DataContext == first && ClipboardCopyBehavior.GetText(button) is string);
            LeftClick(window, system);
            Assert.Equal("Sol", await window.Clipboard!.TryGetTextAsync());
            Button station = window
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Command == first.ToggleWishListCommand);
            LeftClick(window, station);
            Assert.True(overlay.IsVisible);
            Assert.Equal("Steel: 40, Water: 30", search.NeedsText);
            Assert.Equal(2, search.Results.Count);
            Assert.Equal("Steel: 60", model.WishList.Stations[0].Children[0].Label);
            Assert.Equal("Water: 20", model.WishList.Stations[0].Children[1].Label);
            search.Results.Single(row => row.Station == "Port 2").IsExpanded = true;
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
                frame.Save(
                    Path.Combine(Path.GetTempPath(), "srvsurvey-shopping-partial-plan.png"),
                    PngBitmapEncoderOptions.Default
                );
            }
            ShoppingSupplyViewModel steel = search
                .Results.Single(row => row.Station == "Port 2")
                .AvailableSupplies.Single(supply => supply.Need.Commodity == "steel");
            Assert.Equal("Steel: 40", steel.TableLabel);
            Button steelButton = window
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Command == steel.ToggleWishListCommand);
            LeftClick(window, steelButton);
            Assert.Equal("Water: 30", search.NeedsText);
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            ShoppingSupplyViewModel water = search
                .Results.Single(row => row.Station == "Port 2")
                .AvailableSupplies.Single(supply => supply.Need.Commodity == "water");
            Button waterButton = window
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Command == water.ToggleWishListCommand);
            LeftClick(window, waterButton);
            Assert.True(search.IsFullyPlanned);
            Assert.All(search.Results, row => Assert.True(row.IsWishListed));
            Assert.Empty(window.FindControl<ItemsControl>("ShoppingNeeds")!.ItemsSource!);
            using (WriteableBitmap? frame = wish.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
                frame.Save(
                    Path.Combine(Path.GetTempPath(), "srvsurvey-wishlist-tree.png"),
                    PngBitmapEncoderOptions.Default
                );
            }
            Assert.Equal("Steel: 40", model.WishList.Stations[1].Children[0].Label);
            Button wishSystem = wish.GetVisualDescendants()
                .OfType<Button>()
                .First(button => Equals(ClipboardCopyBehavior.GetText(button), "Sol"));
            IClipboard clipboard = Assert.IsType<IClipboard>(wish.Clipboard, exactMatch: false);
            await clipboard.SetTextAsync("before");
            LeftClick(wish, wishSystem);
            Assert.Equal("Sol", await clipboard.TryGetTextAsync());
            window.Hide();
            model.SelectedWorkspaceTabIndex = 0;
            Assert.True(overlay.IsVisible);
            Assert.True(wish.IsVisible);
            model.OpenShopping();
            ColonizationShoppingViewModel restored = model.MarketSearchWorkspace!;
            Assert.Same(search, restored);
            Assert.True(restored.IsFullyPlanned);
            using (WriteableBitmap? frame = wish.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            ColonizationWishListNode commodity = model.WishList.Stations[0].Children[0];
            ColonizationWishListOverlayWindow live = Assert.IsType<ColonizationWishListOverlayWindow>(
                overlayHost.PreparedWindows.Single()
            );
            using (WriteableBitmap? frame = live.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
                frame.Save(
                    Path.Combine(Path.GetTempPath(), "srvsurvey-wishlist-overlay-live.png"),
                    PngBitmapEncoderOptions.Default
                );
            }
            Button overlaySystem = live.GetVisualDescendants()
                .OfType<Button>()
                .First(button =>
                    ReferenceEquals(button.DataContext, model.WishList.Stations[0])
                    && ClipboardCopyBehavior.GetText(button) is string
                );
            IClipboard overlayClipboard = Assert.IsType<IClipboard>(live.Clipboard, exactMatch: false);
            await overlayClipboard.SetTextAsync("before");
            LeftClick(live, overlaySystem);
            Assert.Equal("Sol", await overlayClipboard.TryGetTextAsync());
            Button remove = live.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Command == commodity.RemoveCommand);
            LeftClick(live, remove);
            Assert.Equal("Steel: 60", restored.NeedsText);
            using (WriteableBitmap? frame = wish.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            ColonizationWishListNode port = model.WishList.Stations[1];
            using (WriteableBitmap? frame = live.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            Button removePort = live.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Command == port.RemoveCommand);
            LeftClick(live, removePort);
            Assert.Equal("Steel: 100, Water: 30", restored.NeedsText);
            window.FindControl<Button>("ClearMarketSearch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(model.WishList.HasItems);
            Assert.False(overlay.IsVisible);
            Assert.Empty(restored.Results);
        }
        finally
        {
            owner.Close();
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public async Task WishListPopoutReopensIndependentlyAndClosesOnCommanderConsentAndOwnerChanges()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SrvSurvey-wishlist-" + Guid.NewGuid().ToString("N"));
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
            coordinator.ShowWishList();
            Assert.Null(coordinator.WishListWindow);
            Assert.False(model.OpenWishListCommand.CanExecute(null));
            model.IsEnabled = true;
            coordinator.ShowWishList();
            Assert.Null(coordinator.WishListWindow);
            await model.SetCommanderAsync("Example Cmdr");
            model.OpenWishListCommand.Execute(null);
            ColonizationWishListWindow first = coordinator.WishListWindow!;
            first.WindowState = WindowState.Minimized;
            model.OpenWishListCommand.Execute(null);
            Assert.Same(first, coordinator.WishListWindow);
            Assert.Equal(WindowState.Normal, first.WindowState);
            first.Close();
            Assert.Null(coordinator.WishListWindow);
            model.OpenWishListCommand.Execute(null);
            Assert.NotSame(first, coordinator.WishListWindow);
            await model.SetCommanderAsync(null);
            Assert.Null(coordinator.WishListWindow);
            await model.SetCommanderAsync("Example Cmdr");
            model.OpenWishListCommand.Execute(null);
            model.IsEnabled = false;
            Assert.Null(coordinator.WishListWindow);
            model.IsEnabled = true;
            model.OpenWishListCommand.Execute(null);
            owner.Close();
            Assert.Null(coordinator.WishListWindow);
            coordinator.ShowWishList();
            Assert.Null(coordinator.WishListWindow);
            var designer = new ColonizationWishListWindow();
            designer.Show();
            designer.Close();
        }
        finally
        {
            owner.Close();
            Directory.Delete(directory, true);
        }
    }

    private static void LeftClick(Window window, Control control)
    {
        Point point =
            control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("The control is not attached to the window.");
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
    }
}
