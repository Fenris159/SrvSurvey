using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class DiagnosticsViewMarkupTests
{
    [Fact]
    public void LiveLogUsesAnIndependentNonCaretScrollSurface()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SrvSurvey.Desktop",
            "Views",
            "DiagnosticsView.axaml"));
        var pageScroller = FindNamedElement(
            document,
            "DiagnosticsPageScroller");
        var logScroller = FindNamedElement(
            document,
            "ApplicationLogScroller");
        var logBinding = "{Binding DiagnosticsLog.LogText, Mode=OneWay}";

        Assert.Equal("ScrollViewer", pageScroller.Name.LocalName);
        Assert.Equal("ScrollViewer", logScroller.Name.LocalName);
        Assert.Contains(logScroller.Descendants(), element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value == logBinding);
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "TextBox"
            && element.Attribute("Text")?.Value == logBinding);
    }

    private static XElement FindNamedElement(
        XDocument document,
        string name) => document.Descendants().Single(element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name"
                && attribute.Value == name));

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
