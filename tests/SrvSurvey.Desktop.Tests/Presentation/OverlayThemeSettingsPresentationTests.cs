using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlayThemeSettingsPresentationTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-theme-presentation-{Guid.NewGuid():N}"
    );

    /// <summary>Color editing stays focused on one group, preserves persistence, and keeps Apply visible while scrolling.</summary>
    [AvaloniaTheory]
    [InlineData(900, 760, "blue-dark")]
    [InlineData(600, 600, "blue-light")]
    public void ColorGroupsAndPinnedActionsWorkAtWorkspaceWidths(int width, int height, string palette)
    {
        var service = new RavenThemeService(
            Application.Current!,
            new ThemePreferenceStore(Path.Combine(temporaryDirectory, "ui.json"))
        );
        string previous = service.Current.Key;
        LegacyOverlayTheme originalOverlay = service.CurrentOverlayTheme;
        var store = new LegacyOverlayThemeStore(Path.Combine(temporaryDirectory, "theme.json"));
        var overlay = new OverlayThemeSettingsViewModel(
            store,
            new OverlayThemeStateStore(Path.Combine(temporaryDirectory, "states.json")),
            service,
            initialTheme: LegacyOverlayThemeStore.CreateDefault()
        );
        using MainWindowViewModel main = CreateMain(overlay);
        var view = new ThemeView { DataContext = main };
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = view,
            Classes = { "app-window" },
        };
        try
        {
            service.Select(palette);
            service.ApplyCurrent();
            window.Show();
            view.FindControl<TabControl>("ThemeSections")!.SelectedIndex = 1;
            Capture(window, $"appearance-{palette}-{width}");
            Button library = view.FindControl<Button>("OverlayThemeLibrary")!;
            Assert.False(library.Flyout!.IsOpen);
            ScrollViewer scroller = view.FindControl<ScrollViewer>("OverlayColorScroller")!;
            Border actions = view.FindControl<Border>("OverlayThemeActionsCard")!;
            double footerY = actions.TranslatePoint(default, window)!.Value.Y;
            Assert.True(footerY + actions.Bounds.Height <= window.Bounds.Height);
            Assert.True(scroller.Bounds.Height > 100);
            Grid[] rows = VisibleRows(view);
            Assert.Equal(overlay.Categories.Single(category => category.IsExpanded).Colors.Count, rows.Length);
            foreach (Grid row in rows)
            {
                TextBox hex = row.GetVisualDescendants().OfType<TextBox>().Single();
                Point origin = hex.TranslatePoint(default, row)!.Value;
                Assert.True(origin.X >= 0);
                Assert.True(origin.X + hex.Bounds.Width <= row.Bounds.Width + 1);
            }
            TextBox editor = rows[0].GetVisualDescendants().OfType<TextBox>().Single();
            editor.Text = "#11223380";
            Assert.Equal("#11223380", overlay.Categories[0].Colors[0].HexValue);
            Button apply = view.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Command == overlay.ApplyCommand);
            Assert.True(apply.IsEffectivelyEnabled);
            editor.Text = "invalid";
            Capture(window, $"invalid-color-{palette}-{width}");
            Assert.False(apply.IsEffectivelyEnabled);
            editor.Text = "#11223380";
            apply.Command!.Execute(null);
            Assert.Equal("#11223380", LegacyOverlayThemeStore.FormatHtmlColor(store.Load().GetColor("header")));
            Assert.False(overlay.IsDirty);

            RadioButton exobiology = view.GetVisualDescendants()
                .OfType<RadioButton>()
                .Single(button => Equals(button.Content, "Exobiology"));
            exobiology.IsChecked = true;
            Capture(window, $"colors-exobiology-{palette}-{width}");
            Assert.Single(overlay.Categories, category => category.IsExpanded);
            Assert.Equal("Exobiology", overlay.Categories.Single(category => category.IsExpanded).Name);
            Assert.Equal(
                overlay.Categories.Single(category => category.IsExpanded).Colors.Count,
                VisibleRows(view).Length
            );
            footerY = actions.TranslatePoint(default, window)!.Value.Y;
            scroller.Offset = new Vector(0, scroller.Extent.Height);
            Capture(window, $"colors-scrolled-{palette}-{width}");
            Assert.True(scroller.Offset.Y > 0);
            Assert.Equal(footerY, actions.TranslatePoint(default, window)!.Value.Y);
            Assert.True(apply.IsEffectivelyVisible);

            library.Flyout.ShowAt(library);
            overlay.StateName = "Survey colors";
            overlay.SaveStateCommand.Execute(null);
            Assert.Contains("Survey colors", overlay.SavedStates);
            library.Flyout.Hide();
            overlay.SelectedSavedState = "Nebula Cyan";
            overlay.LoadStateCommand.Execute(null);
            Capture(window, $"saved-themes-{palette}-{width}");
            Assert.Single(overlay.Categories, category => category.IsExpanded);
            Assert.NotEmpty(VisibleRows(view));

            RadioButton custom = view.GetVisualDescendants()
                .OfType<RadioButton>()
                .Single(button => Equals(button.Content, "Custom"));
            custom.IsChecked = true;
            scroller.Offset = default;
            Capture(window, $"custom-brushes-{palette}-{width}");
            Assert.Equal(5, VisibleRows(view).Length);
            ColorPicker picker = VisibleRows(view)[0].GetVisualDescendants().OfType<ColorPicker>().Single();
            Assert.True(
                picker.TranslatePoint(default, scroller)!.Value.Y + picker.Bounds.Height <= scroller.Bounds.Height
            );
            Assert.Contains("Brush override", Assert.IsType<string>(ToolTip.GetTip(custom)));
            picker.Color = Color.Parse("#C34FA5");
            Assert.Equal("#C34FA5", overlay.Categories[^1].Colors[0].HexValue);
            apply.Command.Execute(null);
            Assert.Equal(Color.Parse("#C34FA5"), store.Load().GetColor("custom.1"));
            window.UpdateLayout();
            TextBlock status = actions
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(text => text.Text == overlay.StatusMessage);
            Assert.InRange(status.Bounds.Height, 1, 48);
            Assert.Equal(overlay.StatusMessage, ToolTip.GetTip(status));
            scroller.Offset = new Vector(0, scroller.Extent.Height);
            Capture(window, $"custom-brushes-scrolled-{palette}-{width}");
            ColorPicker lastPicker = VisibleRows(view)[^1].GetVisualDescendants().OfType<ColorPicker>().Single();
            Assert.True(
                lastPicker.TranslatePoint(default, scroller)!.Value.Y + lastPicker.Bounds.Height
                    <= scroller.Bounds.Height
            );
            Assert.True(apply.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
            service.ApplyOverlayTheme(originalOverlay);
            service.Select(previous);
            service.ApplyCurrent();
        }
    }

    /// <summary>Global setup shows one section at a time and both shortcut editors still share the master saved preferences.</summary>
    [AvaloniaTheory]
    [InlineData(900)]
    [InlineData(600)]
    public void SetupSectionsPreserveSettingsAndMasterShortcuts(int width)
    {
        using MainWindowViewModel main = CreateMain();
        var view = new ThemeView { DataContext = main };
        var window = new Window
        {
            Width = width,
            Height = 760,
            Content = view,
            Classes = { "app-window" },
        };
        try
        {
            window.Show();
            view.FindControl<TabControl>("ThemeSections")!.SelectedIndex = 2;
            OverlaySettingsView settings = view.FindControl<OverlaySettingsView>("GlobalOverlaySettings")!;
            TabControl sections = settings.FindControl<TabControl>("GlobalOverlaySections")!;
            string[] cards =
            [
                "GlobalOverlayBehaviorCard",
                "OverlayVisibilityCard",
                "OverlayInteractionShortcutsCard",
                "PassiveNotificationCard",
                "OpenVrOverlayCard",
                "StreamOverlayCard",
            ];
            for (int index = 0; index < cards.Length; index++)
            {
                sections.SelectedIndex = index;
                Capture(window, $"setup-{index}-{width}");
                if (index == 1)
                {
                    Assert.Equal(
                        main.OverlayPanelVisibility.ForCategory(OverlaySettingsCategory.Global),
                        settings
                            .FindControl<ItemsControl>("PanelVisibilityItems")!
                            .Items.Cast<OverlayPanelVisibilityEntryViewModel>()
                    );
                    Assert.True(settings.FindControl<Border>("PanelVisibilityCard")!.IsEffectivelyVisible);
                }
                for (int candidate = 0; candidate < cards.Length; candidate++)
                {
                    Assert.Equal(
                        candidate == index,
                        TopLevel.GetTopLevel(settings.FindControl<Border>(cards[candidate])!) == window
                            && settings.FindControl<Border>(cards[candidate])!.IsEffectivelyVisible
                    );
                }
            }
            sections.SelectedIndex = 2;
            Capture(window, $"setup-shortcuts-{width}");
            ShortcutCaptureBox[] boxes = settings
                .GetVisualDescendants()
                .OfType<ShortcutCaptureBox>()
                .Where(control => control.IsEffectivelyVisible)
                .ToArray();
            Assert.Equal(2, boxes.Length);
            InputBindingViewModel[] master = [main.OverlayInteractionBinding, main.VrOverlayInteractionBinding];
            for (int index = 0; index < boxes.Length; index++)
            {
                boxes[index].BeginCapture();
                boxes[index].CaptureKeyDown(Key.LeftAlt);
                boxes[index].CaptureKeyDown(Key.LeftCtrl);
                Key key = index == 0 ? Key.F11 : Key.F12;
                boxes[index].CaptureKeyDown(key);
                boxes[index].CaptureKeyUp(key);
                boxes[index].CaptureKeyUp(Key.LeftCtrl);
                boxes[index].CaptureKeyUp(Key.LeftAlt);
                Assert.Equal(boxes[index].Chord, master[index].Chord);
                Assert.Equal(
                    master[index].Chord,
                    new GlobalInputSettingsStore(main.AppDataPaths.UiSettingsPath).Load().Bindings[
                        master[index].Definition.Action
                    ]
                );
                master[index].Chord = "ALT CTRL SHIFT F10";
                Assert.Equal(master[index].Chord, boxes[index].Chord);
            }
            main.InputSettings.ResetBindingsCommand.Execute(null);
            for (int index = 0; index < boxes.Length; index++)
            {
                Assert.Equal(master[index].Definition.DefaultChord, boxes[index].Chord);
                Assert.Equal(
                    boxes[index].Chord,
                    new GlobalInputSettingsStore(main.AppDataPaths.UiSettingsPath).Load().Bindings[
                        master[index].Definition.Action
                    ]
                );
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Switching across ThemeView overlay setup tabs and SettingsView categories never triggers Avalonia layout cycles.</summary>
    [AvaloniaFact]
    public void OverlaySetupAndSettingsScrollViewersDoNotTriggerLayoutCycles()
    {
        var service = new RavenThemeService(
            Application.Current!,
            new ThemePreferenceStore(Path.Combine(temporaryDirectory, "ui.json"))
        );
        string previousTheme = service.Current.Key;
        using MainWindowViewModel main = CreateMain();
        var window = new MainWindow(main) { Width = 1174, Height = 816 };
        ILogSink? previous = Logger.Sink;
        var log = new LayoutCycleLogSink();
        Logger.Sink = log;
        try
        {
            service.Select("monochrome-dark");
            service.ApplyCurrent();
            window.Show();
            main.SelectedNavigation = main.NavigationItems.Single(item => item.Key == "theme");
            window.UpdateLayout();

            ThemeView themeView = Assert.Single(window.GetVisualDescendants().OfType<ThemeView>());
            TabControl themeSections = themeView.FindControl<TabControl>("ThemeSections")!;
            themeSections.SelectedIndex = 2;
            window.UpdateLayout();

            OverlaySettingsView settings = themeView.FindControl<OverlaySettingsView>("GlobalOverlaySettings")!;
            TabControl sections = settings.FindControl<TabControl>("GlobalOverlaySections")!;
            for (int index = 0; index < sections.ItemCount; index++)
            {
                sections.SelectedIndex = index;
                foreach (int h in new[] { 580, 600, 640, 700, 760, 816 })
                {
                    window.Height = h;
                    using WriteableBitmap? frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                }
            }

            // Exercise crossing the 540px/556px horizontal-scroll threshold on VR and Settings.
            sections.SelectedIndex = 4;
            foreach (int w in new[] { 860, 820, 780, 860, 1174 })
            {
                window.Width = w;
                window.Height = 600;
                using WriteableBitmap? frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
            }

            main.SelectedNavigation = main.NavigationItems.Single(item => item.Key == "settings");
            foreach (SettingsCategoryViewModel category in main.SettingsWorkspace.Categories)
            {
                main.SettingsWorkspace.SelectCategory(category.Key);
                foreach (int h in new[] { 600, 760 })
                {
                    window.Width = 1174;
                    window.Height = h;
                    using WriteableBitmap? frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                }
                window.Width = window.MinWidth;
                window.Height = window.MinHeight;
                using WriteableBitmap? minimum = window.CaptureRenderedFrame();
                Assert.NotNull(minimum);
            }

            Assert.True(log.Messages.Count == 0, string.Join(Environment.NewLine, log.Messages));
        }
        finally
        {
            window.Close();
            Logger.Sink = previous;
            service.Select(previousTheme);
            service.ApplyCurrent();
        }
    }

    /// <summary>ResponsiveScrollViewer switches cleanly between wide wrapping and narrow horizontal scrolling without cycles.</summary>
    [AvaloniaFact]
    public void ResponsiveScrollViewerAdaptsAcrossMinimumContentWidthWithoutCycles()
    {
        var content = new StackPanel
        {
            MinWidth = 540,
            Children =
            {
                new TextBlock
                {
                    Text = string.Join(' ', Enumerable.Repeat("Wrapping diagnostic text for viewport width test.", 24)),
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };
        var scroller = new ResponsiveScrollViewer { MinimumContentWidth = 540, Content = content };
        var host = new Border
        {
            Width = 700,
            Height = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = scroller,
        };
        var window = new Window
        {
            Width = 800,
            Height = 300,
            Content = host,
        };
        ILogSink? previous = Logger.Sink;
        var log = new LayoutCycleLogSink();
        Logger.Sink = log;
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.Equal(
                Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                scroller.HorizontalScrollBarVisibility
            );
            Assert.True(double.IsNaN(content.Width));
            Assert.True(scroller.Extent.Width <= scroller.Viewport.Width + 1);

            host.Width = 420;
            window.UpdateLayout();
            Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, scroller.HorizontalScrollBarVisibility);
            Assert.Equal(540, content.Width);
            Assert.True(scroller.Extent.Width >= 540);
            Assert.True(
                scroller.Extent.Width > scroller.Viewport.Width,
                $"Expected horizontal overflow at 420px: extent={scroller.Extent}, viewport={scroller.Viewport}, bounds={scroller.Bounds}."
            );

            // VerticalScrollBarVisibility=Disabled uses zero reserve so exact minWidth fits without horizontal scrolling.
            scroller.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
            host.Width = 540;
            window.UpdateLayout();
            Assert.Equal(
                Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                scroller.HorizontalScrollBarVisibility
            );
            Assert.True(double.IsNaN(content.Width));

            // Infinite available width and zero MinimumContentWidth leave settings untouched.
            scroller.Measure(Size.Infinity);
            scroller.MinimumContentWidth = 0;
            scroller.Content = "plain text";
            scroller.Measure(new Size(400, 200));

            Assert.True(log.Messages.Count == 0, string.Join(Environment.NewLine, log.Messages));
        }
        finally
        {
            window.Close();
            Logger.Sink = previous;
        }
    }

    private sealed class LayoutCycleLogSink : ILogSink
    {
        public List<string> Messages { get; } = [];

        public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning && area == "Layout";

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
        {
            if (IsEnabled(level, area))
            {
                Messages.Add(messageTemplate);
            }
        }

        public void Log(
            LogEventLevel level,
            string area,
            object? source,
            string messageTemplate,
            params object?[] propertyValues
        )
        {
            if (IsEnabled(level, area))
            {
                Messages.Add(messageTemplate + " " + string.Join(" ", propertyValues));
            }
        }
    }

    /// <summary>Builds an isolated main-window model with the overlay settings instance used by the presentation test.</summary>
    private MainWindowViewModel CreateMain(OverlayThemeSettingsViewModel? overlay = null) =>
        MainWindowViewModelTestBuilder.Create(
            null,
            builder =>
            {
                builder.WithAppDataPaths(
                    new AppDataPaths(
                        Path.Combine(temporaryDirectory, "config"),
                        Path.Combine(temporaryDirectory, "data"),
                        Path.Combine(temporaryDirectory, "cache"),
                        []
                    )
                );
                if (overlay is not null)
                {
                    builder.WithOverlayThemeSettings(overlay);
                }
            }
        );

    /// <summary>Returns color editors whose own and ancestor visibility permit them to appear in the current category.</summary>
    private static Grid[] VisibleRows(ThemeView view) =>
        view.GetVisualDescendants()
            .OfType<Grid>()
            .Where(grid => grid.Classes.Contains("overlay-theme-color-row") && grid.IsEffectivelyVisible)
            .ToArray();

    /// <summary>Captures an overlay-theme settings frame and optionally saves it for visual inspection.</summary>
    private static void Capture(Window window, string name)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        string? output = Environment.GetEnvironmentVariable("SRVSURVEY_OVERLAY_ORGANIZATION_PREVIEWS");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            using var preview = new RenderTargetBitmap(
                new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height)
            );
            preview.Render(window);
            preview.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
