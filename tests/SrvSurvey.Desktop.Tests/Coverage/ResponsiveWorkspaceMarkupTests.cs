using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class ResponsiveWorkspaceMarkupTests
{
    [Fact]
    public void ColonizationWorkspaceFitsTheAvailableWidthWithoutHorizontalScrolling()
    {
        XDocument view = LoadDesktopFile("Views", "ColonizationView.axaml");
        XElement rootScroller = view.Descendants().First(element => element.Name.LocalName == "ScrollViewer");

        Assert.Equal("Disabled", rootScroller.Attribute("HorizontalScrollBarVisibility")?.Value);
        Assert.Contains(
            view.Descendants(),
            element =>
                element.Attribute("Classes")?.Value?.Contains("colonization-project-card", StringComparison.Ordinal)
                == true
        );
        Assert.DoesNotContain(view.Descendants().Attributes("SharedSizeGroup"), _ => true);
    }

    [Fact]
    public void BoxelListsShareOneAccordionViewportAndRowStyle()
    {
        XDocument view = LoadDesktopFile("BoxelStatsWindow.axaml");
        XElement recent = view.Descendants()
            .Single(element =>
                element
                    .Attributes()
                    .Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "RecentEntriesScroller")
            );

        Assert.Equal("Auto", recent.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal("{Binding IsRecentSectionExpanded}", recent.Attribute("IsVisible")?.Value);
        Assert.Null(recent.Attribute("MaxHeight"));

        XElement browser = view.Descendants()
            .Single(element =>
                element
                    .Attributes()
                    .Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "BrowserRowsList")
            );
        Assert.Equal("{Binding IsBrowserSectionExpanded}", browser.Parent?.Attribute("IsVisible")?.Value);

        Assert.True(view.Descendants().Count(element => element.Attribute("BorderThickness")?.Value == "0,0,0,1") >= 2);
        Assert.Contains(
            view.Descendants().Where(element => element.Name.LocalName == "Style"),
            style => style.Attribute("Selector")?.Value == "Button.mass-code.selected:pointerover"
        );
        XElement selectedMassCodeStyle = view.Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .Single(style => style.Attribute("Selector")?.Value == "Button.mass-code.selected");
        Assert.DoesNotContain(
            selectedMassCodeStyle.Elements().Where(element => element.Name.LocalName == "Setter"),
            setter => setter.Attribute("Property")?.Value is "Background" or "Foreground"
        );
    }

    [Fact]
    public void ColonizationPrimaryActionWrapsInsideItsColumn()
    {
        XDocument view = LoadDesktopFile("Views", "ColonizationView.axaml");
        XElement button = view.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Single(element => element.Attribute("Command")?.Value == "{Binding TogglePrimaryCommand}");
        XElement label = Assert.Single(button.Elements(), element => element.Name.LocalName == "TextBlock");

        Assert.Equal("{Binding PrimaryActionLabel}", label.Attribute("Text")?.Value);
        Assert.Equal("Wrap", label.Attribute("TextWrapping")?.Value);
    }

    private static XDocument LoadDesktopFile(params string[] relativeParts)
    {
        string root = FindRepositoryRoot();
        return XDocument.Load(Path.Combine(new[] { root, "src", "SrvSurvey.Desktop" }.Concat(relativeParts).ToArray()));
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
