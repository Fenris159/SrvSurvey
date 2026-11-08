using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform.Frontier;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class CommanderProfileViewModelTests
{
    private static readonly string[] CommanderFactionDisplayOrder = ["Federation", "Empire", "Alliance"];
    private static readonly string[] NavalFactions = ["Federation", "Empire"];
    private static readonly string[] NavalRankNames = ["Rear Admiral", "Duke"];

    [AvaloniaTheory]
    [InlineData("blue-dark", 1000)]
    [InlineData("blue-light", 600)]
    [InlineData("orange-dark", 1000)]
    [InlineData("green-light", 600)]
    [InlineData("green-dark", 1000)]
    [InlineData("monochrome-dark", 1000)]
    public async Task RankProgressAndFactionOrderRemainReadableAcrossThemes(string palette, int width)
    {
        using var culture = new RankProgressCultureScope();
        FrontierAccountSnapshot snapshot = RankProgressSnapshot();
        using var model = new CommanderProfileViewModel(
            new StubAccountService(new(true, snapshot, snapshot.FetchedAt))
        );
        await model.SetCommanderContextAsync("F123", "Fenris", false);
        await model.OpenAsync();
        SupplyRankProgress(model);
        Assert.Equal(CommanderFactionDisplayOrder, model.CommanderReputation.Select(row => row.Faction));
        FrontierRankCardViewModel combat = model.Ranks.Single(rank => rank.Category == "Combat");
        Assert.Equal(37.625, combat.ProgressValue);
        Assert.Equal("37.63%", combat.ProgressText);
        Assert.Equal("Highest rank", model.Ranks.Single(rank => rank.Category == "Trade").ProgressText);
        Assert.Equal("0%", model.Ranks.Single(rank => rank.Category == "Exobiology").ProgressText);
        Assert.Equal("Progress unavailable", model.Ranks.Single(rank => rank.Category == "CQC").ProgressText);
        Assert.Equal(37, model.Ranks.Single(rank => rank.Category == "Federation").ProgressValue);
        Assert.Equal(82, model.Ranks.Single(rank => rank.Category == "Empire").ProgressValue);
        var view = new FrontierCommanderTabView { DataContext = model };
        var window = new Window
        {
            Classes = { "app-window" },
            Content = view,
            Width = width,
            Height = 1050,
        };
        string themeRoot = Path.Combine(Path.GetTempPath(), $"SrvSurvey-rank-{Guid.NewGuid():N}");
        var theme = new RavenThemeService(
            Application.Current!,
            new ThemePreferenceStore(Path.Combine(themeRoot, "ui.json"))
        );
        string previous = theme.Current.Key;
        try
        {
            theme.Select(palette);
            theme.ApplyCurrent();
            window.Show();
            ScrollViewer scroller = view.FindControl<ScrollViewer>("CommanderScroller")!;
            CaptureConsole(window, $"rank-progress-{palette}-{width}");
            scroller.Offset = new Vector(0, 200);
            CaptureConsole(window, $"rank-progress-{palette}-{width}-scrolled");
            Assert.True(scroller.Extent.Width <= scroller.Viewport.Width + 1);
            ItemsControl pilotRanks = view.FindControl<ItemsControl>("CommanderRanks")!;
            Assert.Equal(7, pilotRanks.Items.Count);
            Assert.DoesNotContain(
                pilotRanks.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text is "Rear Admiral" or "Duke"
            );
            ItemsControl superpowers = view.FindControl<ItemsControl>("CommanderFactionRows")!;
            ProgressBar[] navyBars = superpowers
                .GetVisualDescendants()
                .OfType<ProgressBar>()
                .Where(bar => bar.IsEffectivelyVisible)
                .ToArray();
            Assert.Equal(new double[] { 37, 82 }, navyBars.Select(bar => bar.Value));
            ProgressBar[] bars = view.GetVisualDescendants()
                .OfType<ProgressBar>()
                .Where(bar => bar.IsEffectivelyVisible)
                .ToArray();
            Assert.Equal(8, bars.Length);
            Assert.Equal(new double[] { 37.625, 100, 4.35, 12.5, 0, 0, 37, 82 }, bars.Select(bar => bar.Value));
            Assert.All(
                bars,
                bar =>
                {
                    Assert.Equal(
                        Color.Parse(theme.Current.AccentColor),
                        Assert.IsType<SolidColorBrush>(bar.Foreground, exactMatch: false).Color
                    );
                    Border card = bar.GetVisualAncestors()
                        .OfType<Border>()
                        .First(border =>
                            border.Classes.Contains("commander-rank") || border.Classes.Contains("commander-faction")
                        );
                    TextBlock rankName = Assert.Single(
                        card.GetVisualDescendants().OfType<TextBlock>(),
                        text => text.Classes.Contains("rank-name")
                    );
                    Point namePosition = rankName.TranslatePoint(default, card)!.Value;
                    Point position = bar.TranslatePoint(default, card)!.Value;
                    Assert.True(bar.Bounds.Width >= 50);
                    Assert.True(position.X >= namePosition.X - 1);
                    Assert.True(position.X + bar.Bounds.Width <= namePosition.X + rankName.Bounds.Width + 1);
                    Assert.True(position.Y >= namePosition.Y + rankName.Bounds.Height + 3);
                    Assert.True(position.Y + bar.Bounds.Height <= card.Bounds.Height + 1);
                }
            );
            model.UpdateJournalState(
                "F123",
                [
                    ParseJournalEvent(
                        """{"timestamp":"1970-01-01T00:03:00Z","event":"Progress","Federation":38,"Empire":83}"""
                    ),
                ]
            );
            CaptureConsole(window, $"rank-progress-{palette}-{width}-updated");
            Assert.Equal(
                new double[] { 38, 83 },
                superpowers
                    .GetVisualDescendants()
                    .OfType<ProgressBar>()
                    .Where(bar => bar.IsEffectivelyVisible)
                    .Select(bar => bar.Value)
            );
        }
        finally
        {
            window.Close();
            theme.Select(previous);
            theme.ApplyCurrent();
            if (Directory.Exists(themeRoot))
            {
                Directory.Delete(themeRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task JournalRefreshDeliversRankProgressAndPromotionsToTheCommanderCards()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-rank-refresh-{Guid.NewGuid():N}");
        string journals = Path.Combine(root, "journals");
        Directory.CreateDirectory(journals);
        string journal = Path.Combine(journals, "Journal.2026-11-08T000000.01.log");
        FrontierAccountSnapshot snapshot = RankProgressSnapshot();
        using var profile = new CommanderProfileViewModel(
            new StubAccountService(new(true, snapshot, snapshot.FetchedAt))
        );
        try
        {
            await File.WriteAllTextAsync(
                journal,
                """
                {"timestamp":"2026-11-08T00:00:00Z","event":"Fileheader","Odyssey":true}
                {"timestamp":"2026-11-08T00:00:01Z","event":"Commander","FID":"F123","Name":"Fenris"}
                {"timestamp":"2026-11-08T00:00:02Z","event":"LoadGame","Commander":"Fenris","FID":"F123","Ship":"federation_corvette","Odyssey":true}
                {"timestamp":"2026-11-08T00:00:03Z","event":"Rank","Combat":6}
                {"timestamp":"2026-11-08T00:00:04Z","event":"Progress","Combat":10.5}
                """ + "\n"
            );
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                Path.Combine(root, "data"),
                Path.Combine(root, "cache"),
                []
            )
            {
                SettingsFrontierId = "F123",
            };
            await profile.SetCommanderContextAsync("F123", "Fenris", false);
            await profile.OpenAsync();
            await using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
                journals,
                builder => builder.WithAppDataPaths(paths).WithFrontierProfile(profile)
            );
            await main.RefreshAsync();
            Assert.Equal(10.5, profile.Ranks.Single(rank => rank.Category == "Combat").ProgressValue);
            await File.AppendAllTextAsync(
                journal,
                """{"timestamp":"2026-11-08T00:00:05Z","event":"Progress","Combat":35.5}""" + "\n"
            );
            await main.RefreshAsync();
            Assert.Equal(35.5, profile.Ranks.Single(rank => rank.Category == "Combat").ProgressValue);
            await File.AppendAllTextAsync(
                journal,
                """{"timestamp":"2026-11-08T00:00:06Z","event":"Promotion","Combat":7}""" + "\n"
            );
            await main.RefreshAsync();
            FrontierRankCardViewModel promoted = profile.Ranks.Single(rank => rank.Category == "Combat");
            Assert.Equal("Deadly", promoted.Name);
            Assert.Equal("Progress unavailable", promoted.ProgressText);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task NavalRanksRemainAvailableWithoutOptionalReputation()
    {
        using var culture = new RankProgressCultureScope();
        FrontierAccountSnapshot snapshot = RankProgressSnapshot() with { CommanderReputation = [], Carrier = null };
        using var model = new CommanderProfileViewModel(
            new StubAccountService(new(true, snapshot, snapshot.FetchedAt))
        );
        await model.SetCommanderContextAsync("F123", "Fenris", false);
        await model.OpenAsync();

        Assert.Equal(NavalFactions, model.CommanderReputation.Select(row => row.Faction));
        Assert.Equal(NavalRankNames, model.CommanderReputation.Select(row => row.NavalRank!.Name));
        Assert.All(model.CommanderReputation, row => Assert.Equal("Unavailable", row.Score));
    }

    [Fact]
    public async Task AttributedJournalRanksFillCategoriesMissingFromCapi()
    {
        FrontierAccountSnapshot snapshot = RankProgressSnapshot() with
        {
            Ranks = [new("combat", "Combat", 6, "Dangerous")],
        };
        using var model = new CommanderProfileViewModel(
            new StubAccountService(new(true, snapshot, snapshot.FetchedAt))
        );
        await model.SetCommanderContextAsync("F123", "Fenris", false);
        await model.OpenAsync();
        model.UpdateJournalState(
            "F123",
            [
                ParseJournalEvent(
                    """{"timestamp":"1970-01-01T00:01:00Z","event":"Rank","Soldier":2,"Exobiologist":7}"""
                ),
                ParseJournalEvent(
                    """{"timestamp":"1970-01-01T00:02:00Z","event":"Progress","Soldier":12.5,"Exobiologist":25}"""
                ),
            ]
        );

        Assert.Equal("Rookie", model.PilotRanks.Single(row => row.Category == "Mercenary").Name);
        Assert.Equal(12.5, model.PilotRanks.Single(row => row.Category == "Mercenary").ProgressValue);
        Assert.Equal("Geneticist", model.PilotRanks.Single(row => row.Category == "Exobiology").Name);
        Assert.Equal(25, model.PilotRanks.Single(row => row.Category == "Exobiology").ProgressValue);
    }

    [Fact]
    public async Task RankProgressCannotLeakAcrossCommanderBoundaries()
    {
        using var culture = new RankProgressCultureScope();
        FrontierAccountSnapshot snapshot = RankProgressSnapshot();
        using var model = new CommanderProfileViewModel(
            new StubAccountService(new(true, snapshot, snapshot.FetchedAt))
        );
        await model.SetCommanderContextAsync("F123", "Fenris", false);
        await model.OpenAsync();
        SupplyRankProgress(model);
        model.UpdateJournalState(
            "F123",
            [
                ParseJournalEvent(
                    """{"timestamp":"1970-01-01T00:04:00Z","event":"Commander","FID":"F456","Name":"Other"}"""
                ),
                ParseJournalEvent("""{"timestamp":"1970-01-01T00:05:00Z","event":"Progress","Combat":99}"""),
            ]
        );
        Assert.Equal(37.625, model.Ranks.Single(rank => rank.Category == "Combat").ProgressValue);
        await model.SetCommanderContextAsync("F456", "Other", false);
        await model.SetCommanderContextAsync("F123", "Fenris", false);
        await model.OpenAsync();
        Assert.Equal("Progress unavailable", model.Ranks.Single(rank => rank.Category == "Combat").ProgressText);
        model.UpdateJournalState(
            "F123",
            [
                ParseJournalEvent("""{"timestamp":"1970-01-01T00:06:00Z","event":"Rank","Combat":6}"""),
                ParseJournalEvent("""{"timestamp":"1970-01-01T00:07:00Z","event":"Progress","Combat":10.5}"""),
            ]
        );
        Assert.Equal("10.5%", model.Ranks.Single(rank => rank.Category == "Combat").ProgressText);
        model.UpdateJournalState(
            "F123",
            [ParseJournalEvent("""{"timestamp":"1970-01-01T00:08:00Z","event":"Promotion","Combat":7}""")]
        );
        FrontierRankCardViewModel promoted = model.Ranks.Single(rank => rank.Category == "Combat");
        Assert.Equal("Deadly", promoted.Name);
        Assert.Equal("Progress unavailable", promoted.ProgressText);
    }

    private static FrontierAccountSnapshot RankProgressSnapshot() =>
        CreateSnapshot(DateTimeOffset.UnixEpoch) with
        {
            Ranks =
            [
                new("combat", "Combat", 6, "Dangerous"),
                new("trade", "Trade", 13, "Elite V"),
                new("explore", "Exploration", 8, "Elite"),
                new("soldier", "Mercenary", 2, "Rookie"),
                new("exobiologist", "Exobiology", 7, "Geneticist"),
                new("cqc", "CQC", 0, "Helpless"),
                new("federation", "Federation", 12, "Rear Admiral"),
                new("empire", "Empire", 12, "Duke"),
            ],
            Powerplay = new("A. Lavigny-Duval", 161, 1_263_182, DateTimeOffset.UnixEpoch),
            CommanderReputation =
            [
                new("Alliance", 75),
                new("Independent", -1),
                new("Other", 10),
                new("Empire", 75),
                new("Federation", 74),
            ],
        };

    private static void SupplyRankProgress(CommanderProfileViewModel model) =>
        model.UpdateJournalState(
            "F123",
            [
                ParseJournalEvent(
                    """{"timestamp":"1970-01-01T00:01:00Z","event":"Rank","Combat":6,"Trade":13,"Explore":8,"Soldier":2,"Exobiologist":7,"Federation":12,"Empire":12}"""
                ),
                ParseJournalEvent(
                    """{"timestamp":"1970-01-01T00:02:00Z","event":"Progress","Combat":37.625,"Trade":0,"Explore":4.35,"Soldier":12.5,"Exobiologist":0,"Federation":37,"Empire":82}"""
                ),
            ]
        );

    private sealed class RankProgressCultureScope : IDisposable
    {
        private readonly CultureInfo previous = CultureInfo.CurrentCulture;

        public RankProgressCultureScope()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
