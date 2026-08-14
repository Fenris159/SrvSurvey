using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class GuardianViewMarkupTests
{
    [Fact]
    public void DistanceOriginActionsLeaveTheAutocompleteFullWidth()
    {
        var document = LoadGuardianView();
        var layout = FindNamedElement(document, "GuardianOriginAndCatalog");
        var header = FindNamedElement(document, "GuardianOriginHeader");
        var actions = FindNamedElement(document, "GuardianOriginActions");
        var ramTahOptions = FindNamedElement(document, "GuardianRamTahOptions");
        var entry = document.Descendants().Single(element =>
            element.Name.LocalName == "SystemNameEntry"
            && element.Attribute("Text")?.Value ==
                "{Binding Guardian.OriginSystemName, Mode=TwoWay}");
        var buttons = header.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .ToArray();

        Assert.Equal("*,*", layout.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("Auto,*", header.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("Right", actions.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("Horizontal", actions.Attribute("Orientation")?.Value);
        Assert.Equal(2, buttons.Length);
        Assert.Equal("Stretch", entry.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("Vertical", ramTahOptions.Attribute("Orientation")?.Value);
        Assert.Equal(
            2,
            ramTahOptions.Elements().Count(element =>
                element.Name.LocalName == "CheckBox"));
        Assert.DoesNotContain(
            entry.Parent!.Descendants(),
            element => element.Name.LocalName == "Grid"
                && element.Descendants().Contains(entry)
                && element.Descendants().Any(candidate =>
                    candidate.Name.LocalName == "Button"));
    }

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
        Assert.Equal("True", map.Attribute("AllowViewportInteraction")?.Value);
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
    public void SurveyMapProvidesBottomZoomBarForInteractiveViewport()
    {
        var document = LoadGuardianView();
        var map = FindNamedElement(document, "GuardianSurveyMap");
        var mapGrid = map.Parent
            ?? throw new InvalidDataException("Survey map viewport grid is missing.");
        var slider = mapGrid.Descendants().Single(element =>
            element.Name.LocalName == "Slider");

        Assert.Equal("640,Auto", mapGrid.Attribute("RowDefinitions")?.Value);
        Assert.Equal("1", slider.Attribute("Minimum")?.Value);
        Assert.Equal("10", slider.Attribute("Maximum")?.Value);
        Assert.Contains(
            "ElementName=GuardianSurveyMap",
            slider.Attribute("Value")?.Value,
            StringComparison.Ordinal);
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
    public void ExternalLegendUsesOneCardWithoutInventedStatusKeys()
    {
        var document = LoadGuardianView();
        var legend = FindNamedElement(document, "GuardianSurveyMapLegend");
        var mapLegend = legend.Descendants().Single(element =>
            element.Name.LocalName == "GuardianSiteMapControl");
        var labels = legend.Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Select(element => element.Attribute("Text")?.Value)
            .OfType<string>()
            .ToArray();

        Assert.Equal("True", mapLegend.Attribute("IsLegendOnly")?.Value);
        Assert.Equal(["Map legend"], labels);
        Assert.DoesNotContain(legend.Descendants(), element =>
            element.Name.LocalName is "Border" or "Grid");
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
