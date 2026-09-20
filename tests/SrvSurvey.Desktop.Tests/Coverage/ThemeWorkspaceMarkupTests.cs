using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class ThemeWorkspaceMarkupTests
{
    [Fact]
    public void ThemeIsAFixedWorkspaceBetweenSettingsAndGuides()
    {
        XDocument mainWindow = LoadDesktopFile("MainWindow.axaml");
        string[] viewNames = mainWindow
            .Descendants()
            .Where(element => element.Name.LocalName.EndsWith("View", StringComparison.Ordinal))
            .Select(element => element.Name.LocalName)
            .ToArray();

        Assert.Contains("ThemeView", viewNames);
        Assert.True(Array.IndexOf(viewNames, "SettingsView") < Array.IndexOf(viewNames, "ThemeView"));
        Assert.True(Array.IndexOf(viewNames, "ThemeView") < Array.IndexOf(viewNames, "GuidesView"));
    }

    [Fact]
    public void ThemeWorkspaceOwnsTheThreeExistingSections()
    {
        XDocument theme = LoadDesktopFile("Views", "ThemeView.axaml");
        XDocument settings = LoadDesktopFile("Views", "SettingsView.axaml");
        string[] headers = theme
            .Descendants()
            .Where(element => element.Name.LocalName == "TabItem")
            .Select(element => element.Attribute("Header")?.Value ?? string.Empty)
            .ToArray();

        Assert.Equal(["Application theme", "In-game overlay appearance", "Overlay Settings"], headers);
        Assert.DoesNotContain(
            settings.Root!.DescendantsAndSelf().Attributes(),
            attribute => attribute.Value == "Theme selection"
        );
    }

    [Fact]
    public void OverlayColorGroupsAreSingleOpenAccordionsAndPerPanelScalingReplacesTheGlobalTypographyEditor()
    {
        XDocument theme = LoadDesktopFile("Views", "ThemeView.axaml");
        XElement categoryExpander = theme
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Expander"
                && element.Attribute("Classes")?.Value.Split(' ').Contains("theme-category", StringComparer.Ordinal)
                    == true
            );

        Assert.Equal("{Binding IsExpanded, Mode=TwoWay}", categoryExpander.Attribute("IsExpanded")?.Value);
        Assert.DoesNotContain(theme.Descendants(), element => element.Attribute("Text")?.Value == "Typography");
        Assert.DoesNotContain(
            theme.Descendants(),
            element =>
                element
                    .Attributes()
                    .Any(attribute =>
                        attribute.Name.LocalName == "Name" && attribute.Value == "OverlayTypographyEditorList"
                    )
        );
    }

    [Fact]
    public void ApplicationThemesUseOneSharedSelectablePreviewTemplate()
    {
        XDocument theme = LoadDesktopFile("Views", "ThemeView.axaml");
        XElement gallery = theme
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ItemsControl"
                && element.Attribute("ItemsSource")?.Value == "{Binding ThemeOptions}"
            );
        string[] values = gallery
            .Descendants()
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
        XDocument styles = LoadDesktopFile("Styles", "RavenStyles.axaml");
        string[] selectors = styles
            .Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .Select(element => element.Attribute("Selector")?.Value ?? string.Empty)
            .ToArray();

        Assert.Contains(selectors, selector => selector.Contains("Button:focus-visible", StringComparison.Ordinal));
        Assert.Contains(selectors, selector => selector.Contains("Button:disabled", StringComparison.Ordinal));
        Assert.Contains("ToolTip", selectors);

        XDocument mainWindow = LoadDesktopFile("MainWindow.axaml");
        Assert.Contains(
            mainWindow.Descendants(),
            element => element.Attribute("BoxShadow")?.Value == "{DynamicResource RavenFloatingPanelShadow}"
        );
    }

    [Fact]
    public void LegacyCollapsingHeadersUseRoundedSectionChrome()
    {
        XDocument colonization = LoadDesktopFile("Views", "ColonizationView.axaml");
        XDocument styles = LoadDesktopFile("Styles", "RavenStyles.axaml");
        XElement[] expanders = colonization
            .Descendants()
            .Where(element => element.Name.LocalName == "Expander")
            .ToArray();

        Assert.Equal(2, expanders.Length);
        Assert.All(
            expanders,
            expander =>
                Assert.Contains(
                    "section-pill",
                    expander.Attribute("Classes")?.Value ?? string.Empty,
                    StringComparison.Ordinal
                )
        );
        Assert.Contains(
            styles.Descendants(),
            element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value
                    == "Expander.section-pill /template/ ToggleButton /template/ Border#ToggleButtonBackground"
                && element
                    .Elements()
                    .Any(setter =>
                        setter.Name.LocalName == "Setter"
                        && setter.Attribute("Property")?.Value == "CornerRadius"
                        && setter.Attribute("Value")?.Value == "999"
                    )
        );
    }

    private static XDocument LoadDesktopFile(params string[] relativeParts)
    {
        string root = FindRepositoryRoot();
        string[] parts = new[] { root, "src", "SrvSurvey.Desktop" }.Concat(relativeParts).ToArray();
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
