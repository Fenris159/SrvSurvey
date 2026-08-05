using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class TravelViewMarkupTests
{
    [Fact]
    public void TravelUsesRouteManagerSurfaceNavigationAndFleetCarrierTabs()
    {
        var document = LoadTravelView();
        var tabs = document.Descendants()
            .Where(element => element.Name.LocalName == "TabItem")
            .ToArray();

        Assert.Equal(
            ["Route Manager", "Surface Navigation", "FC Routes"],
            tabs.Select(tab => tab.Attribute("Header")?.Value ?? string.Empty)
                .ToArray());
        Assert.All(
            tabs,
            tab => Assert.Contains(
                "theme-selector",
                tab.Attribute("Classes")?.Value ?? string.Empty,
                StringComparison.Ordinal));
    }

    [Fact]
    public void ThemeSelectorTabsReuseFrontierCommanderTabStateBehavior()
    {
        var document = LoadRavenStyles();
        var styles = document.Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .ToDictionary(
                element => element.Attribute("Selector")?.Value
                    ?? string.Empty,
                StringComparer.Ordinal);

        var commanderSetters = ReadSetters(
            styles["TabItem.commander-profile-tab"]);
        var themeSetters = ReadSetters(styles["TabItem.theme-selector"]);

        Assert.Equal(commanderSetters, themeSetters);
        Assert.DoesNotContain("Background", themeSetters.Keys);
        Assert.DoesNotContain("Foreground", themeSetters.Keys);
        Assert.DoesNotContain("TabItem.theme-selector:pointerover", styles.Keys);
        Assert.DoesNotContain("TabItem.theme-selector:selected", styles.Keys);
    }

    [Fact]
    public void TravelTabsAreUnboundedAndSharedPanelsRemainBelowThem()
    {
        var document = LoadTravelView();
        var tabControl = FindNamedElement(document, "TravelModeTabs");
        var separator = FindNamedElement(document, "TravelTabsSeparator");
        var sharedPanels = FindNamedElement(document, "TravelSharedPanels");

        Assert.Equal("TabControl", tabControl.Name.LocalName);
        Assert.NotEqual("Border", tabControl.Parent?.Name.LocalName);
        Assert.Same(tabControl.Parent, separator.Parent);
        Assert.Same(tabControl.Parent, sharedPanels.Parent);
        Assert.True(
            GetSiblingIndex(tabControl) < GetSiblingIndex(separator));
        Assert.True(
            GetSiblingIndex(separator) < GetSiblingIndex(sharedPanels));

        var tabText = string.Join(
            " ",
            tabControl.Descendants()
                .Select(element => element.Attribute("Text")?.Value)
                .Where(text => text is not null));
        Assert.DoesNotContain("System notes", tabText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Commander journeys",
            tabText,
            StringComparison.Ordinal);

        var sharedText = string.Join(
            " ",
            sharedPanels.Descendants()
                .Select(element => element.Attribute("Text")?.Value)
                .Where(text => text is not null));
        Assert.Contains("System notes", sharedText, StringComparison.Ordinal);
        Assert.Contains(
            "Commander journeys",
            sharedText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RouteManagerOffersStableRowsFavoritesAndRequestedFileActions()
    {
        var document = LoadTravelView();
        var routeManagerTab = document.Descendants().Single(element =>
            element.Name.LocalName == "TabItem"
            && element.Attribute("Header")?.Value == "Route Manager");
        var bindings = routeManagerTab.Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();
        var buttonContent = routeManagerTab.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .OfType<string>()
            .ToArray();

        Assert.Contains("{Binding RouteManager.Routes}", bindings);
        Assert.Contains("{Binding IsSelected, Mode=TwoWay}", bindings);
        Assert.Contains("{Binding ToggleFavoriteCommand}", bindings);
        Assert.Contains("{Binding RouteManager.SortNameCommand}", bindings);
        Assert.Contains("{Binding RouteManager.SortDateCommand}", bindings);
        Assert.Contains(
            "{Binding RouteManager.FavoritesFirst, Mode=TwoWay}",
            bindings);
        Assert.Contains("Open Route Workspace", buttonContent);
        Assert.Contains("Import", buttonContent);
        Assert.Contains("Export", buttonContent);
        Assert.Contains("Exp Spansh", buttonContent);
        Assert.Contains("Exp CSV", buttonContent);
        Assert.Contains("Notes", buttonContent);
        Assert.Contains("Activate", buttonContent);
        Assert.Contains("Deactivate", buttonContent);

        var autoCopyToggle = FindNamedElement(
            document,
            "AutoCopyNextHopToggle");
        var nextSystemReadout = FindNamedElement(document, "NextSystemReadout");
        Assert.Equal(
            "{Binding RouteManager.ToggleAutoCopyCommand}",
            autoCopyToggle.Attribute("Command")?.Value);
        Assert.Equal(
            "{Binding RouteManager.AutoCopy, Mode=OneWay}",
            autoCopyToggle.Attribute("IsChecked")?.Value);
        Assert.Equal("Auto-copy the next hop", autoCopyToggle.Attribute("Content")?.Value);
        Assert.Same(nextSystemReadout.Parent, autoCopyToggle.Parent);
        Assert.True(
            GetSiblingIndex(nextSystemReadout)
                < GetSiblingIndex(autoCopyToggle));

        var deactivateButton = routeManagerTab.Descendants()
            .Single(element => element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value
                    == "{Binding RouteManager.DeactivateCommand}");
        var openWorkspaceButton = routeManagerTab.Descendants()
            .Single(element => element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value
                    == "{Binding RouteManager.OpenWorkspaceCommand}");
        Assert.Same(deactivateButton.Parent, openWorkspaceButton.Parent);
        Assert.True(
            GetSiblingIndex(deactivateButton)
                < GetSiblingIndex(openWorkspaceButton));

        var notesButton = routeManagerTab.Descendants()
            .Single(element => element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value
                    == "{Binding EditNotesCommand}");
        var activateButton = routeManagerTab.Descendants()
            .Single(element => element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value
                    == "{Binding ActivateCommand}");
        Assert.Same(notesButton.Parent, activateButton.Parent);
        Assert.True(
            GetSiblingIndex(notesButton) < GetSiblingIndex(activateButton));

        var favoriteButton = routeManagerTab.Descendants()
            .Single(element => element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value
                    == "{Binding ToggleFavoriteCommand}");
        Assert.Equal("favorite-star", favoriteButton.Attribute("Classes")?.Value);
        Assert.Equal("42", favoriteButton.Attribute("Width")?.Value);
        Assert.Equal("42", favoriteButton.Attribute("Height")?.Value);
        Assert.Null(favoriteButton.Attribute("Background"));
        Assert.Equal(
            "31",
            favoriteButton.Descendants()
                .Single(element => element.Name.LocalName == "TextBlock")
                .Attribute("FontSize")?.Value);
        Assert.Contains(
            "{Binding RenameCommand}",
            bindings);
        Assert.Contains(document.Descendants(), element =>
            element.Attribute("IsVisible")?.Value
                == "{Binding RouteManager.IsRenameVisible}");
    }

    [Fact]
    public void FleetCarrierTabUsesIndependentManagerWorkspaceAndFileActions()
    {
        var document = LoadTravelView();
        var tab = document.Descendants().Single(element =>
            element.Name.LocalName == "TabItem"
            && element.Attribute("Header")?.Value == "FC Routes");
        var bindings = tab.Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();
        var clickHandlers = tab.Descendants()
            .Select(element => element.Attribute("Click")?.Value)
            .OfType<string>()
            .ToArray();

        Assert.Contains(
            "{Binding FleetCarrierRouteManager.Routes}",
            bindings);
        Assert.Contains(
            "{Binding FleetCarrierRouteManager.ToggleAutoCopyCommand}",
            bindings);
        Assert.Contains(
            "{Binding FleetCarrierRouteManager.AutoCopy, Mode=OneWay}",
            bindings);
        Assert.Contains(
            "{Binding FleetCarrierRoute.NextHopName}",
            bindings);
        Assert.Contains(
            "{Binding FleetCarrierRoute.CarrierJumpCountdownTitle}",
            bindings);
        Assert.Contains(
            "{Binding FleetCarrierRoute.CarrierJumpCountdownValue}",
            bindings);
        Assert.Contains(
            "{Binding FleetCarrierRoute.CarrierJumpPhaseLabel}",
            bindings);
        Assert.Contains("ImportFleetCarrierRoutes_Click", clickHandlers);
        Assert.Contains("ExportFleetCarrierRoutes_Click", clickHandlers);
        Assert.Contains("ExportSpanshFleetCarrierRoutes_Click", clickHandlers);
        Assert.Contains("ExportCsvFleetCarrierRoutes_Click", clickHandlers);
        Assert.Contains("{Binding RenameCommand}", bindings);

        var currentRoute = tab.Descendants().First(element =>
            element.Attribute("Text")?.Value
                == "{Binding FleetCarrierRoute.RouteName}");
        var nextSystem = FindNamedElement(
            document,
            "FleetCarrierNextSystemReadout");
        var countdown = FindNamedElement(
            document,
            "FleetCarrierJumpCountdownReadout");
        Assert.Same(currentRoute.Parent?.Parent, nextSystem.Parent);
        Assert.Same(nextSystem.Parent, countdown.Parent);
        Assert.True(GetSiblingIndex(currentRoute.Parent!) < GetSiblingIndex(nextSystem));
        Assert.True(GetSiblingIndex(nextSystem) < GetSiblingIndex(countdown));

        Assert.Contains(document.Descendants(), element =>
            element.Attribute("IsVisible")?.Value
                == "{Binding FleetCarrierRouteManager.IsDeleteConfirmationVisible}");
        Assert.Contains(document.Descendants(), element =>
            element.Attribute("IsVisible")?.Value
                == "{Binding FleetCarrierRouteManager.IsNotesVisible}");
        Assert.Contains(document.Descendants(), element =>
            element.Attribute("IsVisible")?.Value
                == "{Binding FleetCarrierRouteManager.IsRenameVisible}");
    }

    private static XDocument LoadTravelView() => XDocument.Load(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "SrvSurvey.Desktop",
        "Views",
        "TravelView.axaml"));

    private static XDocument LoadRavenStyles() => XDocument.Load(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "SrvSurvey.Desktop",
        "Styles",
        "RavenStyles.axaml"));

    private static Dictionary<string, string> ReadSetters(
        XElement style) => style.Elements()
        .Where(element => element.Name.LocalName == "Setter")
        .ToDictionary(
            element => element.Attribute("Property")?.Value ?? string.Empty,
            element => element.Attribute("Value")?.Value ?? string.Empty,
            StringComparer.Ordinal);

    private static XElement FindNamedElement(
        XDocument document,
        string name) => document.Descendants().Single(element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name"
                && attribute.Value == name));

    private static int GetSiblingIndex(XElement element)
    {
        return element.Parent?.Elements().TakeWhile(candidate => candidate != element)
            .Count() ?? -1;
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
