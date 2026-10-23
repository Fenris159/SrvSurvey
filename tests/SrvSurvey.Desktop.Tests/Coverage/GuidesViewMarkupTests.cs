using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class GuidesViewMarkupTests
{
    /// <summary>Guides exposes tasks through an accordion and retains search and illustrated references.</summary>
    [Fact]
    public void AccordionAndSearchBothOpenCompleteSubjects()
    {
        var document = XDocument.Load(
            Path.Combine(FindRepositoryRoot(), "src", "SrvSurvey.Desktop", "Views", "GuidesView.axaml")
        );
        XElement expander = Assert.Single(document.Descendants(), element => element.Name.LocalName == "Expander");
        Assert.Equal("{Binding IsExpanded, Mode=TwoWay}", expander.Attribute("IsExpanded")?.Value);
        Assert.Contains(
            expander.Descendants(),
            element =>
                element.Name.LocalName == "Button" && element.Attribute("Command")?.Value == "{Binding OpenCommand}"
        );
        Assert.Contains(
            document.Descendants(),
            element =>
                element.Name.LocalName == "TextBox"
                && element.Attribute("Text")?.Value?.Contains("Guides.SearchText", StringComparison.Ordinal) == true
        );
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "SelectableTextBlock");
        Assert.Contains(
            document.Descendants(),
            element => element.Attribute("ItemsSource")?.Value == "{Binding Guides.SelectedTopic.Illustrations}"
        );
        Assert.Contains(
            document.Descendants(),
            element => element.Attribute("ItemsSource")?.Value == "{Binding Guides.SelectedTopic.Steps}"
        );
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Attribute("ItemsSource")?.Value == "{Binding Guides.SelectedCategory.Sections}"
        );
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

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
