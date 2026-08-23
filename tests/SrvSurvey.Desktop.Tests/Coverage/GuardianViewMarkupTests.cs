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
        var originSection = layout.Elements().Single(element =>
            element.Descendants().Contains(entry));
        Assert.DoesNotContain(
            entry.Ancestors().TakeWhile(ancestor => ancestor != layout),
            element => element.Name.LocalName == "Grid"
                && (element == originSection
                    || originSection.Descendants().Contains(element))
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
            "{Binding Guardian.SurveyEditor.SelectedPointName, Mode=TwoWay}",
            map.Attribute("SelectedPointName")?.Value);
        Assert.Equal(
            [
                "GuardianSelectedMap",
                "GuardianSelectedMapPointEditor",
                "GuardianSurveyMapLegend",
                "GuardianSurveyMapOrientation",
                "GuardianSurveyMapNotes",
            ],
            cardNames);
    }

    [Fact]
    public void SurveyEditorExposesRepairAndPrecisionAuthoringFields()
    {
        var document = LoadGuardianView();
        var editor = FindNamedElement(document, "GuardianSurveyEditor");
        var siteType = FindNamedElement(document, "GuardianSurveySiteType");
        var latitude = FindNamedElement(document, "GuardianSurveyLatitude");
        var longitude = FindNamedElement(document, "GuardianSurveyLongitude");
        var activeObelisks = FindNamedElement(
            document,
            "GuardianActiveObeliskEditor");
        var rawPrecision = FindNamedElement(
            document,
            "GuardianRawPointPrecisionEditor");

        Assert.Contains(siteType, editor.Descendants());
        Assert.Equal(
            "{Binding Guardian.SurveyEditor.SiteTypeOptions}",
            siteType.Attribute("ItemsSource")?.Value);
        Assert.Equal(
            "{Binding Guardian.SurveyEditor.SurfaceLatitude, Mode=TwoWay}",
            latitude.Attribute("Value")?.Value);
        Assert.Equal(
            "{Binding Guardian.SurveyEditor.SurfaceLongitude, Mode=TwoWay}",
            longitude.Attribute("Value")?.Value);
        Assert.Contains(activeObelisks, editor.Descendants());
        Assert.Equal(
            "{Binding IsRaw}",
            rawPrecision.Attribute("IsVisible")?.Value);
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

    [Fact]
    public void SitesTableKeepsHeaderFixedAndColumnsAlignedWhileRowsScroll()
    {
        var document = LoadGuardianView();
        var scroller = FindNamedElement(document, "GuardianSitesTableScroller");
        var header = FindNamedElement(document, "GuardianSitesTableHeader");
        var rows = scroller.Descendants().Single(element =>
            element.Name.LocalName == "ListBox"
            && element.Attribute("ItemsSource")?.Value
                == "{Binding Guardian.Rows}");
        var rowGrid = rows.Descendants().Single(element =>
            element.Name.LocalName == "DataTemplate")
            .Elements()
            .Single(element => element.Name.LocalName == "Grid");

        Assert.Equal("430", scroller.Attribute("Height")?.Value);
        Assert.Equal(
            "Auto",
            scroller.Attribute("HorizontalScrollBarVisibility")?.Value);
        Assert.Equal(
            "Disabled",
            scroller.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal(
            "Disabled",
            rows.Attributes().Single(attribute =>
                attribute.Name.LocalName
                    == "ScrollViewer.HorizontalScrollBarVisibility").Value);
        Assert.Equal(
            "Auto",
            rows.Attributes().Single(attribute =>
                attribute.Name.LocalName
                    == "ScrollViewer.VerticalScrollBarVisibility").Value);
        Assert.Equal("370", rows.Attribute("Height")?.Value);
        Assert.Same(header.Parent, rows.Parent);
        Assert.Equal("2", rows.Attribute("Grid.Row")?.Value);
        Assert.Equal(
            header.Attribute("ColumnDefinitions")?.Value,
            rowGrid.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal(
            header.Attribute("ColumnSpacing")?.Value,
            rowGrid.Attribute("ColumnSpacing")?.Value);
        Assert.Contains(
            "table-rows",
            (rows.Attribute("Classes")?.Value ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void SitesTableSortHeadersMatchRouteManagerPresentation()
    {
        var document = LoadGuardianView();
        var expectedParameters = new[]
        {
            "Id",
            "System",
            "Body",
            "Distance",
            "Arrival",
            "Visited",
            "Type",
            "Index",
            "Images",
            "Survey",
            "RamTah",
            "Notes",
        };
        var sortHeaders = document.Descendants()
            .Where(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value
                    == "{Binding Guardian.SortSitesCommand}")
            .ToArray();

        Assert.Equal(expectedParameters.Length, sortHeaders.Length);
        Assert.Equal(
            expectedParameters,
            sortHeaders.Select(header =>
                header.Attribute("CommandParameter")?.Value));
        Assert.All(sortHeaders, header =>
        {
            Assert.Contains(
                "link",
                (header.Attribute("Classes")?.Value ?? string.Empty)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries));
            Assert.Equal("0", header.Attribute("Padding")?.Value);
            Assert.Equal(
                "Left",
                header.Attribute("HorizontalContentAlignment")?.Value);

            var content = Assert.Single(header.Elements(), element =>
                element.Name.LocalName == "StackPanel");
            Assert.Equal("Horizontal", content.Attribute("Orientation")?.Value);
            var textBlocks = content.Elements()
                .Where(element => element.Name.LocalName == "TextBlock")
                .ToArray();
            Assert.Equal(2, textBlocks.Length);
            var label = textBlocks[0];
            Assert.Contains(
                "table-heading",
                (label.Attribute("Classes")?.Value ?? string.Empty)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries));
            Assert.Equal(
                $"{{Binding Guardian.{header.Attribute("CommandParameter")?.Value}SortIndicator}}",
                textBlocks[1].Attribute("Text")?.Value);
        });
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
