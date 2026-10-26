using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class RouteWindowMarkupTests
{
    private static readonly string[] RoutedFileNames = ["RouteWindow.axaml", "JumpInfoOverlayPresentation.axaml"];

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
        XDocument document = LoadRouteWindow();
        XElement window = document.Root ?? throw new InvalidDataException("RouteWindow.axaml has no root element.");

        Assert.Equal("{Binding WindowTitle}", window.Attribute("Title")?.Value);

        XElement routeItems = window
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ItemsControl"
                && element
                    .Attributes()
                    .Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "RouteHopItems")
            );

        Assert.Equal(
            "{Binding Hops}",
            routeItems.Attributes().Single(attribute => attribute.Name.LocalName == "ItemsSource").Value
        );
        Assert.DoesNotContain(
            window.Descendants(),
            element =>
                element.Name.LocalName == "ListBox"
                && element
                    .Attributes()
                    .Any(attribute => attribute.Name.LocalName == "ItemsSource" && attribute.Value == "{Binding Hops}")
        );
    }

    [Fact]
    public void SidebarReservesScrollbarGutterOutsidePanels()
    {
        XDocument document = LoadRouteWindow();
        XElement sidebar = FindNamedElement(document, "RouteSidebar");
        XElement scroller = FindNamedElement(document, "RouteSidebarScroller");
        XElement panels = FindNamedElement(document, "RouteSidebarPanels");

        Assert.Equal("18,18,6,18", sidebar.Attribute("Padding")?.Value);
        Assert.Equal("Auto", scroller.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal("0,0,12,0", panels.Attribute("Margin")?.Value);
    }

    [Fact]
    public void SaveAsValidationUsesABooleanVisibilityBinding()
    {
        XDocument document = LoadRouteWindow();
        XElement error = document
            .Descendants()
            .Single(element => element.Attribute("Text")?.Value == "{Binding SaveAsError}");

        Assert.Equal("{Binding HasSaveAsError}", error.Attribute("IsVisible")?.Value);
    }

    [Fact]
    public void RouteListReservesScrollbarGutterOutsidePanels()
    {
        XDocument document = LoadRouteWindow();
        XElement workspace = FindNamedElement(document, "RouteHopWorkspace");
        XElement header = FindNamedElement(document, "RouteHopHeader");
        XElement scroller = FindNamedElement(document, "RouteHopScroller");
        XElement table = FindNamedElement(document, "RouteHopTable");
        XElement routeItems = FindNamedElement(document, "RouteHopItems");

        Assert.Equal("20,20,6,20", workspace.Attribute("Margin")?.Value);
        Assert.Equal("{Binding !IsFleetCarrierWorkspace}", header.Attribute("IsVisible")?.Value);
        Assert.Equal("Auto", scroller.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal("Auto", scroller.Attribute("HorizontalScrollBarVisibility")?.Value);
        Assert.Equal("0,0,14,0", table.Attribute("Margin")?.Value);
        Assert.Null(routeItems.Attribute("Margin"));
    }

    [Fact]
    public void EveryRouteLayoutEndsWithTheExistingRedTrashButtonStyle()
    {
        XDocument document = LoadRouteWindow();
        XElement footerButton = document
            .Descendants()
            .Single(element => element.Attribute("Command")?.Value == "{Binding DeleteCommand}");
        XElement footerIcon = footerButton.Elements().Single();
        XElement[] rowButtons = document
            .Descendants()
            .Where(element => element.Attribute("Click")?.Value == "RemoveHop_Click")
            .ToArray();

        Assert.Single(rowButtons);
        foreach (XElement button in rowButtons)
        {
            XElement row = button.Parent!;
            string[] columns = row.Attribute("ColumnDefinitions")!.Value.Split(',');
            Assert.Equal(
                (columns.Length - 1).ToString(global::System.Globalization.CultureInfo.InvariantCulture),
                button.Attribute("Grid.Column")?.Value
            );
            Assert.Equal("44", columns[^1]);
            Assert.Equal(footerButton.Attribute("Classes")?.Value, button.Attribute("Classes")?.Value);
            Assert.Equal(footerButton.Attribute("Width")?.Value, button.Attribute("Width")?.Value);
            Assert.Equal("Delete waypoint", button.Attribute("ToolTip.Tip")?.Value);
            Assert.Equal("Delete waypoint", button.Attribute("AutomationProperties.Name")?.Value);
            Assert.Equal(footerIcon.ToString(), button.Elements().Single().ToString());
        }

        Assert.Equal(
            "48,*,112,2*,44",
            FindNamedElement(document, "RouteHopHeader").Elements().Single().Attribute("ColumnDefinitions")?.Value
        );
        Assert.Equal(
            "48,270,110,125,90,135,145,135,105,100,140,44",
            FindNamedElement(document, "FleetCarrierRouteHopHeader")
                .Elements()
                .Single()
                .Attribute("ColumnDefinitions")
                ?.Value
        );
    }

    [Fact]
    public void FleetCarrierRowsFollowSpanshLogisticsColumnOrder()
    {
        XDocument document = LoadRouteWindow();
        XElement header = FindNamedElement(document, "FleetCarrierRouteHopHeader");
        string[] headerTexts = header
            .Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Select(element => element.Attribute("Text")?.Value)
            .OfType<string>()
            .ToArray();

        Assert.Equal(FleetCarrierHeaderTexts, headerTexts);
        Assert.Equal("{Binding IsFleetCarrierWorkspace}", header.Attribute("IsVisible")?.Value);

        XElement carrierRow = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Grid"
                && element.Attribute("IsVisible")?.Value == "{Binding IsFleetCarrierHop}"
            );
        string?[] bindings = carrierRow
            .Descendants()
            .Select(element => element.Attribute("Text")?.Value)
            .Where(value =>
                value?.StartsWith("{Binding Carrier", StringComparison.Ordinal) == true
                || value == "{Binding JumpsRemaining}"
            )
            .ToArray();
        Assert.Equal(FleetCarrierBindings, bindings);
    }

    [Fact]
    public void RouteRowsExposeStructuredBodyTreeAndSeparateGuidanceIndicators()
    {
        XDocument document = LoadRouteWindow();
        string text = string.Join(
            " ",
            document.Descendants().Select(element => element.Attribute("Text")?.Value).Where(value => value is not null)
        );

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
            element =>
                element.Name.LocalName == "Image"
                && element.Attribute("Source")?.Value == "avares://SrvSurvey.Desktop/Assets/Routes/refuel-star.png"
        );
        Assert.Contains(
            document.Descendants(),
            element =>
                element.Name.LocalName == "Image"
                && element.Attribute("Source")?.Value == "avares://SrvSurvey.Desktop/Assets/Routes/neutron-star.png"
        );
        Assert.Contains(
            document.Descendants(),
            element =>
                element.Name.LocalName == "Image"
                && element.Attribute("Source")?.Value
                    == "{Binding BodyIconAssetPath, Converter={StaticResource BundledAssetImageConverter}}"
        );
        Assert.Contains("Scan for biological signals", text, StringComparison.Ordinal);
        Assert.Contains(
            document.Descendants(),
            element =>
                element.Name.LocalName == "ItemsControl"
                && element.Attribute("ItemsSource")?.Value == "{Binding BioTargets}"
        );
        Assert.Contains(
            document.Descendants(),
            element =>
                element.Name.LocalName == "CheckBox" && element.Attribute("Click")?.Value == "BioTargetCheckBox_Click"
        );
    }

    [Fact]
    public void RouteGuidanceBadgesKeepTheWorkspacePaletteInTheOverlay()
    {
        foreach (string fileName in RoutedFileNames)
        {
            var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "SrvSurvey.Desktop", fileName));
            XElement[] guidanceBadges = document
                .Descendants()
                .Where(element =>
                    element.Name.LocalName == "Border"
                    && element.Attribute("Classes")?.Value == "badge"
                    && element
                        .Descendants()
                        .Any(descendant =>
                            descendant.Name.LocalName == "TextBlock"
                            && descendant.Attribute("Text")?.Value is "REFUEL" or "NEUTRON"
                        )
                )
                .ToArray();

            Assert.Equal(2, guidanceBadges.Length);
            Assert.All(
                guidanceBadges,
                badge =>
                    Assert.Equal("{DynamicResource RavenRouteGuidanceBadgeBrush}", badge.Attribute("Background")?.Value)
            );
        }
    }

    [Fact]
    public void ScoopableStarBadgeMatchesGuidancePillsWithoutAnIcon()
    {
        var document = XDocument.Load(
            Path.Combine(FindRepositoryRoot(), "src", "SrvSurvey.Desktop", "JumpInfoOverlayPresentation.axaml")
        );
        XElement label = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TextBlock" && element.Attribute("Text")?.Value == "SCOOPABLE"
            );
        XElement badge = label.Parent ?? throw new InvalidDataException("The scoopable label has no badge parent.");
        XElement iconScaleHost =
            badge.Parent ?? throw new InvalidDataException("The scoopable badge has no icon scale host.");
        XElement starClass =
            iconScaleHost
                .Parent?.Elements()
                .SingleOrDefault(element => element.Attribute("Text")?.Value == "{Binding JumpInfo.StarClass}")
            ?? throw new InvalidDataException("The scoopable badge has no adjacent star-class label.");

        Assert.Equal("Border", badge.Name.LocalName);
        Assert.Equal("badge", badge.Attribute("Classes")?.Value);
        Assert.Equal("LayoutTransformControl", iconScaleHost.Name.LocalName);
        Assert.Contains("type-icon", iconScaleHost.Attribute("Classes")?.Value.Split(' ') ?? []);
        Assert.Equal("{DynamicResource RavenRouteGuidanceBadgeBrush}", badge.Attribute("Background")?.Value);
        Assert.Equal("{Binding JumpInfo.IsScoopableStarClass}", iconScaleHost.Attribute("IsVisible")?.Value);
        Assert.Equal("18", badge.Attribute("MinHeight")?.Value);
        Assert.Equal("7,2", badge.Attribute("Padding")?.Value);
        Assert.DoesNotContain(
            label.Attribute("Classes")?.Value.Split(' ') ?? [],
            className => className.StartsWith("type-", StringComparison.Ordinal)
        );
        Assert.Equal("9", label.Attribute("FontSize")?.Value);
        Assert.Equal("{DynamicResource RavenWarningBrush}", label.Attribute("Foreground")?.Value);
        Assert.Equal(starClass.Attribute("Foreground")?.Value, label.Attribute("Foreground")?.Value);
        Assert.DoesNotContain(badge.Descendants(), element => element.Name.LocalName == "Image");
        XElement neutronScaleHost = Assert.IsType<XElement>(iconScaleHost.ElementsAfterSelf().FirstOrDefault());
        Assert.Equal("LayoutTransformControl", neutronScaleHost.Name.LocalName);
        Assert.Equal("{Binding JumpInfo.HasNeutronGuidance}", neutronScaleHost.Attribute("IsVisible")?.Value);
        Assert.Same(starClass, Assert.Single(neutronScaleHost.ElementsAfterSelf()));
    }

    [Fact]
    public void RouteBodiesWrapBelowTheCompactHopSummary()
    {
        XDocument document = LoadRouteWindow();
        XElement header = FindNamedElement(document, "RouteHopHeader");
        XElement bodyItems = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ItemsControl"
                && element.Attribute("ItemsSource")?.Value == "{Binding BioTargets}"
            );
        XElement bodySection =
            bodyItems.Parent ?? throw new InvalidDataException("The route body list has no section.");
        XElement bodyLayout =
            bodySection.Parent ?? throw new InvalidDataException("The route body section has no layout.");
        XElement bodyPanel = bodyItems
            .Descendants()
            .Single(element => element.Name.LocalName == "WrapPanel" && element.Attribute("ItemWidth")?.Value == "520");

        Assert.DoesNotContain(header.Descendants(), element => element.Attribute("Text")?.Value == "BODIES");
        Assert.Equal("1", bodyLayout.Attribute("Grid.Row")?.Value);
        Assert.Null(bodyLayout.Attribute("Grid.ColumnSpan"));
        Assert.Equal("{Binding IsStandardHop}", bodyLayout.Attribute("IsVisible")?.Value);
        Assert.Equal("60,12,0,0", bodySection.Attribute("Margin")?.Value);
        Assert.Equal("{Binding HasBioTargets}", bodySection.Attribute("IsVisible")?.Value);
        Assert.Contains(bodySection.Descendants(), element => element.Attribute("Text")?.Value == "BODIES");
        Assert.Equal("Horizontal", bodyPanel.Attribute("Orientation")?.Value);
        Assert.Equal("520", bodyPanel.Attribute("ItemWidth")?.Value);
    }

    [Fact]
    public void RouteLifecycleControlsAndDialogsArePresentInRequestedOrder()
    {
        XDocument document = LoadRouteWindow();
        XElement[] buttons = document.Descendants().Where(element => element.Name.LocalName == "Button").ToArray();
        string?[] contents = buttons
            .Select(button => button.Attribute("Content")?.Value)
            .Where(content => content is not null)
            .ToArray();

        Assert.Contains("Notes", contents);
        Assert.Contains(
            "Are you sure?",
            string.Join(
                " ",
                document
                    .Descendants()
                    .Select(element => element.Attribute("Text")?.Value)
                    .Where(text => text is not null)
            )
        );
        Assert.Contains(
            "Clipboard system names append to the draft, one per line. Spansh and file imports replace it. Use Save As... to keep route changes.",
            document.Descendants().Select(element => element.Attribute("Text")?.Value)
        );

        XElement footer = document
            .Descendants()
            .Single(element => element.Name.LocalName == "Border" && element.Attribute("Grid.Row")?.Value == "2");
        Assert.Equal(
            FleetCarrierFooterCommands,
            footer
                .Descendants()
                .Where(element => element.Name.LocalName == "Button")
                .Select(element => element.Attribute("Command")?.Value)
                .OfType<string>()
                .ToArray()
        );

        Assert.DoesNotContain(document.Descendants(), element => element.Attribute("Text")?.Value == "Route library");
    }

    private static XDocument LoadRouteWindow() =>
        XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "SrvSurvey.Desktop", "RouteWindow.axaml"));

    private static XElement FindNamedElement(XDocument document, string name) =>
        document
            .Descendants()
            .Single(element =>
                element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == name)
            );

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
