using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class RouteWindowMarkupTests
{
    private static readonly string[] RoutedFileNames =
    [
        "RouteWindow.axaml",
        "JumpInfoOverlayPresentation.axaml",
    ];

    private static readonly string[] FleetCarrierFooterCommands =
    [
        "{Binding DeleteCommand}",
        "{Binding NewCommand}",
        "{Binding SaveAsCommand}",
        "{Binding ResetCommand}",
        "{Binding DiscardCommand}",
        "{Binding SaveCommand}",
    ];

    private static readonly string[] FleetCarrierHeaderTexts =
    [
        "DONE",
        "SYSTEM NAME",
        "DISTANCE (LY)",
        "REMAINING (LY)",
        "JUMPS LEFT",
        "FUEL LEFT (TONNES)",
        "TRITIUM IN MARKET",
        "FUEL USED (TONNES)",
        "ICY RING",
        "RESTOCK?",
        "RESTOCK AMOUNT",
    ];
    private static readonly string[] FleetCarrierBindings =
    [
        "{Binding CarrierDistance}",
        "{Binding CarrierRemaining}",
        "{Binding JumpsRemaining}",
        "{Binding CarrierFuelRemaining}",
        "{Binding CarrierTritiumInMarket}",
        "{Binding CarrierFuelUsed}",
        "{Binding CarrierIcyRing}",
        "{Binding CarrierRestock}",
        "{Binding CarrierRestockAmount}",
    ];

    [Fact]
    public void RouteRowsAreNotSelectableAndWindowUsesWorkspaceTitle()
    {
        var document = LoadRouteWindow();
        var window = document.Root
            ?? throw new InvalidDataException("RouteWindow.axaml has no root element.");

        Assert.Equal("{Binding WindowTitle}", window.Attribute("Title")?.Value);

        var routeItems = window
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ItemsControl"
                && element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Name"
                    && attribute.Value == "RouteHopItems"));

        Assert.Equal(
            "{Binding Hops}",
            routeItems.Attributes().Single(attribute =>
                attribute.Name.LocalName == "ItemsSource").Value);
        Assert.DoesNotContain(
            window.Descendants(),
            element => element.Name.LocalName == "ListBox"
                && element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "ItemsSource"
                    && attribute.Value == "{Binding Hops}"));
    }

    [Fact]
    public void SidebarReservesScrollbarGutterOutsidePanels()
    {
        var document = LoadRouteWindow();
        var sidebar = FindNamedElement(document, "RouteSidebar");
        var scroller = FindNamedElement(document, "RouteSidebarScroller");
        var panels = FindNamedElement(document, "RouteSidebarPanels");

        Assert.Equal("18,18,6,18", sidebar.Attribute("Padding")?.Value);
        Assert.Equal(
            "Auto",
            scroller.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal("0,0,12,0", panels.Attribute("Margin")?.Value);
    }

    [Fact]
    public void SaveAsValidationUsesABooleanVisibilityBinding()
    {
        var document = LoadRouteWindow();
        var error = document.Descendants()
            .Single(element =>
                element.Attribute("Text")?.Value == "{Binding SaveAsError}");

        Assert.Equal(
            "{Binding HasSaveAsError}",
            error.Attribute("IsVisible")?.Value);
    }

    [Fact]
    public void RouteListReservesScrollbarGutterOutsidePanels()
    {
        var document = LoadRouteWindow();
        var workspace = FindNamedElement(document, "RouteHopWorkspace");
        var header = FindNamedElement(document, "RouteHopHeader");
        var scroller = FindNamedElement(document, "RouteHopScroller");
        var table = FindNamedElement(document, "RouteHopTable");
        var routeItems = FindNamedElement(document, "RouteHopItems");

        Assert.Equal("20,20,6,20", workspace.Attribute("Margin")?.Value);
        Assert.Equal("{Binding !IsFleetCarrierWorkspace}", header.Attribute("IsVisible")?.Value);
        Assert.Equal(
            "Auto",
            scroller.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal(
            "Auto",
            scroller.Attribute("HorizontalScrollBarVisibility")?.Value);
        Assert.Equal("0,0,14,0", table.Attribute("Margin")?.Value);
        Assert.Null(routeItems.Attribute("Margin"));
    }

    [Fact]
    public void FleetCarrierRowsFollowSpanshLogisticsColumnOrder()
    {
        var document = LoadRouteWindow();
        var header = FindNamedElement(document, "FleetCarrierRouteHopHeader");
        var headerTexts = header.Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Select(element => element.Attribute("Text")?.Value)
            .OfType<string>()
            .ToArray();

        Assert.Equal(
            FleetCarrierHeaderTexts,
            headerTexts);
        Assert.Equal(
            "{Binding IsFleetCarrierWorkspace}",
            header.Attribute("IsVisible")?.Value);

        var carrierRow = document.Descendants().Single(element =>
            element.Name.LocalName == "Grid"
            && element.Attribute("IsVisible")?.Value
                == "{Binding IsFleetCarrierHop}");
        var bindings = carrierRow.Descendants()
            .Select(element => element.Attribute("Text")?.Value)
            .Where(value => value?.StartsWith("{Binding Carrier", StringComparison.Ordinal) == true
                || value == "{Binding JumpsRemaining}")
            .ToArray();
        Assert.Equal(
            FleetCarrierBindings,
            bindings);
    }

    [Fact]
    public void RouteRowsExposeStructuredBodyTreeAndSeparateGuidanceIndicators()
    {
        var document = LoadRouteWindow();
        var text = string.Join(
            " ",
            document.Descendants()
                .Select(element => element.Attribute("Text")?.Value)
                .Where(value => value is not null));

        Assert.Contains("BODIES", text, StringComparison.Ordinal);
        Assert.Contains("TYPE", text, StringComparison.Ordinal);
        Assert.Contains("ARRIVAL", text, StringComparison.Ordinal);
        Assert.Contains("SCAN", text, StringComparison.Ordinal);
        Assert.Contains("MAP", text, StringComparison.Ordinal);
        Assert.Contains("BIO", text, StringComparison.Ordinal);
        Assert.Contains("TERRAFORMABLE", text, StringComparison.Ordinal);
        Assert.Contains("REFUEL", text, StringComparison.Ordinal);
        Assert.Contains("NEUTRON", text, StringComparison.Ordinal);
        Assert.Contains(
            document.Descendants(),
            element => element.Name.LocalName == "Image"
                && element.Attribute("Source")?.Value
                    == "avares://SrvSurvey.Desktop/Assets/Routes/refuel-star.png");
        Assert.Contains(
            document.Descendants(),
            element => element.Name.LocalName == "Image"
                && element.Attribute("Source")?.Value
                    == "avares://SrvSurvey.Desktop/Assets/Routes/neutron-star.png");
        Assert.Contains(
            document.Descendants(),
            element => element.Name.LocalName == "Image"
                && element.Attribute("Source")?.Value
                    == "{Binding BodyIconAssetPath, Converter={StaticResource BundledAssetImageConverter}}");
        Assert.Contains("Scan for biological signals", text, StringComparison.Ordinal);
        Assert.Contains(
            document.Descendants(),
            element => element.Name.LocalName == "ItemsControl"
                && element.Attribute("ItemsSource")?.Value == "{Binding BioTargets}");
        Assert.Contains(
            document.Descendants(),
            element => element.Name.LocalName == "CheckBox"
                && element.Attribute("Click")?.Value == "BioTargetCheckBox_Click");
    }

    [Fact]
    public void RouteGuidanceBadgesKeepTheWorkspacePaletteInTheOverlay()
    {
        foreach (var fileName in RoutedFileNames)
        {
            var document = XDocument.Load(Path.Combine(
                FindRepositoryRoot(),
                "src",
                "SrvSurvey.Desktop",
                fileName));
            var guidanceBadges = document.Descendants()
                .Where(element => element.Name.LocalName == "Border"
                    && element.Attribute("Classes")?.Value == "badge"
                    && element.Descendants().Any(descendant =>
                        descendant.Name.LocalName == "TextBlock"
                        && descendant.Attribute("Text")?.Value
                            is "REFUEL" or "NEUTRON"))
                .ToArray();

            Assert.Equal(2, guidanceBadges.Length);
            Assert.All(guidanceBadges, badge => Assert.Equal(
                "{DynamicResource RavenRouteGuidanceBadgeBrush}",
                badge.Attribute("Background")?.Value));
        }
    }

    [Fact]
    public void RouteBodiesWrapBelowTheCompactHopSummary()
    {
        var document = LoadRouteWindow();
        var header = FindNamedElement(document, "RouteHopHeader");
        var bodyItems = document.Descendants().Single(element =>
            element.Name.LocalName == "ItemsControl"
            && element.Attribute("ItemsSource")?.Value == "{Binding BioTargets}");
        var bodySection = bodyItems.Parent
            ?? throw new InvalidDataException("The route body list has no section.");
        var bodyPanel = bodyItems.Descendants().Single(element =>
            element.Name.LocalName == "WrapPanel"
            && element.Attribute("ItemWidth")?.Value == "520");

        Assert.DoesNotContain(
            header.Descendants(),
            element => element.Attribute("Text")?.Value == "BODIES");
        Assert.Equal("1", bodySection.Attribute("Grid.Row")?.Value);
        Assert.Equal("4", bodySection.Attribute("Grid.ColumnSpan")?.Value);
        Assert.Equal("60,12,0,0", bodySection.Attribute("Margin")?.Value);
        Assert.Equal(
            "{Binding HasBioTargets}",
            bodySection.Attribute("IsVisible")?.Value);
        Assert.Contains(
            bodySection.Descendants(),
            element => element.Attribute("Text")?.Value == "BODIES");
        Assert.Equal("Horizontal", bodyPanel.Attribute("Orientation")?.Value);
        Assert.Equal("520", bodyPanel.Attribute("ItemWidth")?.Value);
    }

    [Fact]
    public void RouteLifecycleControlsAndDialogsArePresentInRequestedOrder()
    {
        var document = LoadRouteWindow();
        var buttons = document.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .ToArray();
        var contents = buttons
            .Select(button => button.Attribute("Content")?.Value)
            .Where(content => content is not null)
            .ToArray();

        Assert.Contains("Notes", contents);
        Assert.Contains("Are you sure?", string.Join(
            " ",
            document.Descendants()
                .Select(element => element.Attribute("Text")?.Value)
                .Where(text => text is not null)));
        Assert.Contains(
            "Imports replace the on-screen draft. Nothing is written until Saved.",
            document.Descendants()
                .Select(element => element.Attribute("Text")?.Value));

        var footer = document.Descendants()
            .Single(element => element.Name.LocalName == "Border"
                && element.Attribute("Grid.Row")?.Value == "2");
        Assert.Equal(
            FleetCarrierFooterCommands,
            footer.Descendants()
                .Where(element => element.Name.LocalName == "Button")
                .Select(element => element.Attribute("Command")?.Value)
                .OfType<string>()
                .ToArray());

        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Attribute("Text")?.Value == "Route library");
    }

    private static XDocument LoadRouteWindow() => XDocument.Load(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "SrvSurvey.Desktop",
        "RouteWindow.axaml"));

    private static XElement FindNamedElement(
        XDocument document,
        string name) => document
        .Descendants()
        .Single(element => element.Attributes().Any(attribute =>
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
