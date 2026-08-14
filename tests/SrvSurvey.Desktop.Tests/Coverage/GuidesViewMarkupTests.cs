using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class GuidesViewMarkupTests
{
    [Fact]
    public void WorkflowStepsUseALegibleFullSizeChevron()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SrvSurvey.Desktop",
            "Views",
            "GuidesView.axaml"));
        var chevron = document.Descendants().Single(element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value == "❯");
        var circle = chevron.Parent
            ?? throw new InvalidDataException("Guide step chevron circle is missing.");

        Assert.Equal("17", chevron.Attribute("FontSize")?.Value);
        Assert.Equal("22", circle.Attribute("Width")?.Value);
        Assert.Equal("22", circle.Attribute("Height")?.Value);
        Assert.Equal("11", circle.Attribute("CornerRadius")?.Value);
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
