using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class ThemeWorkspaceMarkupTests
{
    [Fact]
    public void ThemeIsAFixedWorkspaceBetweenSettingsAndGuides()
    {
        var mainWindow = LoadDesktopFile("MainWindow.axaml");
        var viewNames = mainWindow.Descendants()
            .Where(element => element.Name.LocalName.EndsWith("View", StringComparison.Ordinal))
            .Select(element => element.Name.LocalName)
            .ToArray();

        Assert.Contains("ThemeView", viewNames);
        Assert.True(Array.IndexOf(viewNames, "SettingsView")
            < Array.IndexOf(viewNames, "ThemeView"));
        Assert.True(Array.IndexOf(viewNames, "ThemeView")
            < Array.IndexOf(viewNames, "GuidesView"));
    }

    [Fact]
    public void ThemeWorkspaceOwnsTheThreeExistingSections()
    {
        var theme = LoadDesktopFile("Views", "ThemeView.axaml");
        var settings = LoadDesktopFile("Views", "SettingsView.axaml");
        var headers = theme.Descendants()
            .Where(element => element.Name.LocalName == "TabItem")
            .Select(element => element.Attribute("Header")?.Value ?? string.Empty)
            .ToArray();

        Assert.Equal(
            ["Application theme", "In-game overlay appearance", "Overlay Settings"],
            headers);
        Assert.DoesNotContain(
            settings.Root!.DescendantsAndSelf().Attributes(),
            attribute => attribute.Value == "Theme selection");
    }

    [Fact]
    public void OverlayColorGroupsAreSingleOpenAccordionsAndTypographyIsExperimental()
    {
        var theme = LoadDesktopFile("Views", "ThemeView.axaml");
        var categoryExpander = theme.Descendants().Single(element =>
            element.Name.LocalName == "Expander"
            && element.Attribute("Classes")?.Value.Split(' ')
                .Contains("theme-category", StringComparer.Ordinal) == true);

        Assert.Equal(
            "{Binding IsExpanded, Mode=TwoWay}",
            categoryExpander.Attribute("IsExpanded")?.Value);
        Assert.Contains(
            theme.Root!.DescendantsAndSelf().Attributes(),
            attribute => attribute.Value.Contains(
                "Experimental",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplicationThemesUseOneSharedSelectablePreviewTemplate()
    {
        var theme = LoadDesktopFile("Views", "ThemeView.axaml");
        var gallery = theme.Descendants().Single(element =>
            element.Name.LocalName == "ItemsControl"
            && element.Attribute("ItemsSource")?.Value
                == "{Binding ThemeOptions}");
        var values = gallery.Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();

        Assert.Contains("{Binding SelectCommand}", values);
        Assert.Contains("{Binding WindowBrush}", values);
        Assert.Contains("{Binding SurfaceBrush}", values);
        Assert.Contains("{Binding AccentBrush}", values);
        Assert.Contains("{Binding TextBrush}", values);
    }

    [Fact]
    public void ApplicationThemeStylesExposeGrayscaleInteractionAndDepthRoles()
    {
        var styles = LoadDesktopFile("Styles", "RavenStyles.axaml");
        var selectors = styles.Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .Select(element => element.Attribute("Selector")?.Value ?? string.Empty)
            .ToArray();

        Assert.Contains(selectors, selector => selector.Contains(
            "Button:focus-visible",
            StringComparison.Ordinal));
        Assert.Contains(selectors, selector => selector.Contains(
            "Button:disabled",
            StringComparison.Ordinal));
        Assert.Contains("ToolTip", selectors);

        var mainWindow = LoadDesktopFile("MainWindow.axaml");
        Assert.Contains(
            mainWindow.Descendants(),
            element => element.Attribute("BoxShadow")?.Value
                == "{DynamicResource RavenFloatingPanelShadow}");
    }

    [Fact]
    public void LegacyCollapsingHeadersUseRoundedSectionChrome()
    {
        var colonization = LoadDesktopFile("Views", "ColonizationView.axaml");
        var styles = LoadDesktopFile("Styles", "RavenStyles.axaml");
        var expanders = colonization.Descendants()
            .Where(element => element.Name.LocalName == "Expander")
            .ToArray();

        Assert.Equal(2, expanders.Length);
        Assert.All(expanders, expander =>
            Assert.Contains(
                "section-pill",
                expander.Attribute("Classes")?.Value ?? string.Empty,
                StringComparison.Ordinal));
        Assert.Contains(styles.Descendants(), element =>
            element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value
                == "Expander.section-pill /template/ ToggleButton /template/ Border#ToggleButtonBackground"
            && element.Elements().Any(setter =>
                setter.Name.LocalName == "Setter"
                && setter.Attribute("Property")?.Value == "CornerRadius"
                && setter.Attribute("Value")?.Value == "999"));
    }

    private static XDocument LoadDesktopFile(params string[] relativeParts)
    {
        var root = FindRepositoryRoot();
        var parts = new[] { root, "src", "SrvSurvey.Desktop" }
            .Concat(relativeParts)
            .ToArray();
        return XDocument.Load(Path.Combine(parts));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SrvSurvey.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
