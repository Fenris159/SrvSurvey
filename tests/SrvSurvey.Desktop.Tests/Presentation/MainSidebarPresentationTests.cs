using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class MainSidebarPresentationTests
{
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
            Assert.True(toggle.Focus());
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Capture(window, themeKey, "collapsed");
            Assert.False(content.IsVisible);
            Assert.True(toggle.IsEffectivelyVisible);
            Assert.True(toggle.IsFocused);
            Assert.Single(sidebar.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible);
            Assert.True(sidebar.Bounds.Width < 50);
            Assert.True(workspace.Bounds.Width > expandedWorkspaceWidth + 150);
            Assert.Equal(windowSize, window.Bounds.Size);
            Assert.Equal("Expand sidebar", viewModel.SidebarToggleLabel);
            Assert.Same(selection, viewModel.SelectedNavigation);
            Assert.Same(chapter, viewModel.Guides.SelectedCategory);

            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Capture(window, themeKey, "restored");
            Assert.True(content.IsVisible);
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
