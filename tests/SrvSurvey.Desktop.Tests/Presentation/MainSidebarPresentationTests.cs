using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using SkiaSharp;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Presentation;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class MainSidebarPresentationTests
{
    private static readonly string[] AdditionalIconNames =
    [
        "profile",
        "group-survey",
        "group-navigation",
        "group-activities",
    ];

    [AvaloniaFact]
    public void FixedDestinationsRemainOutsideTheInitiallyCollapsedAccordionScroller()
    {
        using MainWindowViewModel viewModel = MainWindowViewModelTestBuilder.Create(null, _ => { });
        var window = new MainWindow(viewModel) { Height = 600 };
        try
        {
            window.Show();
            ItemsControl overview = Assert.IsType<ItemsControl>(
                window.FindControl<ItemsControl>("OverviewNavigationShortcuts")
            );
            ScrollViewer scroller = Assert.IsType<ScrollViewer>(
                window.FindControl<ScrollViewer>("NavigationAccordionScroller")
            );
            ItemsControl utilities = Assert.IsType<ItemsControl>(
                window.FindControl<ItemsControl>("UtilityNavigationShortcuts")
            );
            using WriteableBitmap? frame = window.CaptureRenderedFrame();

            Assert.False(viewModel.IsSurveyNavigationExpanded);
            Assert.False(viewModel.IsNavigationNavigationExpanded);
            Assert.False(viewModel.IsActivitiesNavigationExpanded);
            Assert.DoesNotContain(scroller, overview.GetVisualAncestors());
            Assert.DoesNotContain(scroller, utilities.GetVisualAncestors());
            Assert.True(BottomOf(overview, window) <= TopOf(scroller, window));
            Assert.True(BottomOf(scroller, window) <= TopOf(utilities, window));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("monochrome-dark", 100)]
    [InlineData("blue-light", 125)]
    public void ToggleReclaimsWorkspaceWithoutResizingOrLosingSelection(string themeKey, int scale)
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-sidebar-{Guid.NewGuid():N}");
        using MainWindowViewModel viewModel = MainWindowViewModelTestBuilder.Create(null, _ => { });
        var theme = new RavenThemeService(
            Assert.IsType<Application>(Application.Current, exactMatch: false),
            new ThemePreferenceStore(Path.Combine(root, "theme.json"))
        );
        string originalTheme = theme.Current.Key;
        theme.Select(themeKey);
        theme.ApplyCurrent();
        viewModel.DesktopBehavior.SelectedApplicationWindowScale = ApplicationWindowScaleCatalog.All.Single(option =>
            option.Percent == scale
        );
        viewModel.SelectedNavigation = viewModel.NavigationItems.Single(item => item.Key == "guides");
        viewModel.Guides.SelectedCategory = viewModel.Guides.Categories.Single(category =>
            category.Key == "surface-mining"
        );
        NavigationItemViewModel selection = viewModel.SelectedNavigation;
        GuideCategoryViewModel chapter = viewModel.Guides.SelectedCategory;
        var window = new MainWindow(viewModel);
        try
        {
            window.Show();
            Capture(window, themeKey, "expanded");
            Border sidebar = Assert.IsType<Border>(window.FindControl<Border>("MainSidebar"));
            Grid content = Assert.IsType<Grid>(window.FindControl<Grid>("SidebarContent"));
            Grid workspace = Assert.IsType<Grid>(window.FindControl<Grid>("MainWorkspace"));
            Button toggle = Assert.IsType<Button>(window.FindControl<Button>("SidebarToggleButton"));
            Size windowSize = window.Bounds.Size;
            double expandedWorkspaceWidth = workspace.Bounds.Width;
            double expandedSidebarWidth = sidebar.Bounds.Width;
            Assert.True(content.IsVisible);
            AssertExpandedNavigationUsesLabelsOnly(sidebar);
            Assert.True(toggle.Focus());
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Capture(window, themeKey, "collapsed");
            Assert.True(content.IsVisible);
            Assert.True(toggle.IsEffectivelyVisible);
            Assert.True(toggle.IsFocused);
            Assert.Equal(
                7,
                sidebar
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .Count(button => button.Classes.Contains("nav-item") && IsHitTarget(window, button))
            );
            Assert.Equal(44, sidebar.Bounds.Width);
            Border header = window.FindControl<Border>("SidebarHeaderBand")!;
            Button commander = window.FindControl<Button>("ActiveCommanderCard")!;
            Assert.True(BottomOf(header, sidebar) + 6 <= TopOf(commander, sidebar));
            Image[] visibleIcons = sidebar
                .GetVisualDescendants()
                .OfType<Image>()
                .Where(icon =>
                    icon.Classes.Contains("nav-icon")
                    && icon.IsEffectivelyVisible
                    && IsHitTarget(window, icon.GetVisualAncestors().OfType<Button>().First())
                )
                .ToArray();
            Assert.Equal(11, visibleIcons.Length);
            Assert.All(
                visibleIcons,
                icon => Assert.InRange(icon.TranslatePoint(new Point(12, 12), sidebar)!.Value.X, 21, 23)
            );
            Assert.True(workspace.Bounds.Width > expandedWorkspaceWidth + 150);
            Assert.Equal(windowSize, window.Bounds.Size);
            Assert.Equal("Expand sidebar", viewModel.SidebarToggleLabel);
            Assert.Same(selection, viewModel.SelectedNavigation);
            Assert.Same(chapter, viewModel.Guides.SelectedCategory);

            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Capture(window, themeKey, "restored");
            Assert.True(content.IsVisible);
            AssertExpandedNavigationUsesLabelsOnly(sidebar);
            Assert.Equal(expandedSidebarWidth, sidebar.Bounds.Width);
            Assert.Equal(expandedWorkspaceWidth, workspace.Bounds.Width);
            Assert.Equal(windowSize, window.Bounds.Size);
            Assert.Equal("Collapse sidebar", viewModel.SidebarToggleLabel);
        }
        finally
        {
            window.Close();
            theme.Select(originalTheme);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [AvaloniaFact]
    public void CompactIconsNavigateEveryWorkspaceAndKeepAccordionOrderAcrossToggles()
    {
        using MainWindowViewModel viewModel = MainWindowViewModelTestBuilder.Create(null, _ => { });
        viewModel.DesktopBehavior.ReduceMotion = true;
        viewModel.IsSidebarCollapsed = true;
        var window = new MainWindow(viewModel) { Height = 600 };
        try
        {
            window.Show();
            Layout(window);
            Border sidebar = window.FindControl<Border>("MainSidebar")!;
            Button[] headings = window
                .GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.Classes.Contains("nav-group-heading"))
                .ToArray();
            Assert.Equal(["Survey", "Navigation", "Activities"], headings.Select(button => button.Content));
            double closedNavigationTop = TopOf(headings[1], window);
            int visited = 0;
            VisitDestinations(viewModel.OverviewNavigationItems);
            VisitDestinations(viewModel.UtilityNavigationItems);

            foreach (
                (Button heading, IReadOnlyList<NavigationItemViewModel> items) in new[]
                {
                    (headings[0], viewModel.SurveyNavigationItems),
                    (headings[1], viewModel.NavigationWorkspaceItems),
                    (headings[2], viewModel.ActivityNavigationItems),
                }
            )
            {
                ClickButton(window, heading);
                Layout(window);
                Assert.Single(headings, button => button.Classes.Contains("expanded"));
                if (heading == headings[0])
                {
                    Assert.True(TopOf(headings[1], window) > closedNavigationTop + 100);
                }
                VisitDestinations(items);
            }

            Assert.Equal(19, visited);
            Assert.False(viewModel.IsSurveyNavigationExpanded);
            Assert.False(viewModel.IsNavigationNavigationExpanded);
            Assert.True(viewModel.IsActivitiesNavigationExpanded);
            Button toggle = window.FindControl<Button>("SidebarToggleButton")!;
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(window);
            Assert.True(viewModel.IsActivitiesNavigationExpanded);
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(window);
            Assert.True(viewModel.IsActivitiesNavigationExpanded);
            ClickButton(window, headings[2]);
            window.FindControl<ScrollViewer>("NavigationAccordionScroller")!.ScrollToHome();
            Layout(window);
            Assert.DoesNotContain(headings, button => button.Classes.Contains("expanded"));
            Assert.Equal(closedNavigationTop, TopOf(headings[1], window));
            ClickButton(window, window.FindControl<Button>("ActiveCommanderCard")!);
            Assert.True(viewModel.IsProfileSelected);

            void VisitDestinations(IReadOnlyList<NavigationItemViewModel> items)
            {
                foreach (NavigationItemViewModel item in items)
                {
                    Button button = window
                        .GetVisualDescendants()
                        .OfType<Button>()
                        .Single(candidate =>
                            candidate.Classes.Contains("nav-item") && ReferenceEquals(candidate.CommandParameter, item)
                        );
                    button.BringIntoView();
                    Layout(window);
                    Assert.True(button.IsEffectivelyVisible);
                    Assert.True(button.IsEffectivelyEnabled);
                    Assert.Equal(item.Label, ToolTip.GetTip(button));
                    Image icon = Assert.Single(
                        button.GetVisualDescendants().OfType<Image>(),
                        rectangle => rectangle.Classes.Contains("nav-icon")
                    );
                    Assert.IsType<Bitmap>(icon.Source);
                    Assert.Equal(new Size(24, 24), icon.Bounds.Size);
                    Assert.InRange(icon.TranslatePoint(new Point(12, 12), sidebar)!.Value.X, 21, 23);
                    Assert.InRange(icon.TranslatePoint(default, button)!.Value.X, 0, button.Bounds.Width - 24);
                    Assert.DoesNotContain(
                        button.GetVisualDescendants().OfType<TextBlock>(),
                        label => label.Classes.Contains("nav-label") && label.IsEffectivelyVisible
                    );
                    ClickButton(window, button);
                    Layout(window);
                    Assert.Same(item, viewModel.SelectedNavigation);
                    Assert.True(item.IsSelected);
                    visited++;
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeyboardNavigationSkipsClosedCategoriesAndEntersOpenCategories(bool compact)
    {
        using MainWindowViewModel viewModel = MainWindowViewModelTestBuilder.Create(null, _ => { });
        viewModel.DesktopBehavior.ReduceMotion = true;
        viewModel.IsSidebarCollapsed = compact;
        var window = new MainWindow(viewModel);
        try
        {
            window.Show();
            Layout(window);
            Button[] headings = window
                .GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.Classes.Contains("nav-group-heading"))
                .ToArray();
            Assert.True(headings[0].Focus());
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.True(headings[1].IsFocused);
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Layout(window);
            Assert.True(viewModel.IsNavigationNavigationExpanded);
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Button travel = window
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.CommandParameter is NavigationItemViewModel { Key: "travel" });
            Assert.True(travel.IsFocused);
            window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
            Assert.True(headings[1].IsFocused);
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Layout(window);
            Assert.False(viewModel.IsNavigationNavigationExpanded);
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.True(headings[2].IsFocused);
            Assert.True(viewModel.IsOverviewSelected);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void FractionalApplicationScaleKeepsPngEdgesSmoothWithoutRepeatedPixelSteps()
    {
        Application application = Assert.IsType<Application>(Application.Current, exactMatch: false);
        object? original = application.Resources["NavigationIconImageConverter"];
        using MainWindowViewModel viewModel = MainWindowViewModelTestBuilder.Create(null, _ => { });
        viewModel.IsSidebarCollapsed = true;
        viewModel.DesktopBehavior.SelectedApplicationWindowScale = ApplicationWindowScaleCatalog.All.Single(option =>
            option.Percent == 125
        );
        MainWindow? window = null;
        try
        {
            application.Resources["NavigationIconImageConverter"] = new NavigationIconImageConverter(_ =>
                CreateAlphaRamp()
            );
            window = new MainWindow(viewModel);
            window.Show();
            Button overview = window
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.CommandParameter is NavigationItemViewModel { Key: "overview" });
            overview.Foreground = Brushes.White;
            overview.Background = Brushes.Black;
            Layout(window);
            Control icon = overview
                .GetVisualDescendants()
                .OfType<Control>()
                .Single(control => control.Classes.Contains("nav-icon"));
            Point sample = icon.TranslatePoint(new Point(4, 12), window)!.Value;
            using WriteableBitmap frame = window.CaptureRenderedFrame()!;
            using var stream = new MemoryStream();
            frame.Save(stream, PngBitmapEncoderOptions.Default);
            stream.Position = 0;
            using var rendered = SKBitmap.Decode(stream);
            byte[] brightness = Enumerable
                .Range(0, 20)
                .Select(offset => rendered.GetPixel((int)Math.Round(sample.X) + offset, (int)Math.Round(sample.Y)).Red)
                .ToArray();
            Assert.Equal(20, brightness.Distinct().Count());
            Assert.True(brightness[0] < brightness[^1]);
        }
        finally
        {
            try
            {
                window?.Close();
            }
            finally
            {
                application.Resources["NavigationIconImageConverter"] = original;
            }
        }
    }

    private static MemoryStream CreateAlphaRamp()
    {
        using var bitmap = new SKBitmap(256, 256);
        bitmap.Pixels = Enumerable
            .Range(0, 256 * 256)
            .Select(index => new SKColor(255, 255, 255, (byte)(index % 256)))
            .ToArray();
        using var image = SKImage.FromBitmap(bitmap);
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
        return new MemoryStream(png.ToArray());
    }

    [AvaloniaFact]
    public void EveryWorkspaceAndAccordionHasDistinctTransparentPngArtwork()
    {
        using MainWindowViewModel viewModel = MainWindowViewModelTestBuilder.Create(null, _ => { });
        string[] paths = viewModel
            .NavigationItems.Select(item => item.IconAssetPath)
            .Concat(AdditionalIconNames.Select(name => $"avares://SrvSurvey.Desktop/Assets/Navigation/{name}.png"))
            .ToArray();
        Assert.Equal(23, paths.Length);
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in paths)
        {
            using Stream stream = AssetLoader.Open(new Uri(path));
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            byte[] png = bytes.ToArray();
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
            Assert.True(hashes.Add(Convert.ToHexString(SHA256.HashData(png))), $"Duplicate icon: {path}");
            using var bitmap = SKBitmap.Decode(png);
            Assert.Equal(256, bitmap.Width);
            Assert.Equal(256, bitmap.Height);
            Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
            Assert.Contains(bitmap.Pixels, pixel => pixel.Alpha == 255);
        }
    }

    [AvaloniaFact]
    public void MiningResultsWidthToggleFitsBothWorkspacesAndRestoresAfterManualResize()
    {
        using MainWindowViewModel viewModel = MainWindowViewModelTestBuilder.Create(null, _ => { });
        viewModel.SelectedNavigation = viewModel.NavigationItems.Single(item => item.Key == "mine-map");
        viewModel.MineMap.SelectedTab = 4;
        var window = new MainWindow(viewModel);
        try
        {
            window.Show();
            Layout(window);
            Button expand = Assert.IsType<Button>(window.FindControl<Button>("MiningWidthExpandButton"));
            Button restore = Assert.IsType<Button>(window.FindControl<Button>("MiningWidthRestoreButton"));
            ScrollViewer surfaceResults = window
                .GetVisualDescendants()
                .OfType<ScrollViewer>()
                .Single(scroller => scroller.Name == "SurfaceSearchResultsScroller");
            double defaultWidth = window.Bounds.Width;
            Assert.True(expand.IsEffectivelyVisible);
            Assert.True(surfaceResults.Extent.Width > surfaceResults.Viewport.Width);

            expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(window);
            Assert.True(window.Bounds.Width > defaultWidth);
            Assert.True(surfaceResults.Extent.Width <= surfaceResults.Viewport.Width + 3);
            Assert.True(restore.IsEffectivelyVisible);

            window.Width += 100;
            Layout(window);
            Assert.True(restore.IsEffectivelyVisible);
            restore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(window);
            Assert.InRange(Math.Abs(window.Bounds.Width - defaultWidth), 0, 1);

            viewModel.SelectedNavigation = viewModel.NavigationItems.Single(item => item.Key == "mining");
            viewModel.MiningWorkspace.SelectedTab = 3;
            Layout(window);
            ScrollViewer powerplayResults = window
                .GetVisualDescendants()
                .OfType<ScrollViewer>()
                .Single(scroller => scroller.Name == "ResultsScroller");
            Assert.True(expand.IsEffectivelyVisible);
            expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(window);
            Assert.True(powerplayResults.Extent.Width <= powerplayResults.Viewport.Width + 3);
            Assert.True(restore.IsEffectivelyVisible);
            restore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(window);
            Assert.InRange(Math.Abs(window.Bounds.Width - defaultWidth), 0, 1);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Layout(Window window)
    {
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
    }

    private static void AssertExpandedNavigationUsesLabelsOnly(Border sidebar)
    {
        Assert.DoesNotContain(
            sidebar.GetVisualDescendants().OfType<Image>(),
            icon => icon.Classes.Contains("nav-icon") && icon.IsEffectivelyVisible
        );
        Assert.Equal(
            22,
            sidebar
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Count(label => label.Classes.Contains("nav-label") && label.IsVisible)
        );
    }

    private static bool IsHitTarget(Window window, Button button)
    {
        Point point = button
            .TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!
            .Value;
        var hit = window.InputHitTest(point) as Visual;
        return hit == button || hit?.GetVisualAncestors().Contains(button) == true;
    }

    private static void ClickButton(Window window, Button button)
    {
        button.BringIntoView();
        Layout(window);
        Assert.True(IsHitTarget(window, button), $"Cannot click {button.Content ?? button.CommandParameter}.");
        Point point = button
            .TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!
            .Value;
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
    }

    private static void Capture(Window window, string theme, string state)
    {
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        string? directory = Environment.GetEnvironmentVariable("SRVSURVEY_SHELL_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            using FileStream stream = File.Create(Path.Combine(directory, $"sidebar-{theme}-{state}.png"));
            frame.Save(stream, PngBitmapEncoderOptions.Default);
        }
    }

    private static double TopOf(Control control, Visual relativeTo) =>
        control.TranslatePoint(default, relativeTo)!.Value.Y;

    private static double BottomOf(Control control, Visual relativeTo) =>
        TopOf(control, relativeTo) + control.Bounds.Height;
}
