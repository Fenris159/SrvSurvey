using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Settlements;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlaySettingsDesignPresentationTests
{
    [AvaloniaTheory]
    [InlineData(920, "monochrome-dark")]
    [InlineData(720, "monochrome-dark")]
    [InlineData(920, "blue-light")]
    [InlineData(720, "blue-light")]
    [InlineData(920, "blue-dark")]
    [InlineData(920, "orange-dark")]
    [InlineData(920, "green-light")]
    [InlineData(920, "green-dark")]
    public void EveryCategoryKeepsItsHeadingAndEditorsClearWhileScrolling(int width, string theme)
    {
        using var fixture = new SettingsFixture(theme);
        var errors = new List<string>();
        foreach (OverlaySettingsCategoryDefinition definition in OverlaySettingsCategoryCatalog.All)
        {
            var window = new OverlayCategorySettingsWindow(definition, fixture.Main)
            {
                Width = width,
                Height = width == 720 ? 520 : 760,
            };
            try
            {
                window.Show();
                using WriteableBitmap? frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                SavePreview(frame, $"{definition.NavigationKey}-{theme}-{width}");
                if (!window.Classes.Contains("app-window"))
                {
                    errors.Add($"{definition.DisplayName}: missing application-window styling.");
                }
                OverlaySettingsView view = Assert.Single(window.GetVisualDescendants().OfType<OverlaySettingsView>());
                TextBlock title = view.FindControl<TextBlock>("OverlaySettingsTitle")!;
                if (Math.Abs(title.FontSize - 32) > 0.1)
                {
                    errors.Add($"{definition.DisplayName}: heading size is {title.FontSize}, expected 32.");
                }
                Point titleTop = title.TranslatePoint(default, window)!.Value;
                ScrollViewer scroll = view.FindControl<ScrollViewer>("CategoryOverlaySections")!;
                ScrollContentPresenter viewport = scroll
                    .GetVisualDescendants()
                    .OfType<ScrollContentPresenter>()
                    .Single(presenter => presenter.GetVisualAncestors().OfType<ScrollViewer>().First() == scroll);
                if (scroll.Viewport.Height > window.ClientSize.Height - titleTop.Y - title.Bounds.Height)
                {
                    errors.Add($"{definition.DisplayName}: category viewport extends below the window.");
                }
                foreach (Control editor in view.GetVisualDescendants().OfType<Control>().Where(IsEditor))
                {
                    if (!editor.IsEffectivelyVisible || editor.Bounds.Width == 0)
                    {
                        continue;
                    }
                    Point left = editor.TranslatePoint(default, viewport)!.Value;
                    if (left.X < 0 || left.X + editor.Bounds.Width > viewport.Bounds.Width - 8)
                    {
                        errors.Add(
                            $"{definition.DisplayName}: {editor.Name ?? editor.GetType().Name} exceeds the content width."
                        );
                    }
                }
                Click(window, view.FindControl<Button>("ShowPanelShortcuts")!);
                using WriteableBitmap? panels = window.CaptureRenderedFrame();
                Assert.NotNull(panels);
                SavePreview(panels, $"{definition.NavigationKey}-panels-{theme}-{width}");
                Point after = title.TranslatePoint(default, window)!.Value;
                if (Math.Abs(after.Y - titleTop.Y) > 1 || after.Y < 0)
                {
                    errors.Add($"{definition.DisplayName}: scrolling hides the settings heading.");
                }
                foreach (
                    ShortcutCaptureBox editor in view.FindControl<ItemsControl>("PanelVisibilityItems")!
                        .GetVisualDescendants()
                        .OfType<ShortcutCaptureBox>()
                )
                {
                    Point point = editor.TranslatePoint(default, viewport)!.Value;
                    if (point.X + editor.Bounds.Width > viewport.Bounds.Width - 8)
                    {
                        errors.Add($"{definition.DisplayName}: shortcut editor runs into the scrollbar.");
                    }
                }
                TextBlock panelsHeading = view.FindControl<TextBlock>("PanelVisibilityHeading")!;
                Point panelsTop = panelsHeading.TranslatePoint(default, viewport)!.Value;
                if (panelsTop.Y < 0 || panelsTop.Y + panelsHeading.Bounds.Height > viewport.Bounds.Height)
                {
                    errors.Add($"{definition.DisplayName}: panel-shortcut navigation does not reveal its heading.");
                }
                if (
                    view.FindControl<Border>("PanelVisibilityCard")!.Bounds.Height >= viewport.Bounds.Height
                    && panelsTop.Y > 32
                )
                {
                    errors.Add($"{definition.DisplayName}: shortcut navigation stops below the start of the section.");
                }
                Button close = window
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .Single(button => Equals(button.Content, "Close"));
                Click(window, close);
                if (window.IsVisible)
                {
                    errors.Add($"{definition.DisplayName}: Close leaves the settings window open.");
                }
            }
            finally
            {
                window.Close();
            }
        }
        Assert.Empty(errors);
    }

    [AvaloniaTheory]
    [InlineData(OverlaySettingsCategory.Exploration)]
    [InlineData(OverlaySettingsCategory.Exobiology)]
    [InlineData(OverlaySettingsCategory.Travel)]
    [InlineData(OverlaySettingsCategory.FleetCarrierRoutes)]
    [InlineData(OverlaySettingsCategory.SquadronCarrierRoutes)]
    [InlineData(OverlaySettingsCategory.Boxel)]
    [InlineData(OverlaySettingsCategory.Mining)]
    [InlineData(OverlaySettingsCategory.MineMap)]
    [InlineData(OverlaySettingsCategory.Guardian)]
    [InlineData(OverlaySettingsCategory.Quests)]
    [InlineData(OverlaySettingsCategory.Colonization)]
    [InlineData(OverlaySettingsCategory.Firegroups)]
    public void CategoryShortcutsShareMasterEditsValidationAndSavedDefaults(OverlaySettingsCategory category)
    {
        using var fixture = new SettingsFixture("monochrome-dark");
        MainWindowViewModel main = fixture.Main;
        var window = new OverlayCategorySettingsWindow(
            OverlaySettingsCategoryCatalog.All.Single(definition => definition.Category == category),
            main
        );
        try
        {
            window.Show();
            using WriteableBitmap? initial = window.CaptureRenderedFrame();
            Assert.NotNull(initial);
            OverlaySettingsView view = Assert.Single(window.GetVisualDescendants().OfType<OverlaySettingsView>());
            Click(window, view.FindControl<Button>("ShowPanelShortcuts")!);
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            ShortcutCaptureBox editor = view.FindControl<ItemsControl>("PanelVisibilityItems")!
                .GetVisualDescendants()
                .OfType<ShortcutCaptureBox>()
                .First();
            OverlayPanelVisibilityEntryViewModel panel = Assert.IsType<OverlayPanelVisibilityEntryViewModel>(
                editor.DataContext
            );
            InputBindingViewModel master = main.InputSettings.Bindings.Single(binding =>
                binding.Definition.Action == panel.Shortcut.Definition.Action
            );
            var store = new GlobalInputSettingsStore(main.AppDataPaths.UiSettingsPath);

            editor.Chord = "ALT SHIFT F12";
            Assert.Equal("ALT SHIFT F12", master.Chord);
            Assert.Equal("ALT SHIFT F12", store.Load().Bindings[master.Definition.Action]);
            master.Chord = "CTRL SHIFT F10";
            Assert.Equal("CTRL SHIFT F10", editor.Chord);
            master.Chord = "CTRL A B";
            Assert.True(master.HasValidationError);
            Assert.Equal("CTRL SHIFT F10", store.Load().Bindings[master.Definition.Action]);
            using WriteableBitmap? validation = window.CaptureRenderedFrame();
            Assert.NotNull(validation);
            Assert.Contains(
                view.GetVisualDescendants().OfType<TextBlock>(),
                text =>
                    text.IsEffectivelyVisible
                    && text.Text == "Enter a keyboard shortcut or controller chord, or leave blank."
            );
            main.InputSettings.ResetBindingsCommand.Execute(null);
            Assert.Equal(master.Definition.DefaultChord, editor.Chord);
            Assert.Equal(master.Definition.DefaultChord, store.Load().Bindings[master.Definition.Action]);
            Assert.False(master.HasValidationError);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(440, "monochrome-dark")]
    [InlineData(680, "monochrome-dark")]
    [InlineData(440, "blue-light")]
    [InlineData(680, "blue-light")]
    [InlineData(680, "blue-dark")]
    [InlineData(680, "orange-dark")]
    [InlineData(680, "green-light")]
    [InlineData(680, "green-dark")]
    public void VisibilityExceptionsKeepVehicleChoicesAndBulkActionsUsableAcrossThemes(int width, string theme)
    {
        using var fixture = new SettingsFixture(theme);
        var store = new OverlayVehicleSettingsStore(fixture.Main.AppDataPaths.UiSettingsPath);
        var exceptions = new OverlayExceptionsViewModel(store, new OverlayWindowRegistry());
        foreach (
            OverlaySettingsCategory category in new[]
            {
                OverlaySettingsCategory.Mining,
                OverlaySettingsCategory.MineMap,
            }
        )
        {
            OverlayExceptionCategoryViewModel model = exceptions.ForCategory(category);
            var window = new OverlayExceptionsWindow
            {
                DataContext = model,
                Width = width,
                Height = 740,
            };
            try
            {
                window.Show();
                using WriteableBitmap? frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                SavePreview(frame, $"exceptions-{category}-{theme}-{width}");
                ScrollViewer scroll = window.FindControl<ScrollViewer>("ExceptionsGroupsScroll")!;
                ScrollContentPresenter viewport = scroll
                    .GetVisualDescendants()
                    .OfType<ScrollContentPresenter>()
                    .Single(presenter => presenter.GetVisualAncestors().OfType<ScrollViewer>().First() == scroll);
                CheckBox[] choices = window.GetVisualDescendants().OfType<CheckBox>().ToArray();
                Assert.Equal(model.Entries.Select(entry => entry.Name), choices.Select(choice => choice.Content));
                foreach (CheckBox choice in choices)
                {
                    Point right = choice.TranslatePoint(new Point(choice.Bounds.Width, 0), viewport)!.Value;
                    Assert.True(right.X <= viewport.Bounds.Width - 8, $"{choice.Content} reaches the scrollbar.");
                }
                Button clear = window
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .Single(button => Equals(button.Content, "Uncheck All"));
                Click(window, clear);
                Assert.All(choices, choice => Assert.False(choice.IsChecked));
                Assert.Empty(store.Load(category)!);
                Button allow = window
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .Single(button => Equals(button.Content, "Check All"));
                Click(window, allow);
                Assert.All(choices, choice => Assert.True(choice.IsChecked));
                Assert.Equal(model.Entries.Select(entry => entry.Definition.Id).Order(), store.Load(category)!.Order());
            }
            finally
            {
                window.Close();
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(720, "monochrome-dark")]
    [InlineData(920, "blue-light")]
    public void ExpandedAuthoringAndRewardControlsStayInsideTheirSettingsPanels(int width, string theme)
    {
        using var fixture = new SettingsFixture(theme);
        HumanSiteTemplate template = HumanSiteTemplateCatalog.LoadEmbedded().Templates[0];
        var time = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        var site = new HumanSiteLiveSnapshot(
            Name: "Atlas Depot",
            LocalizedName: "Atlas Depot",
            MarketId: 1,
            SystemAddress: 42,
            BodyId: 7,
            BodyName: "Synuefai CX-V c18-6 4 a",
            Location: new HumanSiteSurfaceLocation(0, 0),
            Economy: HumanSiteEconomy.Agriculture,
            EconomyToken: "$economy_Agri;",
            EconomyLocalized: "Agriculture",
            FactionName: string.Empty,
            FactionState: null,
            Government: string.Empty,
            GovernmentLocalized: string.Empty,
            Services: [],
            StationType: "OnFootSettlement",
            AvailablePads: HumanSiteLandingPads.From(template),
            SubType: template.SubType,
            Template: template,
            Heading: 0,
            Docking: HumanSiteDockingStatus.None,
            GrantedPad: 0,
            DockingDeniedReason: null,
            HasLanded: false,
            FirstApproached: time,
            LastUpdated: time
        );
        fixture.Main.HumanSite.TemplateAuthor.UpdateContext(site, new HumanSiteMapPoint(0, 0), 0, false);
        fixture.Main.HumanSite.TemplateAuthor.StartCommand.Execute(null);
        Assert.True(fixture.Main.HumanSite.TemplateAuthor.IsAuthoring);
        foreach (
            OverlaySettingsCategory category in new[]
            {
                OverlaySettingsCategory.Quests,
                OverlaySettingsCategory.Exobiology,
                OverlaySettingsCategory.MineMap,
            }
        )
        {
            var window = new OverlayCategorySettingsWindow(
                OverlaySettingsCategoryCatalog.All.Single(definition => definition.Category == category),
                fixture.Main
            )
            {
                Width = width,
                Height = 760,
            };
            try
            {
                window.Show();
                OverlaySettingsView view = Assert.Single(window.GetVisualDescendants().OfType<OverlaySettingsView>());
                if (category == OverlaySettingsCategory.Quests)
                {
                    view.FindControl<Expander>("HumanTemplateAuthoringExpander")!.IsExpanded = true;
                }
                if (category == OverlaySettingsCategory.MineMap)
                {
                    Expander guide = view.GetVisualDescendants()
                        .OfType<Expander>()
                        .Single(expander => Equals(expander.Header, "Calibration guide"));
                    Assert.False(guide.IsExpanded);
                    guide.IsExpanded = true;
                }
                using WriteableBitmap? initial = window.CaptureRenderedFrame();
                Assert.NotNull(initial);
                ScrollViewer scroll = view.FindControl<ScrollViewer>("CategoryOverlaySections")!;
                ScrollContentPresenter viewport = scroll
                    .GetVisualDescendants()
                    .OfType<ScrollContentPresenter>()
                    .Single(presenter => presenter.GetVisualAncestors().OfType<ScrollViewer>().First() == scroll);
                Control target = category switch
                {
                    OverlaySettingsCategory.Quests => view.FindControl<Expander>("HumanTemplateAuthoringExpander")!,
                    OverlaySettingsCategory.Exobiology => view.FindControl<StackPanel>("BiologyRewardPanel")!,
                    _ => view.FindControl<Border>("MiningShortcutsCard")!,
                };
                foreach (
                    Control editor in target
                        .GetVisualDescendants()
                        .OfType<Control>()
                        .Where(control =>
                            IsEditor(control)
                            || control is Button
                            || control is TextBox
                            || control is BiologyRewardBandControl
                        )
                )
                {
                    if (!editor.IsEffectivelyVisible || editor.Bounds.Width == 0)
                    {
                        continue;
                    }
                    Point left = editor.TranslatePoint(default, viewport)!.Value;
                    Assert.True(
                        left.X >= 0 && left.X + editor.Bounds.Width <= viewport.Bounds.Width - 8,
                        $"{category} {editor.GetType().Name} exceeds the settings column."
                    );
                }
                Point offset = target
                    .TranslatePoint(default, view.FindControl<StackPanel>("CategoryOverlayContent")!)!
                    .Value;
                scroll.Offset = new Vector(0, offset.Y);
                using WriteableBitmap? frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                SavePreview(frame, $"advanced-{category}-{theme}-{width}");
            }
            finally
            {
                window.Close();
            }
        }
    }

    private static void Click(Window window, Button button)
    {
        Point center = button
            .TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!
            .Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
    }

    private static bool IsEditor(Control control) =>
        control is CheckBox or RadioButton or NumericUpDown or ComboBox or Slider or ShortcutCaptureBox;

    private static void SavePreview(WriteableBitmap frame, string name)
    {
        string? output = Environment.GetEnvironmentVariable("SRVSURVEY_OVERLAY_SETTINGS_DESIGN_OUTPUT");
        if (output is null)
        {
            return;
        }
        Directory.CreateDirectory(output);
        using FileStream file = File.Create(Path.Combine(output, name + ".png"));
        frame.Save(file, PngBitmapEncoderOptions.Default);
    }

    private sealed class SettingsFixture : IDisposable
    {
        private readonly string directory = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-overlay-design-" + Guid.NewGuid()
        );
        private readonly RavenThemeService themes;
        private readonly string originalTheme;

        public SettingsFixture(string theme)
        {
            Main = MainWindowViewModelTestBuilder.Create(
                null,
                builder =>
                    builder.WithAppDataPaths(
                        new(
                            Path.Combine(directory, "config"),
                            Path.Combine(directory, "data"),
                            Path.Combine(directory, "cache"),
                            []
                        )
                    )
            );
            Application application = Assert.IsAssignableFrom<Application>(Application.Current);
            themes = new RavenThemeService(
                application,
                new ThemePreferenceStore(Path.Combine(directory, "theme.json"))
            );
            Color fill = Assert.IsAssignableFrom<ISolidColorBrush>(application.Resources["RavenWindowBrush"]).Color;
            originalTheme = RavenThemeCatalog.All.Single(palette => Color.Parse(palette.WindowColor) == fill).Key;
            themes.Select(theme == "blue-dark" ? "monochrome-dark" : "blue-dark");
            themes.Select(theme);
        }

        public MainWindowViewModel Main { get; }

        public void Dispose()
        {
            Main.Dispose();
            themes.Select(originalTheme);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
