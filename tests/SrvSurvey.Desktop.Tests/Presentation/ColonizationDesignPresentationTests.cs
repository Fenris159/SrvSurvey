using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed partial class ColonizationShoppingPresentationTests
{
    [AvaloniaTheory]
    [InlineData(1400, "monochrome-dark")]
    [InlineData(640, "monochrome-dark")]
    [InlineData(1400, "blue-light")]
    public async Task ProjectToolbarAndCarrierActionsStayOrderedWithinTheWorkspace(int width, string theme)
    {
        using var http = new HttpClient(DesignHandler());
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithExternalNetworkClient(http)
        );
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Example Cmdr");
        Assert.True(
            JournalEventEnvelope.TryParse(
                """{"event":"Loadout","CargoCapacity":500}""",
                out JournalEventEnvelope? loadout,
                out _
            )
        );
        main.Colonization.ApplyJournalEvents([loadout!]);
        main.Colonization.ProjectBuildFilter = main.Colonization.ProjectBuildOptions.Single(option =>
            option.Label == "Building"
        );
        using var preview = new DesignTheme(theme);
        var view = new ColonizationView { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 1050,
        };
        window.Classes.Add("app-window");
        try
        {
            window.Show();
            view.FindControl<ComboBox>("ProjectSystemFilter")!.BringIntoView();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            SaveColonizationDesign(frame, $"projects-{theme}-{width}");
            Control filters = view.FindControl<Control>("ProjectFilterFields")!;
            Control tools = view.FindControl<Control>("ProjectViewActions")!;
            Control summary = view.FindControl<Control>("ProjectSelectionSummary")!;
            Control carriers = view.FindControl<Control>("ProjectCarrierActions")!;
            Assert.NotNull(filters);
            Assert.NotNull(tools);
            Assert.NotNull(summary);
            Assert.NotNull(carriers);
            TextBlock cargo = Assert.Single(
                summary.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "Cargo required: 243,834 | 488 trips in current ship"
            );
            Assert.Equal(Avalonia.Media.TextWrapping.Wrap, cargo.TextWrapping);
            Assert.True(filters.TranslatePoint(default, summary)!.Value.Y < 0);
            Assert.True(tools.TranslatePoint(default, summary)!.Value.Y < 0);
            Assert.True(carriers.TranslatePoint(default, summary)!.Value.Y >= summary.Bounds.Height);
            AssertFitsPage(filters, view);
            AssertFitsPage(tools, view);
            AssertFitsPage(carriers, view);
            Assert.True(view.FindControl<Button>("LinkCarrierToSelectedProjectsButton")!.IsEffectivelyVisible);
            Assert.True(view.FindControl<Button>("UnlinkCarrierFromSelectedProjectsButton")!.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(1400, true, "monochrome-dark")]
    [InlineData(640, false, "monochrome-dark")]
    [InlineData(1400, true, "blue-light")]
    public async Task PreparationFieldsReflowAndRetainTheReviewStep(int width, bool paired, string theme)
    {
        using var http = new HttpClient(DesignHandler());
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithExternalNetworkClient(http)
        );
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Example Cmdr");
        ColonizationProjectEditorViewModel editor = main.Colonization.ProjectEditor;
        editor.UpdateContext(
            new(
                true,
                "Example Cmdr",
                "Sol",
                [1, 2, 3],
                new(10, 42, "Sol", "Orbital Construction Site: Hope", null, ["colonisationcontribution"]),
                new(
                    new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero),
                    10,
                    0,
                    false,
                    false,
                    [new("steel", "Steel", 100, 0, 1)]
                ),
                CurrentBodyId: 12,
                CurrentBodyName: "Sol 1"
            )
        );
        await editor.PrepareAsync();
        editor.SelectedBuild = editor.BuildOptions.Single(option => option.Build.BuildType == "no_truss");
        editor.SelectedLayout = "no_truss";
        using var preview = new DesignTheme(theme);
        var view = new ColonizationView { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 1100,
        };
        window.Classes.Add("app-window");
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            SaveColonizationDesign(frame, $"prepare-{theme}-{width}");
            Control site = view.FindControl<ComboBox>("ProjectPlannedSiteInput")!;
            TextBox name = view.FindControl<TextBox>("ProjectNameInput")!;
            Assert.NotNull(site);
            Assert.NotNull(name);
            Point offset = name.TranslatePoint(default, site)!.Value;
            if (paired)
            {
                Assert.Equal(0, offset.Y, 1);
                Assert.True(offset.X > site.Bounds.Width);
            }
            else
            {
                Assert.Equal(0, offset.X, 1);
                Assert.True(offset.Y >= site.Bounds.Height);
            }
            AssertFitsPage(name, view);
            AssertFitsPage(view.FindControl<ComboBox>("ProjectBuildTypeInput")!, view);
            AssertFitsPage(view.FindControl<TextBox>("ProjectBodyNameInput")!, view);
            name.Text = "Hope orbital port";
            Assert.Equal("Hope orbital port", editor.ProjectName);
            CheckBox include = view.FindControl<CheckBox>("LinkBuildCarriersCheckBox")!;
            include.IsChecked = true;
            Assert.True(editor.IncludeCarrierLinks);
            await editor.ReviewAsync();
            Assert.True(editor.IsConfirmationPending);
            Control confirmation = view.FindControl<Border>("ProjectPublishConfirmation")!;
            confirmation.BringIntoView();
            using WriteableBitmap? review = window.CaptureRenderedFrame();
            Assert.NotNull(review);
            confirmation.BringIntoView();
            using WriteableBitmap? scrolled = window.CaptureRenderedFrame();
            Assert.NotNull(scrolled);
            Assert.True(confirmation.IsEffectivelyVisible);
            AssertFitsPage(confirmation, view);
            Point bottom = confirmation.TranslatePoint(new Point(0, confirmation.Bounds.Height), window)!.Value;
            Assert.InRange(bottom.Y, confirmation.Bounds.Height, window.ClientSize.Height);
            Assert.Contains("Hope orbital port", editor.ConfirmationSummary);
            SaveColonizationDesign(scrolled, $"prepare-review-{theme}-{width}");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(1400, "monochrome-dark")]
    [InlineData(640, "monochrome-dark")]
    [InlineData(1400, "blue-light")]
    public async Task SystemEditorKeepsReviewReachableAndFieldsAligned(int width, string theme)
    {
        using var http = new HttpClient(DesignHandler());
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
        using var preview = new DesignTheme(theme);
        var view = new ColonizationView { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 1050,
        };
        window.Classes.Add("app-window");
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            SaveColonizationDesign(frame, $"system-{theme}-{width}");
            Control review = view.FindControl<Control>("SystemReviewActions")!;
            Assert.NotNull(review);
            AssertFitsPage(review, view);
            TextBox systemInput = Assert.Single(
                view.FindControl<AutoCompleteBox>("EditorSystemName")!.GetVisualDescendants().OfType<TextBox>()
            );
            Button useCurrent = view.FindControl<Button>("UseCurrentEditorSystem")!;
            Assert.Equal(useCurrent.Bounds.Height, systemInput.Bounds.Height, 1);
            Assert.Equal(0, systemInput.TranslatePoint(default, useCurrent)!.Value.Y, 1);
            Point top = review.TranslatePoint(default, window)!.Value;
            Assert.InRange(top.Y, 0, window.ClientSize.Height - review.Bounds.Height);
            Assert.InRange(window.ClientSize.Height - top.Y - review.Bounds.Height, 0, 60);
            ScrollViewer pageScroll = Assert.Single(
                view.FindControl<StackPanel>("SystemEditorPage")!.GetVisualAncestors().OfType<ScrollViewer>()
            );
            pageScroll.Offset = new Vector(0, double.MaxValue);
            using WriteableBitmap? scrolled = window.CaptureRenderedFrame();
            Assert.Equal(top.Y, review.TranslatePoint(default, window)!.Value.Y, 1);
            if (width == 640)
            {
                Assert.True(pageScroll.Offset.Y > 0);
            }
            SaveColonizationDesign(scrolled!, $"system-scrolled-{theme}-{width}");
            Grid header = view.FindControl<Grid>("SystemSitesHeaders")!;
            ListBox sites = view.FindControl<ListBox>("SystemSites")!;
            Grid row = sites
                .GetVisualDescendants()
                .OfType<Grid>()
                .First(grid =>
                    grid.DataContext is ColonizationSystemSiteRowViewModel && grid.ColumnDefinitions.Count == 5
                );
            Assert.Equal(header.Bounds.Width, row.Bounds.Width, 1);
            for (int i = 0; i < 5; i++)
            {
                Assert.Equal(header.ColumnDefinitions[i].ActualWidth, row.ColumnDefinitions[i].ActualWidth, 1);
            }
            TextBox input = row.Children.OfType<TextBox>().First();
            input.Text = "Hope orbital port";
            Assert.Equal("Hope orbital port", main.Colonization.SystemEditor.Sites[0].Name);
            Assert.True(main.Colonization.SystemEditor.HasLocalChanges);
            Button button = review
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(control => Equals(control.Content, "Review Raven changes"));
            Assert.True(button.IsEffectivelyEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    private static RavenHandler DesignHandler() =>
        new()
        {
            ActiveProjects =
            [
                new()
                {
                    BuildId = "one",
                    BuildName = "Onufriyenko Vision",
                    BuildType = "dodec",
                    SystemName = "Synuefai CX-V c18-6",
                    SystemAddress = 1732784263842,
                    MaximumRequired = 236304,
                    RemainingRequired = 235108,
                    Commodities = new() { ["steel"] = 100000, ["water"] = 135108 },
                },
                new()
                {
                    BuildId = "two",
                    BuildName = "Chomsky Enterprise",
                    BuildType = "ares",
                    SystemName = "Synuefai CX-V c18-6",
                    SystemAddress = 1732784263842,
                    MaximumRequired = 9922,
                    RemainingRequired = 8726,
                    Commodities = new() { ["steel"] = 4000, ["water"] = 4726 },
                },
            ],
            SystemArchitect = "Example Cmdr",
            AdditionalSystems = new()
            {
                ["/api/v2/system/Synuefai%20CX-V%20c18-6"] = new ColonizationSystemRecord
                {
                    Name = "Synuefai CX-V c18-6",
                    SystemAddress = 1732784263842,
                    Architect = "Example Cmdr",
                    Bodies = [],
                    Sites = [],
                },
            },
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
                        Name = "Onufriyenko Vision",
                        BodyNumber = 36,
                        BuildType = "military_settlement_medium",
                        MarketId = 4_315_444_579,
                        Status = ColonizationSystemSiteStatus.Complete,
                    },
                    new()
                    {
                        Id = "plan",
                        Name = "Hope orbital port",
                        BodyNumber = 12,
                        BuildType = "no_truss",
                        Status = ColonizationSystemSiteStatus.Plan,
                    },
                ],
            },
            LinkedCarriers =
            [
                new()
                {
                    MarketId = 42,
                    Name = "N4W-T0Z",
                    DisplayName = "DIRE ECLIPSE",
                },
                new()
                {
                    MarketId = 43,
                    Name = "BBB-222",
                    DisplayName = "Colony supplies",
                },
            ],
        };

    private static void AssertFitsPage(Control control, ColonizationView view)
    {
        Assert.NotNull(control);
        Point left = control.TranslatePoint(default, view)!.Value;
        Assert.InRange(left.X, 0, view.Bounds.Width);
        Assert.True(
            left.X + control.Bounds.Width <= view.Bounds.Width,
            $"{control.Name} extends beyond the workspace."
        );
    }

    private static void SaveColonizationDesign(WriteableBitmap frame, string name)
    {
        string? output = Environment.GetEnvironmentVariable("SRVSURVEY_COLONIZATION_DESIGN_OUTPUT");
        if (output is null)
        {
            return;
        }
        Directory.CreateDirectory(output);
        using FileStream file = File.Create(Path.Combine(output, name + ".png"));
        frame.Save(file, PngBitmapEncoderOptions.Default);
    }

    private sealed class DesignTheme : IDisposable
    {
        private readonly string directory = Path.Combine(
            Path.GetTempPath(),
            "colonization-design-theme-" + Guid.NewGuid()
        );
        private readonly RavenThemeService themes;
        private readonly string original;

        public DesignTheme(string theme)
        {
            themes = new RavenThemeService(
                Application.Current!,
                new ThemePreferenceStore(Path.Combine(directory, "theme.json"))
            );
            original = themes.Current.Key;
            themes.Select(theme);
        }

        public void Dispose()
        {
            themes.Select(original);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
