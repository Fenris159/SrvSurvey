using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Platform.Frontier;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class CommanderProfileViewModelTests
{
    [AvaloniaTheory]
    [InlineData("blue-dark", 1000)]
    [InlineData("blue-dark", 600)]
    [InlineData("blue-light", 1000)]
    [InlineData("blue-light", 600)]
    [InlineData("orange-dark", 1000)]
    [InlineData("orange-dark", 600)]
    [InlineData("green-light", 1000)]
    [InlineData("green-light", 600)]
    [InlineData("green-dark", 1000)]
    [InlineData("green-dark", 600)]
    [InlineData("monochrome-dark", 1000)]
    [InlineData("monochrome-dark", 600)]
    public async Task CommanderConsoleKeepsReadoutsAndNavigationReadable(string palette, int width)
    {
        FrontierAccountSnapshot snapshot = ConsoleSnapshot();
        using var model = new CommanderProfileViewModel(
            new StubAccountService(new FrontierAccountState(true, snapshot, snapshot.FetchedAt))
        );
        await model.OpenAsync();
        model.PaneStates.CurrentShipModules.IsExpanded = true;
        model.PaneStates.ShipyardModules.IsExpanded = true;
        var view = new CommanderProfileView { DataContext = model };
        var window = new Window
        {
            Classes = { "app-window" },
            Content = view,
            Width = width,
            Height = 960,
        };
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-console-{Guid.NewGuid():N}");
        var theme = new RavenThemeService(
            Application.Current!,
            new ThemePreferenceStore(Path.Combine(root, "ui.json"))
        );
        string previous = theme.Current.Key;
        try
        {
            theme.Select(palette);
            theme.ApplyCurrent();
            window.Show();
            CaptureConsole(window, $"{palette}-{width}");
            FrontierCommanderTabView commander = Assert.Single(
                view.GetVisualDescendants().OfType<FrontierCommanderTabView>()
            );
            ScrollViewer scroller = commander.FindControl<ScrollViewer>("CommanderScroller")!;
            Assert.NotNull(scroller);
            Assert.True(scroller.Extent.Width <= scroller.Viewport.Width + 1);
            Assert.Equal(model.CommanderName, view.FindControl<TextBlock>("CommanderIdentity")!.Text);
            Assert.Equal(32, view.FindControl<TextBlock>("CommanderIdentity")!.FontSize);
            Assert.Equal(model.CurrentLocation, commander.FindControl<TextBlock>("CommanderLocation")!.Text);
            Assert.Equal(34, commander.FindControl<TextBlock>("CommanderLocation")!.FontSize);
            Assert.Equal(
                new Thickness(3, 0, 0, 0),
                commander.FindControl<Border>("CommanderFlightReadout")!.BorderThickness
            );
            Assert.Same(model.RefreshCommand, view.FindControl<Button>("RefreshFrontier")!.Command);
            Assert.NotNull(view.FindControl<ComboBox>("CommanderSelector")!.SelectedItem);
            Border[] themedEmblems = commander
                .GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("theme-rank-emblem") && border.IsVisible)
                .ToArray();
            Assert.Equal(3, themedEmblems.Length);
            Assert.All(
                themedEmblems,
                emblem =>
                    Assert.Equal(
                        Color.Parse(theme.Current.TextColor),
                        Assert.IsAssignableFrom<ISolidColorBrush>(emblem.Background).Color
                    )
            );
            ResponsiveColumnsPanel finances = commander.FindControl<ResponsiveColumnsPanel>("CommanderFinance")!;
            Assert.Equal(3, finances.Children.Count(child => child.IsVisible));
            Assert.False(commander.FindControl<StackPanel>("CommanderDebt")!.IsVisible);
            foreach (
                TextBlock value in finances
                    .GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Where(value => value.IsEffectivelyVisible)
            )
            {
                Assert.True(value.Bounds.Height > 0);
                Point position = value.TranslatePoint(default, finances)!.Value;
                Assert.True(position.X + value.Bounds.Width <= finances.Bounds.Width + 1);
            }

            TabControl tabs = view.FindControl<TabControl>("CommanderProfileTabs")!;
            Assert.Equal(6, tabs.Items.Count);
            Expander fleet = commander.FindControl<Expander>("CommanderFleet")!;
            fleet.IsExpanded = false;
            Assert.False(model.PaneStates.CommanderOwnedFleet.IsExpanded);
            for (int index = 1; index < tabs.Items.Count; index++)
            {
                tabs.SelectedIndex = index;
                CaptureConsole(window, $"{palette}-{width}-tab-{index}");
                Assert.True(Assert.IsType<TabItem>(tabs.SelectedItem).IsSelected);
                Control page = Assert.IsAssignableFrom<Control>(Assert.IsType<TabItem>(tabs.SelectedItem).Content);
                ScrollViewer pageScroller = page.FindControl<ScrollViewer>("FrontierPageScroller")!;
                Assert.True(
                    pageScroller.Extent.Width <= pageScroller.Viewport.Width + 1,
                    $"Tab {index} overflows the {width}px workspace: {pageScroller.Extent.Width} > {pageScroller.Viewport.Width}."
                );
                pageScroller.Offset = new Vector(
                    0,
                    Math.Max(0, pageScroller.Extent.Height - pageScroller.Viewport.Height)
                );
                CaptureConsole(window, $"{palette}-{width}-tab-{index}-bottom");
                Assert.True(pageScroller.Extent.Width <= pageScroller.Viewport.Width + 1);
                foreach (
                    ScrollViewer table in page.GetVisualDescendants()
                        .OfType<ScrollViewer>()
                        .Where(control =>
                            control.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto
                            && control.IsEffectivelyVisible
                        )
                )
                {
                    table.Offset = new Vector(Math.Max(0, table.Extent.Width - table.Viewport.Width), 0);
                    CaptureConsole(window, $"{palette}-{width}-tab-{index}-table-end");
                    Assert.True(table.Offset.X >= 0);
                }
            }
            tabs.SelectedIndex = 0;
            CaptureConsole(window, $"{palette}-{width}-returned");
            Assert.False(
                Assert
                    .Single(view.GetVisualDescendants().OfType<FrontierCommanderTabView>())
                    .FindControl<Expander>("CommanderFleet")!
                    .IsExpanded
            );
            Button account = view.FindControl<Button>("CommanderAccountMenu")!;
            account.Flyout!.ShowAt(account);
            CaptureConsole(window, $"{palette}-{width}-account");
            MenuFlyout flyout = Assert.IsType<MenuFlyout>(account.Flyout);
            MenuItem unlink = Assert.Single(flyout.Items.OfType<MenuItem>());
            Assert.Same(model.UnlinkCommand, unlink.Command);
            Assert.True(unlink.IsEffectivelyVisible);
            flyout.Hide();
        }
        finally
        {
            window.Close();
            theme.Select(previous);
            theme.ApplyCurrent();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(0, 125000)]
    [InlineData(125000, 0)]
    [InlineData(0, -1)]
    public async Task DebtOnlyUsesSpaceWhenTheReportedAmountIsNonzero(long initialDebt, long refreshedDebt)
    {
        FrontierAccountSnapshot first = ConsoleSnapshot() with { Debt = initialDebt };
        FrontierAccountSnapshot refreshed = first with
        {
            Debt = refreshedDebt,
            FetchedAt = first.FetchedAt.AddMinutes(1),
        };
        using var model = new CommanderProfileViewModel(
            new StubAccountService(new FrontierAccountState(true, first, first.FetchedAt), refreshed)
        );
        Assert.False(model.HasDebt);
        Assert.False(model.HasMarketServices);
        Assert.False(model.HasMarketEconomies);
        await model.OpenAsync();
        Assert.True(model.HasMarketServices);
        Assert.True(model.HasMarketEconomies);
        var view = new FrontierCommanderTabView { DataContext = model };
        var window = new Window
        {
            Content = view,
            Width = 1000,
            Height = 800,
        };
        try
        {
            window.Show();
            CaptureConsole(window, $"debt-{initialDebt}");
            StackPanel debt = view.FindControl<StackPanel>("CommanderDebt")!;
            ResponsiveColumnsPanel finances = view.FindControl<ResponsiveColumnsPanel>("CommanderFinance")!;
            Assert.Equal(initialDebt != 0, debt.IsVisible);
            Assert.Equal(initialDebt == 0 ? 3 : 4, finances.Children.Count(child => child.IsVisible));
            await model.RefreshAsync();
            CaptureConsole(window, $"debt-updated-{refreshedDebt}");
            Assert.Equal(refreshedDebt != 0, model.HasDebt);
            Assert.Equal(refreshedDebt != 0, debt.IsVisible);
            Assert.Equal(refreshedDebt == 0 ? 3 : 4, finances.Children.Count(child => child.IsVisible));
            Assert.Equal(model.Debt, debt.GetVisualDescendants().OfType<TextBlock>().Last().Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CommanderConsoleReflowsLongReadoutsWhenTheWindowIsResized()
    {
        FrontierAccountSnapshot snapshot = ConsoleSnapshot() with
        {
            CommanderName = "Fenris of the Perseus Survey Expedition",
            LastSystem = "Swoilz NL-N c23-416",
            LastStation = "Perseus Science and Survey Anchorage",
        };
        using var model = new CommanderProfileViewModel(
            new StubAccountService(new FrontierAccountState(true, snapshot, snapshot.FetchedAt))
        );
        await model.OpenAsync();
        var view = new CommanderProfileView { DataContext = model };
        var window = new Window
        {
            Classes = { "app-window" },
            Content = view,
            Width = 1000,
            Height = 900,
        };
        try
        {
            window.Show();
            foreach (int width in new[] { 1000, 600, 1000 })
            {
                window.Width = width;
                CaptureConsole(window, $"long-readouts-{width}");
                FrontierCommanderTabView commander = Assert.Single(
                    view.GetVisualDescendants().OfType<FrontierCommanderTabView>()
                );
                ScrollViewer scroller = commander.FindControl<ScrollViewer>("CommanderScroller")!;
                Assert.True(scroller.Extent.Width <= scroller.Viewport.Width + 1);
                Assert.Equal(width < 820 ? 1 : 0, Grid.GetRow(commander.FindControl<StackPanel>("FlightShip")!));
                Assert.Equal(width < 820 ? 1 : 0, Grid.GetRow(commander.FindControl<Expander>("CommanderFleet")!));
                Assert.Equal(
                    width < 860 ? 1 : 0,
                    Grid.GetRow(view.FindControl<StackPanel>("CommanderAccountSelection")!)
                );
                Assert.Equal(model.CurrentLocation, commander.FindControl<TextBlock>("CommanderLocation")!.Text);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(600)]
    [InlineData(1000)]
    public async Task CommanderConsoleConnectionScreenKeepsSelectionAndSignInAvailable(int width)
    {
        using var model = new CommanderProfileViewModel(
            new StubAccountService(new FrontierAccountState(false, null, null))
        );
        await model.OpenAsync();
        var view = new CommanderProfileView { DataContext = model };
        var window = new Window
        {
            Classes = { "app-window" },
            Content = view,
            Width = width,
            Height = 700,
        };
        try
        {
            window.Show();
            CaptureConsole(window, $"unlinked-{width}");
            Button connect = view.FindControl<Button>("ConnectFrontier")!;
            Assert.True(connect.IsEffectivelyVisible);
            Assert.Same(model.ConnectCommand, connect.Command);
            Assert.True(view.FindControl<ComboBox>("UnlinkedCommanderSelector")!.IsEffectivelyVisible);
            ScrollViewer scroller = view.GetVisualDescendants()
                .OfType<ScrollViewer>()
                .First(control => control.IsEffectivelyVisible);
            Assert.True(scroller.Extent.Width <= scroller.Viewport.Width + 1);
        }
        finally
        {
            window.Close();
        }
    }

    private static FrontierAccountSnapshot ConsoleSnapshot()
    {
        FrontierAccountSnapshot snapshot = CreateSnapshot(DateTimeOffset.UtcNow);
        return snapshot with
        {
            CommanderId = 472567,
            Credits = 12_604_928_718,
            Debt = 0,
            CurrentShip = snapshot.CurrentShip! with
            {
                HullValue = 200_000,
                ModulesValue = 50_000,
                Integrity = 0.96,
                ShieldUp = true,
                Modules =
                [
                    new(
                        "FrameShiftDrive",
                        1,
                        "Frame Shift Drive",
                        "4A Frame Shift Drive",
                        160000,
                        false,
                        1,
                        true,
                        1,
                        "Felicity Farseer",
                        "Increased FSD Range",
                        5,
                        ["Mass Manager"],
                        "Int_Hyperdrive_Size4_Class5"
                    ),
                ],
            },
            Market = snapshot.Market! with
            {
                Services = [new("Market", "Available"), new("Refuel", "Available"), new("Repair", "Available")],
                Economies = [new(1, "Industrial", .7), new(2, "Refinery", .3)],
                Imported = ["Basic Medicines", "Food Cartridges"],
                Exported = ["Gold", "Silver"],
                Prohibited = ["Narcotics"],
            },
            Ranks =
            [
                new("combat", "Combat", 7, "Deadly"),
                new("trade", "Trade", 8, "Elite"),
                new("explore", "Exploration", 8, "Elite"),
                new("soldier", "Mercenary", 3, "Soldier"),
                new("exobiologist", "Exobiology", 7, "Geneticist"),
                new("cqc", "CQC", 2, "Amateur"),
                new("federation", "Federation", 8, "Post Commander"),
                new("empire", "Empire", 9, "Earl"),
            ],
            Powerplay = new("Aisling Duval", 160, 1_260_198, snapshot.FetchedAt),
            CommanderReputation = [new("Federation", 100), new("Empire", 78), new("Alliance", 42)],
        };
    }

    private static void CaptureConsole(Window window, string stage)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        using WriteableBitmap? first = window.CaptureRenderedFrame();
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        string? output = Environment.GetEnvironmentVariable("SRVSURVEY_COMMANDER_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            frame.Save(Path.Combine(output, $"{stage}.png"), PngBitmapEncoderOptions.Default);
        }
    }
}
