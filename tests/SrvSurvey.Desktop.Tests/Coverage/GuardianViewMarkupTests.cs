using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class GuardianViewMarkupTests
{
    [Fact]
    public void SurveyMapKeepsContextCardsBesideMapInRequestedOrder()
    {
        var document = LoadGuardianView();
        var top = FindNamedElement(document, "GuardianSurveyMapTop");
        var sidebar = FindNamedElement(document, "GuardianSurveyMapSidebar");
        var map = top.Descendants().Single(element =>
            element.Name.LocalName == "GuardianSiteMapControl"
            && element.Attribute("IsLegendOnly") is null);
        var cardNames = sidebar.Elements()
            .Select(GetName)
            .OfType<string>()
            .ToArray();

        Assert.Equal("3*,2*", top.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("False", map.Attribute("ShowLegend")?.Value);
        Assert.Equal("True", map.Attribute("ClipToBounds")?.Value);
        Assert.Equal(
            [
                "GuardianSelectedMap",
                "GuardianSurveyMapLegend",
                "GuardianSurveyMapOrientation",
                "GuardianSurveyMapNotes",
            ],
            cardNames);
    }

    [Fact]
    public void OperationalEditorsSpanRowsBelowTheMap()
    {
        var document = LoadGuardianView();
        var top = FindNamedElement(document, "GuardianSurveyMapTop");
        var developerTools = FindNamedElement(
            document,
            "GuardianTemplateDeveloperTools");
        var surveyEditor = FindNamedElement(document, "GuardianSurveyEditor");

        Assert.Same(top.Parent, developerTools.Parent);
        Assert.Same(top.Parent, surveyEditor.Parent);
        Assert.Null(developerTools.Attribute("Grid.Column"));
        Assert.Null(surveyEditor.Attribute("Grid.Column"));
    }

    [Fact]
    public void ExternalLegendContainsTheMapGlyphsAndStatusKey()
    {
        var document = LoadGuardianView();
        var legend = FindNamedElement(document, "GuardianSurveyMapLegend");
        var mapLegend = legend.Descendants().Single(element =>
            element.Name.LocalName == "GuardianSiteMapControl");
        var labels = legend.Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Select(element => element.Attribute("Text")?.Value)
            .Where(text => text is not null)
            .ToArray();

        Assert.Equal("True", mapLegend.Attribute("IsLegendOnly")?.Value);
        Assert.Contains("Unknown / unconfirmed", labels);
        Assert.Contains("Present / scanned", labels);
        Assert.Contains("Absent", labels);
        Assert.Contains("Active obelisk", labels);
    }

    private static XDocument LoadGuardianView() => XDocument.Load(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "SrvSurvey.Desktop",
        "Views",
        "GuardianView.axaml"));

    private static XElement FindNamedElement(
        XDocument document,
        string name) => document.Descendants().Single(element =>
            GetName(element) == name);

    private static string? GetName(XElement element) =>
        element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName == "Name")?.Value;

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
