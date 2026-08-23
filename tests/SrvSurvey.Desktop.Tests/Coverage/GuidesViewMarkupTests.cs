using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class GuidesViewMarkupTests
{
    [Fact]
    public void CatalogueAndReadingFlowAvoidNumbersBulletsAndChevrons()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SrvSurvey.Desktop",
            "Views",
            "GuidesView.axaml"));
        var values = document.Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();
        var categoryTemplate = document.Descendants().Single(element =>
            element.Name.LocalName == "ListBox.ItemTemplate");

        Assert.DoesNotContain("{Binding Number}", values);
        Assert.DoesNotContain(values, value => value.Contains(
            "CATEGORY {0}",
            StringComparison.Ordinal));
        Assert.DoesNotContain("❯", values);
        Assert.DoesNotContain("•", values);
        Assert.Contains("guide-step", values);
        Assert.Contains("guide-detail", values);
        Assert.Single(categoryTemplate.Descendants(), element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value == "{Binding Title}");
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

        throw new DirectoryNotFoundException(
            "Could not locate the repository root.");
    }
}
