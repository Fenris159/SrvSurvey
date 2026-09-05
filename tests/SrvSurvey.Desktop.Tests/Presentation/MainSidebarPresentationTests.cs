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
    [AvaloniaTheory]
    [InlineData("monochrome-dark", 100)]
    [InlineData("blue-light", 125)]
    public void ToggleReclaimsWorkspaceWithoutResizingOrLosingSelection(string themeKey, int scale)
    {
        var root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-sidebar-{Guid.NewGuid():N}");
        using var viewModel = MainWindowViewModelTestBuilder.Create(null, _ => { });
        var theme = new RavenThemeService(Assert.IsType<Application>(Application.Current, exactMatch: false),
            new ThemePreferenceStore(Path.Combine(root, "theme.json")));
        var originalTheme = theme.Current.Key;
        theme.Select(themeKey);
        theme.ApplyCurrent();
        viewModel.DesktopBehavior.SelectedApplicationWindowScale =
            ApplicationWindowScaleCatalog.All.Single(option => option.Percent == scale);
        viewModel.SelectedNavigation = viewModel.NavigationItems.Single(item => item.Key == "guides");
        viewModel.Guides.SelectedCategory = viewModel.Guides.Categories.Single(category => category.Key == "surface-mining");
        var selection = viewModel.SelectedNavigation;
        var chapter = viewModel.Guides.SelectedCategory;
        var window = new MainWindow(viewModel);
        try
        {
            window.Show();
            Capture(window, themeKey, "expanded");
            var sidebar = Assert.IsType<Border>(window.FindControl<Border>("MainSidebar"));
            var content = Assert.IsType<Grid>(window.FindControl<Grid>("SidebarContent"));
            var workspace = Assert.IsType<Grid>(window.FindControl<Grid>("MainWorkspace"));
            var toggle = Assert.IsType<Button>(window.FindControl<Button>("SidebarToggleButton"));
            var windowSize = window.Bounds.Size;
            var expandedWorkspaceWidth = workspace.Bounds.Width;
            var expandedSidebarWidth = sidebar.Bounds.Width;
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

    private static void Capture(Window window, string theme, string state)
    {
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var directory = Environment.GetEnvironmentVariable("SRVSURVEY_SHELL_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            using var stream = File.Create(Path.Combine(directory, $"sidebar-{theme}-{state}.png"));
            frame.Save(stream, PngBitmapEncoderOptions.Default);
        }
    }
}
