using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class MainWindowRedesignMarkupTests
{
    [Fact]
    public void ShellUsesCorrectedBrandGroupsHelpAndUpdateNotification()
    {
        var mainWindow = LoadDesktopFile("MainWindow.axaml");
        var values = Values(mainWindow);

        Assert.Contains("SrvSurvey-XP", values);
        Assert.Contains("CMDR'S COMPANION", values);
        Assert.Contains(
            "/Assets/logo-remastered-linux-windows-split.png",
            values);
        Assert.Contains("Survey", values);
        Assert.Contains("Navigation", values);
        Assert.Contains("Activities", values);
        Assert.Contains("Guardian Science Corps", values);
        Assert.Contains("OpenDiscord_Click", values);
        Assert.Contains("OpenCategoryOverlaySettings_Click", values);
        Assert.Contains("{StaticResource window_multiple_regular}", values);
        Assert.DoesNotContain(values, value => value.Contains(
            "chevron",
            StringComparison.OrdinalIgnoreCase));

        var notification = mainWindow.Descendants().Single(element =>
            element.Name.LocalName == "Border"
            && element.Attribute("IsVisible")?.Value
                == "{Binding ReleaseUpdates.ShouldShowUpdateNotification}");
        Assert.Equal("Bottom", notification.Attribute("VerticalAlignment")?.Value);
        Assert.Contains(
            notification.Descendants(),
            element => element.Attribute("Command")?.Value
                == "{Binding ReleaseUpdates.OpenUpdateDiagnosticsCommand}");
    }

    [Fact]
    public void DiagnosticReplayUsesOnlyAnAlteredApplicationBorder()
    {
        var mainWindow = LoadDesktopFile("MainWindow.axaml");
        var diagnosticBorder = mainWindow.Descendants().Single(element =>
            element.Name.LocalName == "Border"
            && element.Attribute("Classes")?.Value == "diagnostic-shell");

        Assert.Equal(
            "{Binding IsDiagnosticReplay}",
            diagnosticBorder.Attribute("Classes.active")?.Value);
        Assert.DoesNotContain(
            mainWindow.Descendants(),
            element => element.Attributes().Any(attribute =>
                attribute.Value.Contains("watermark", StringComparison.OrdinalIgnoreCase)
                || attribute.Value.Contains("replay bar", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void NavigationGroupHeadingsMatchDestinationTypography()
    {
        var styles = LoadDesktopFile("Styles", "RavenStyles.axaml");
        var headingStyle = styles.Descendants().Single(element =>
            element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value
                == "Button.nav-group-heading");

        AssertStyleSetter(headingStyle, "FontSize", "14");
        AssertStyleSetter(headingStyle, "FontWeight", "SemiBold");
    }

    [Fact]
    public void NavigationGroupsAnimateHeightAndOpacityUnlessMotionIsReduced()
    {
        var mainWindow = LoadDesktopFile("MainWindow.axaml");
        var containers = mainWindow.Descendants()
            .Where(element => element.Name.LocalName == "Border"
                && element.Attribute("Classes")?.Value
                    == "nav-group-container")
            .ToArray();

        Assert.Equal(3, containers.Length);
        Assert.All(containers, container => Assert.Equal(
            "{Binding DesktopBehavior.ReduceMotion}",
            container.Attribute("Classes.reduced-motion")?.Value));
        Assert.DoesNotContain(
            containers.SelectMany(container => container.Descendants()),
            element => element.Attribute("IsVisible")?.Value is not null);

        var styles = LoadDesktopFile("Styles", "RavenStyles.axaml");
        var containerStyle = styles.Descendants().Single(element =>
            element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value
                == "Border.nav-group-container");
        var transitionedProperties = containerStyle.Descendants()
            .Where(element => element.Name.LocalName == "DoubleTransition")
            .Select(element => element.Attribute("Property")?.Value)
            .ToArray();
        Assert.Contains("MaxHeight", transitionedProperties);
        Assert.Contains("Opacity", transitionedProperties);

        var reducedMotionStyle = styles.Descendants().Single(element =>
            element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value
                == "Border.nav-group-container.reduced-motion");
        AssertStyleSetter(reducedMotionStyle, "Transitions", "{x:Null}");
    }

    [Fact]
    public void OverviewPreservesCommanderAndMultipleCommanderContracts()
    {
        var overview = LoadView("OverviewView.axaml");
        var values = Values(overview);

        Assert.Contains("{Binding CommanderName}", values);
        Assert.Contains("{Binding FrontierId, StringFormat=Frontier ID: {0}}", values);
        Assert.Contains("{Binding SessionState}", values);
        Assert.Contains("{Binding GameDescription}", values);
        Assert.Contains("{Binding OverviewSystemName}", values);
        Assert.Contains("{Binding OverviewSystemAddress}", values);
        Assert.Contains("{Binding BodyName}", values);
        Assert.Contains("{Binding GameMode}", values);
        Assert.Contains("{Binding CommanderInstances.RefreshCommand}", values);
        Assert.Contains("{Binding CommanderInstances.CurrentCommander}", values);
        Assert.Contains("{Binding CommanderInstances.SwitchWindowCommand}", values);
        Assert.Contains("{Binding CommanderInstances.Commanders}", values);
        Assert.Contains("{Binding CommanderInstances.SelectedCommander}", values);
        Assert.Contains("{Binding CommanderInstances.LaunchCommand}", values);
        Assert.Contains("{Binding CommanderInstances.StatusMessage}", values);
        Assert.DoesNotContain("COMMANDER CONSOLE", values);
        Assert.DoesNotContain("LIVE", values);
    }

    [Fact]
    public void SphereLimitKeepsItsFullLookupAndLimitContract()
    {
        var search = LoadView("SearchView.axaml");
        var values = Values(search);

        Assert.Contains("{Binding Search.LimitSummary}", values);
        Assert.Contains("{Binding Search.CurrentSystemResult}", values);
        Assert.Contains("{Binding Search.CurrentSystemName}", values);
        Assert.Contains("{Binding Search.DistanceToCenter}", values);
        Assert.Contains("{Binding Search.Query, Mode=TwoWay}", values);
        Assert.Contains("{Binding Search.SearchResults}", values);
        Assert.Contains("{Binding Search.SelectedCenterSystem, Mode=TwoWay}", values);
        Assert.Contains("{Binding Search.Radius, Mode=TwoWay}", values);
        Assert.Contains("{Binding Search.EnableCommand}", values);
        Assert.Contains("{Binding Search.DisableCommand}", values);
        Assert.Contains("{Binding Search.CenterSystemName}", values);
        Assert.Contains("{Binding Search.CenterPosition}", values);
        Assert.Contains(values, value => value.Contains(
            "suggestions come from EDSM with an Ardent fallback",
            StringComparison.Ordinal));
    }

    [Fact]
    public void CopyLinksStayClickableWithoutUnderlines()
    {
        var styles = LoadDesktopFile("Styles", "RavenStyles.axaml");
        var styleMap = styles.Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .ToDictionary(
                element => element.Attribute("Selector")?.Value ?? string.Empty,
                StringComparer.Ordinal);

        AssertNoUnderlineSetter(styleMap["Button.link TextBlock"]);
        AssertNoUnderlineSetter(styleMap["TextBlock.system-copy-link"]);

        var copyTargets = Directory
            .EnumerateFiles(
                Path.Combine(FindRepositoryRoot(), "src", "SrvSurvey.Desktop"),
                "*.axaml",
                SearchOption.AllDirectories)
            .Select(XDocument.Load)
            .SelectMany(document => document.Descendants())
            .Count(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "ClipboardCopyBehavior.Text"));
        Assert.True(copyTargets >= 41, $"Expected at least 41 copy targets, found {copyTargets}.");
    }

    [Fact]
    public void GlobalScrollbarsKeepAReservedVisibleGutter()
    {
        var styles = LoadDesktopFile("Styles", "RavenStyles.axaml");
        var styleMap = styles.Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .ToDictionary(
                element => element.Attribute("Selector")?.Value ?? string.Empty,
                StringComparer.Ordinal);

        AssertStyleSetter(styleMap["ScrollViewer"], "AllowAutoHide", "False");
        AssertStyleSetter(styleMap["ScrollBar"], "AllowAutoHide", "False");
        AssertStyleSetter(styleMap["ScrollBar:vertical"], "Width", "16");
        AssertStyleSetter(styleMap["ScrollBar:horizontal"], "Height", "16");
        AssertStyleSetter(
            styleMap["ScrollBar:vertical /template/ Thumb"],
            "MinHeight",
            "32");
        AssertStyleSetter(
            styleMap["ScrollBar:horizontal /template/ Thumb"],
            "MinWidth",
            "32");
    }

    [Fact]
    public void OverlayShortcutColorDoesNotDependOnRemovedListBoxItemAncestor()
    {
        var styles = LoadDesktopFile("Styles", "RavenStyles.axaml");
        var overlayShortcutStyle = styles.Descendants().Single(element =>
            element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value
                == "Button.nav-overlay-settings");
        var foreground = overlayShortcutStyle.Elements().Single(element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "Foreground");

        Assert.Equal(
            "{DynamicResource RavenMutedTextBrush}",
            foreground.Attribute("Value")?.Value);
        Assert.DoesNotContain(
            "$parent[ListBoxItem]",
            foreground.Attribute("Value")?.Value,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalScrollbarTemplateUsesRoundedTrackWithoutLineButtons()
    {
        var styles = LoadDesktopFile("Styles", "RavenStyles.axaml");
        var styleMap = styles.Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .ToDictionary(
                element => element.Attribute("Selector")?.Value ?? string.Empty,
                StringComparer.Ordinal);

        foreach (var selector in new[] { "ScrollBar:vertical", "ScrollBar:horizontal" })
        {
            var style = styleMap[selector];
            Assert.Contains(style.Elements(), element =>
                element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "Template");
            Assert.DoesNotContain(style.Descendants(), element =>
                element.Attribute("Name")?.Value
                    is "PART_LineUpButton" or "PART_LineDownButton");
        }

        Assert.Contains(
            styles.Descendants(),
            element => element.Name.LocalName == "Border"
                && element.Attribute("Classes")?.Value == "scrollbar-thumb"
                && element.Attribute("CornerRadius")?.Value == "999");

        var separatorStyle = styleMap[
            "ScrollViewer[IsExpanded=true] /template/ Panel#PART_ScrollBarsSeparator"];
        AssertStyleSetter(separatorStyle, "Background", "Transparent");
        AssertStyleSetter(separatorStyle, "Opacity", "0");
    }

    [Fact]
    public void ExpandableSectionHeadersUsePillChrome()
    {
        var styles = LoadDesktopFile("Styles", "RavenStyles.axaml");
        var expectedSelectors = new HashSet<string>(StringComparer.Ordinal)
        {
            "Expander.theme-selector /template/ ToggleButton",
            "Expander.profile-section /template/ ToggleButton",
            "Expander.section-pill /template/ ToggleButton",
        };

        var pillToggleStyles = styles.Descendants().Where(element =>
            element.Name.LocalName == "Style"
            && expectedSelectors.Contains(element.Attribute("Selector")?.Value ?? string.Empty))
            .ToArray();

        Assert.Equal(3, pillToggleStyles.Length);
        Assert.All(pillToggleStyles, style =>
            AssertStyleSetter(style, "CornerRadius", "999"));

        var boxelStats = LoadDesktopFile("BoxelStatsWindow.axaml");
        Assert.Contains(boxelStats.Descendants(), element =>
            element.Name.LocalName == "Expander"
            && element.Attribute("Classes")?.Value == "section-pill");
    }

    private static void AssertStyleSetter(
        XElement style,
        string property,
        string value)
    {
        Assert.Contains(style.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == property
            && element.Attribute("Value")?.Value == value);
    }

    private static void AssertNoUnderlineSetter(XElement style)
    {
        Assert.DoesNotContain(style.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "TextDecorations"
            && element.Attribute("Value")?.Value == "Underline");
    }

    private static XDocument LoadView(string fileName) =>
        LoadDesktopFile("Views", fileName);

    private static XDocument LoadDesktopFile(params string[] segments) =>
        XDocument.Load(Path.Combine(
            new[]
            {
                FindRepositoryRoot(),
                "src",
                "SrvSurvey.Desktop",
            }.Concat(segments).ToArray()));

    private static string[] Values(XDocument document) => document.Descendants()
        .SelectMany(element => element.Attributes())
        .Select(attribute => attribute.Value)
        .ToArray();

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

        throw new DirectoryNotFoundException(
            "Could not locate the repository root.");
    }
}
