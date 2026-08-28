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
        var zoom = top.Descendants().Single(element =>
            element.Name.LocalName == "Slider"
            && element.Attribute("Value")?.Value
                == "{Binding ViewportZoom, ElementName=GuardianSurveyMap, Mode=TwoWay}");
        var selectedMap = FindNamedElement(document, "GuardianSelectedMap");
        var selectedPoint = FindNamedElement(
            document,
            "GuardianSelectedMapPointEditor");
        var startMapDraft = document.Descendants().Single(element =>
            element.Name.LocalName == "Button"
            && element.Attribute("Content")?.Value == "Start map draft");
        var editCurrentMap = document.Descendants().Single(element =>
            element.Name.LocalName == "Button"
            && element.Attribute("Content")?.Value == "Edit Current Map");
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
            "{Binding Guardian.SelectedMapPointName}",
            map.Attribute("HighlightedPointName")?.Value);
        Assert.Equal(
            "{Binding Guardian.SelectedMapCommanderPosition}",
            map.Attribute("CommanderMapPosition")?.Value);
        Assert.Equal(
            "{Binding Guardian.SelectedMapCommanderPosition}",
            map.Attribute("Proximity")?.Value);
        Assert.Equal(
            "{Binding Guardian.ActiveMapRelativeHeading}",
            map.Attribute("CommanderHeading")?.Value);
        Assert.Equal(
            "False",
            map.Attribute("RotateMapWithCommander")?.Value);
        Assert.Equal(
            "{Binding Guardian.SelectedMapTargetPointName}",
            map.Attribute("TargetPointName")?.Value);
        Assert.Equal("15", zoom.Attribute("Maximum")?.Value);
        Assert.Contains(
            top.Descendants(),
            element => element.Attribute("Text")?.Value.Contains(
                "Gradient wedges mark active obelisks",
                StringComparison.Ordinal) == true);
        Assert.Equal(
            "{Binding Guardian.SurveyEditor.IsMapSummaryVisible}",
            selectedMap.Attribute("IsVisible")?.Value);
        Assert.Equal(
            "{Binding Guardian.SurveyEditor.HasSelectedMapMarker}",
            selectedPoint.Attribute("IsVisible")?.Value);
        Assert.Null(selectedPoint.Attribute("IsEnabled"));
        Assert.Contains(startMapDraft, selectedMap.Descendants());
        Assert.Contains(editCurrentMap, selectedMap.Descendants());
        Assert.Equal(startMapDraft.Parent, editCurrentMap.Parent);
        Assert.DoesNotContain(startMapDraft, selectedPoint.Descendants());
        var selectedContent = selectedPoint.Descendants().Single(element =>
            element.Name.LocalName == "ContentControl"
            && element.Attribute("Content")?.Value
                == "{Binding Guardian.SurveyEditor.SelectedPoint}");
        Assert.Equal(
            "{Binding Guardian.SurveyEditor.CanEditSelectedPoint}",
            selectedContent.Attribute("IsEnabled")?.Value);
        Assert.Equal(
            [
                "GuardianSurveyMapLegend",
                "GuardianSelectedMap",
                "GuardianSurveyPoints",
                "GuardianSelectedMapPointEditor",
                "GuardianSurveyMapNotes",
            ],
            cardNames);
    }

    [Fact]
    public void SharedMapAndPerSiteSurveyEditorsExplainSeparateSaveScopes()
    {
        var document = LoadGuardianView();
        var draftTools = FindNamedElement(document, "GuardianMapDraftTools");
        var catalogDetails = FindNamedElement(
            document,
            "GuardianMapCatalogDetails");
        var surveyEditor = FindNamedElement(document, "GuardianSurveyEditor");

        Assert.Contains(draftTools.Descendants(), element =>
            element.Attribute("Text")?.Value
                == "{Binding Guardian.TemplateAuthoring.DraftDescription}");
        Assert.Contains(draftTools.Descendants(), element =>
            element.Attribute("Text")?.Value
                == "{Binding Guardian.TemplateAuthoring.SaveLocationText}");
        Assert.Contains(draftTools.Descendants(), element =>
            element.Attribute("Content")?.Value == "Save map changes...");
        Assert.DoesNotContain(draftTools.Descendants(), element =>
            element.Attribute("Content")?.Value == "Apply metadata to draft");
        Assert.Contains(catalogDetails, draftTools.Descendants());
        Assert.Contains(catalogDetails.Descendants(), element =>
            element.Attribute("Text")?.Value == "BODY");
        Assert.Contains(catalogDetails.Descendants(), element =>
            element.Attribute("Text")?.Value == "DISTANCE LY");
        Assert.Contains(catalogDetails.Descendants(), element =>
            element.Attribute("Text")?.Value == "ARRIVAL DISTANCE LS");
        Assert.Contains(catalogDetails.Descendants(), element =>
            element.Attribute("Content")?.Value == "Save site details");
        Assert.DoesNotContain(catalogDetails.Descendants(), element =>
            element.Attribute("Text")?.Value is "GALACTIC X" or "GALACTIC Y" or "GALACTIC Z");
        Assert.Contains(surveyEditor.Descendants(), element =>
            element.Attribute("Text")?.Value == "This site survey");
        Assert.Contains(surveyEditor.Descendants(), element =>
            element.Attribute("Text")?.Value?.Contains(
                "belong only to the selected site",
                StringComparison.Ordinal) == true);
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
        var activeObeliskDetails = activeObelisks.Descendants().Single(element =>
            element.Name.LocalName == "ContentControl"
            && element.Attribute("Content")?.Value
                == "{Binding Guardian.SurveyEditor.SelectedActiveObelisk}");
        var rawPrecision = FindNamedElement(
            document,
            "GuardianRawPointPrecisionEditor");
        var rawFields = FindNamedElement(
            document,
            "GuardianRawPointGeometryFields");
        var templatePointEditor = FindNamedElement(
            document,
            "GuardianSelectedTemplatePointEditor");
        var templatePointFields = FindNamedElement(
            document,
            "GuardianSelectedTemplatePointFields");
        var templateIdentityFields = FindNamedElement(
            document,
            "GuardianSelectedTemplateIdentityFields");

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
        Assert.DoesNotContain(
            activeObeliskDetails.Descendants().Attributes(),
            attribute => attribute.Value.Contains(
                "SelectedActiveObelisk.",
                StringComparison.Ordinal));
        Assert.Equal(
            "{Binding IsRaw}",
            rawPrecision.Attribute("IsVisible")?.Value);
        Assert.Equal("StackPanel", rawFields.Name.LocalName);
        Assert.Null(rawFields.Attribute("Orientation"));
        var rawCoordinateInputs = rawFields.Descendants()
            .Where(element => element.Name.LocalName == "NumericUpDown")
            .ToArray();
        Assert.Equal(3, rawCoordinateInputs.Length);
        Assert.All(rawCoordinateInputs, input =>
        {
            Assert.Equal("0.1", input.Attribute("Increment")?.Value);
            Assert.Equal("132", input.Attribute("MinWidth")?.Value);
        });
        Assert.Equal(
            "{Binding Guardian.TemplateAuthoring.HasSelectedPoint}",
            templatePointEditor.Attribute("IsVisible")?.Value);
        Assert.Equal(
            "{Binding Guardian.TemplateAuthoring.IsAuthoring}",
            templatePointFields.Attribute("IsEnabled")?.Value);
        Assert.Equal("StackPanel", templateIdentityFields.Name.LocalName);
        Assert.Null(templateIdentityFields.Attribute("Orientation"));
        var coordinateInputs = templatePointFields.Descendants()
            .Where(element => element.Name.LocalName == "NumericUpDown")
            .ToArray();
        Assert.Equal(3, coordinateInputs.Length);
        Assert.All(coordinateInputs, input =>
        {
            Assert.Equal("0.1", input.Attribute("Increment")?.Value);
            Assert.Equal("132", input.Attribute("MinWidth")?.Value);
        });
        Assert.Contains(
            templatePointFields.Descendants(),
            element => element.Name.LocalName == "TextBox"
                && element.Attribute("Text")?.Value
                    == "{Binding Guardian.TemplateAuthoring.PointName, Mode=TwoWay}");
        Assert.Contains(
            templatePointFields.Descendants(),
            element => element.Name.LocalName == "ComboBox"
                && element.Attribute("SelectedItem")?.Value
                    == "{Binding Guardian.TemplateAuthoring.PointType, Mode=TwoWay}");
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
        var zoomBar = slider.Parent
            ?? throw new InvalidDataException("Survey map zoom bar is missing.");
        var orientation = zoomBar.Elements().Single(element =>
            element.Name.LocalName == "StackPanel"
            && element.Descendants().Any(candidate =>
                candidate.Name.LocalName == "TextBlock"
                && candidate.Attribute("Text")?.Value == "Orientation"));
        var orientationHelp = orientation.Descendants().Single(element =>
            element.Name.LocalName == "Button");
        var orientationIcon = orientationHelp.Descendants().Single(element =>
            element.Name.LocalName == "PathIcon");

        Assert.Equal("640,Auto", mapGrid.Attribute("RowDefinitions")?.Value);
        Assert.Equal(
            "Auto,*,Auto,Auto",
            zoomBar.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("1", slider.Attribute("Minimum")?.Value);
        Assert.Equal("15", slider.Attribute("Maximum")?.Value);
        Assert.Contains(
            "ElementName=GuardianSurveyMap",
            slider.Attribute("Value")?.Value,
            StringComparison.Ordinal);
        Assert.Equal("3", orientation.Attribute("Grid.Column")?.Value);
        Assert.Equal(
            "The legacy aerial image is fixed to the Guardian site template's surveyed origin and scale. Site, tower, marker, and commander geometry uses that same alignment.",
            orientationHelp.Attribute("ToolTip.Tip")?.Value);
        Assert.Null(orientationHelp.Attribute("Click"));
        Assert.Equal(
            "{StaticResource question_circle_regular}",
            orientationIcon.Attribute("Data")?.Value);
        Assert.DoesNotContain(document.Descendants(), element =>
            GetName(element) == "GuardianSurveyMapOrientation");
    }

    [Fact]
    public void OperationalEditorsSpanRowsBelowTheMap()
    {
        var document = LoadGuardianView();
        var top = FindNamedElement(document, "GuardianSurveyMapTop");
        var mapDraftTools = FindNamedElement(
            document,
            "GuardianMapDraftTools");
        var surveyEditor = FindNamedElement(document, "GuardianSurveyEditor");

        Assert.Same(top.Parent, mapDraftTools.Parent);
        Assert.Same(top.Parent, surveyEditor.Parent);
        Assert.Null(mapDraftTools.Attribute("Grid.Column"));
        Assert.Equal(
            "{Binding Guardian.TemplateAuthoring.IsAuthoring}",
            mapDraftTools.Attribute("IsVisible")?.Value);
        Assert.Null(surveyEditor.Attribute("Grid.Column"));
    }

    [Fact]
    public void SurveyPointsSitDirectlyBelowSelectedMapInSidebar()
    {
        var document = LoadGuardianView();
        var sidebar = FindNamedElement(document, "GuardianSurveyMapSidebar");
        var selectedMap = FindNamedElement(document, "GuardianSelectedMap");
        var surveyPoints = FindNamedElement(document, "GuardianSurveyPoints");
        var sidebarChildren = sidebar.Elements().ToArray();
        var selectedMapIndex = Array.IndexOf(sidebarChildren, selectedMap);

        Assert.Same(sidebar, surveyPoints.Parent);
        Assert.Equal(selectedMapIndex + 1, Array.IndexOf(
            sidebarChildren,
            surveyPoints));
        Assert.Equal(
            "{Binding Guardian.SurveyEditor.IsMapSummaryVisible}",
            surveyPoints.Attribute("IsVisible")?.Value);

        var list = surveyPoints.Descendants().Single(element =>
            element.Name.LocalName == "ListBox");
        Assert.Equal(
            "{Binding Guardian.SurveyEditor.Points}",
            list.Attribute("ItemsSource")?.Value);
        Assert.Equal(
            "{Binding Guardian.SurveyEditor.SelectedPoint, Mode=TwoWay}",
            list.Attribute("SelectedItem")?.Value);

        var editor = FindNamedElement(document, "GuardianSurveyEditor");
        Assert.DoesNotContain(editor.Descendants(), element =>
            element.Attribute("Text")?.Value == "SURVEY POINTS");
    }

    [Fact]
    public void ExternalLegendUsesOneRenderedCardWithRoomForAllStates()
    {
        var document = LoadGuardianView();
        var legend = FindNamedElement(document, "GuardianSurveyMapLegend");
        var expander = FindNamedElement(
            document,
            "GuardianSurveyMapLegendExpander");
        var headerStyle = document.Descendants().Single(element =>
            element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value
                == "Expander.guardian-map-legend /template/ ToggleButton /template/ Border#ToggleButtonBackground");
        var monochromeHeaderStyle = document.Descendants().Single(element =>
            element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value
                == "Expander.guardian-map-legend.monochrome /template/ ToggleButton /template/ Border#ToggleButtonBackground");
        var expanderContentStyle = document.Descendants().Single(element =>
            element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value
                == "Expander.guardian-map-legend /template/ Border#ExpanderContent");
        var expandedContentStyle = document.Descendants().Single(element =>
            element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value
                == "Expander.guardian-map-legend:expanded /template/ Border#ExpanderContent");
        var monochromeContentStyle = document.Descendants().Single(element =>
            element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value
                == "Expander.guardian-map-legend.monochrome /template/ Border#ExpanderContent");
        static Dictionary<string, string?> GetSetters(XElement style) =>
            style.Elements()
                .Where(element => element.Name.LocalName == "Setter")
                .ToDictionary(
                    element => element.Attribute("Property")?.Value
                        ?? throw new InvalidDataException(
                            "Map legend template setter is missing its property."),
                    element => element.Attribute("Value")?.Value);
        var headerSetters = GetSetters(headerStyle);
        var monochromeHeaderSetters = GetSetters(monochromeHeaderStyle);
        var expanderContentSetters = GetSetters(expanderContentStyle);
        var expandedContentSetters = GetSetters(expandedContentStyle);
        var monochromeContentSetters = GetSetters(monochromeContentStyle);
        var mapLegend = legend.Descendants().Single(element =>
            element.Name.LocalName == "GuardianSiteMapControl");
        var labels = legend.Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Select(element => element.Attribute("Text")?.Value)
            .OfType<string>()
            .ToArray();

        Assert.Equal("False", expander.Attribute("IsExpanded")?.Value);
        Assert.Equal(
            "guardian-map-legend",
            expander.Attribute("Classes")?.Value);
        Assert.Equal(
            "{Binding IsMonochromeTheme}",
            expander.Attribute("Classes.monochrome")?.Value);
        Assert.Null(legend.Attribute("Classes"));
        Assert.Equal("0", legend.Attribute("Margin")?.Value);
        Assert.Equal("0", legend.Attribute("Padding")?.Value);
        Assert.Equal("1", headerSetters["BorderThickness"]);
        Assert.Equal("12", headerSetters["CornerRadius"]);
        Assert.Equal("none", headerSetters["BoxShadow"]);
        Assert.Equal("0,0,0,10", headerSetters["Margin"]);
        Assert.Equal("#1C1C1C", monochromeHeaderSetters["Background"]);
        Assert.Equal("#33FFFFFF", monochromeHeaderSetters["BorderBrush"]);
        Assert.Equal(
            "OuterBorderEdge",
            expanderContentSetters["BackgroundSizing"]);
        Assert.Equal("1,0,1,1", expanderContentSetters["BorderThickness"]);
        Assert.Equal("0,0,12,12", expanderContentSetters["CornerRadius"]);
        Assert.Equal("none", expanderContentSetters["BoxShadow"]);
        Assert.Equal("0", expanderContentSetters["Margin"]);
        Assert.Equal("10,0,0,0", expanderContentSetters["Padding"]);
        Assert.Equal("290", expanderContentSetters["MinWidth"]);
        Assert.Equal("270", expanderContentSetters["MinHeight"]);
        Assert.Equal("1", expandedContentSetters["BorderThickness"]);
        Assert.Equal("12", expandedContentSetters["CornerRadius"]);
        Assert.Equal("#2B2B2B", monochromeContentSetters["Background"]);
        Assert.Equal("#33FFFFFF", monochromeContentSetters["BorderBrush"]);
        Assert.Same(legend, expander.Parent);
        Assert.Equal("True", mapLegend.Attribute("IsLegendOnly")?.Value);
        Assert.Equal("270", mapLegend.Attribute("Height")?.Value);
        Assert.Equal("20,0,0,0", mapLegend.Attribute("Margin")?.Value);
        Assert.Equal("True", mapLegend.Attribute("ClipToBounds")?.Value);
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
