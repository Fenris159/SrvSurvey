using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class SystemIdentityMarkupTests
{
    [Fact]
    public void SharedEntryShowsEdsmSourceAndId64()
    {
        var entry = Load("Controls", "SystemNameEntry.axaml");
        var values = Values(entry);

        Assert.Contains("{Binding Name}", values);
        Assert.Contains("{Binding Source}", values);
        Assert.Contains("{Binding SystemAddress}", values);
        Assert.Contains("InputBox_KeyDown", values);
    }

    [Fact]
    public void EverySingleSystemEntryUsesSharedSuggestions()
    {
        var search = Load("Views", "SearchView.axaml");
        AssertEntry(
            search,
            "{Binding Search.Query, Mode=TwoWay}");
        Assert.Contains(
            search.Descendants(),
            element => element.Attribute("Text")?.Value ==
                "Inline system suggestions come from EDSM with an Ardent fallback; the resolved center and coordinates still come from Spansh. Saving updates only the compatible sphereLimit fields.");
        AssertEntry(
            Load("Views", "GuardianView.axaml"),
            "{Binding Guardian.OriginSystemName, Mode=TwoWay}");
        AssertEntry(
            Load(null, "JourneyWindow.axaml"),
            "{Binding StartSystemQuery, Mode=TwoWay}");
        AssertEntry(
            Load("Views", "DiagnosticsView.axaml"),
            "{Binding VisitedStarsCache.SystemName, Mode=TwoWay}");
    }

    [Fact]
    public void SphereLookupActionAlignsWithTheTopOfTheAutocomplete()
    {
        var search = Load("Views", "SearchView.axaml");
        var entry = search.Descendants().Single(element =>
            element.Name.LocalName == "SystemNameEntry"
            && element.Attribute("Text")?.Value ==
                "{Binding Search.Query, Mode=TwoWay}");
        var row = entry.Parent!;
        var button = row.Elements().Single(element =>
            element.Name.LocalName == "Button");

        Assert.Equal("Grid", row.Name.LocalName);
        Assert.Equal("*,Auto", row.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("Top", button.Attribute("VerticalAlignment")?.Value);
    }

    [Fact]
    public void ResultTablesDoNotCopyOnSystemNameClick()
    {
        var search = Load("Views", "SearchView.axaml");
        AssertNoCopyBehavior(FindItemsHost(
            search,
            "{Binding Search.SearchResults}"));
        AssertNoCopyBehavior(FindItemsHost(
            search,
            "{Binding NearestSystems.Results}"));

        var guardian = Load("Views", "GuardianView.axaml");
        AssertNoCopyBehavior(FindItemsHost(
            guardian,
            "{Binding Guardian.Rows}"));

        var boxel = Load("Views", "BoxelView.axaml");
        AssertNoCopyBehavior(FindItemsHost(
            boxel,
            "{Binding BoxelSearch.Systems}"));
    }

    [Fact]
    public void SystemSummariesExposeThemedCopyLinks()
    {
        var documents = new[]
        {
            Load("Views", "OverviewView.axaml"),
            Load("Views", "BoxelView.axaml"),
            Load("Views", "SearchView.axaml"),
            Load("Views", "GuardianView.axaml"),
            Load("Views", "TravelView.axaml"),
            Load("Views", "ColonizationView.axaml"),
            Load("Views", "ExobiologyView.axaml"),
            Load(null, "JourneyWindow.axaml"),
            Load(null, "RouteWindow.axaml"),
            Load(null, "SystemNotesWindow.axaml"),
            Load(null, "BiologyPredictionsWindow.axaml"),
            Load(null, "BiologyCodexWindow.axaml"),
        };

        Assert.All(documents, document =>
            Assert.Contains(
                document.Descendants(),
                element => element.Name.LocalName == "Button"
                    && HasCopyBehavior(element)
                    && element.Attribute("Classes")?.Value
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Contains("link", StringComparer.Ordinal) == true));
    }

    [Fact]
    public void OverviewCopiesSystemNameAndId64Separately()
    {
        var overview = Load("Views", "OverviewView.axaml");
        var copyValues = overview.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .SelectMany(element => element.Attributes())
            .Where(attribute => attribute.Name.LocalName ==
                "ClipboardCopyBehavior.Text")
            .Select(attribute => attribute.Value)
            .ToArray();

        Assert.Contains("{Binding OverviewSystemName}", copyValues);
        Assert.Contains("{Binding OverviewSystemAddress}", copyValues);
        Assert.Contains(
            overview.Descendants(),
            element => element.Attribute("Text")?.Value ==
                "{Binding OverviewSystemAddressText}");
    }

    [Fact]
    public void OptionalSystemIdentitiesAvoidNullableIntermediateBindings()
    {
        var values = new[]
        {
            Load("Views", "SearchView.axaml"),
            Load("Views", "TravelView.axaml"),
            Load("Views", "BoxelView.axaml"),
            Load(null, "RouteWindow.axaml"),
        }.SelectMany(Values).ToArray();

        Assert.DoesNotContain(values, value =>
            value.Contains("NextHop.SystemAddress", StringComparison.Ordinal)
            || value.Contains("SelectedCenterSystem.SystemAddress", StringComparison.Ordinal)
            || value.Contains("ParentBoxel.", StringComparison.Ordinal)
            || value.Contains("PreviousSiblingBoxel.", StringComparison.Ordinal)
            || value.Contains("CurrentHierarchyBoxel.", StringComparison.Ordinal)
            || value.Contains("NextSiblingBoxel.", StringComparison.Ordinal));
        Assert.Contains("{Binding Search.CenterSystemAddress}", values);
        Assert.Contains("{Binding Route.NextHopSystemAddress}", values);
        Assert.Contains("{Binding FleetCarrierRoute.NextHopSystemAddress}", values);
    }

    [Fact]
    public void OverlaysKeepSystemIdentityDisplayOnly()
    {
        var documents = new[]
        {
            Load(null, "FleetCarrierRouteOverlayPresentation.axaml"),
            Load(null, "RouteBioOverlayPresentation.axaml"),
            Load(null, "SphericalSearchOverlayPresentation.axaml"),
        };

        Assert.All(documents, document =>
            Assert.DoesNotContain(
                document.Descendants(),
                HasCopyBehavior));
    }

    private static void AssertEntry(XDocument document, string textBinding)
    {
        Assert.Contains(
            document.Descendants(),
            element => element.Name.LocalName == "SystemNameEntry"
                && element.Attribute("Text")?.Value == textBinding
                && element.Attribute("PlaceholderText")?.Value
                    == "System name or id64");
    }

    private static XElement FindItemsHost(
        XDocument document,
        string itemsSource)
    {
        return document.Descendants().Single(element =>
            element.Attribute("ItemsSource")?.Value == itemsSource);
    }

    private static void AssertNoCopyBehavior(XElement element)
    {
        Assert.DoesNotContain(
            element.DescendantsAndSelf(),
            HasCopyBehavior);
    }

    private static bool HasCopyBehavior(XElement element)
    {
        return element.Attributes().Any(attribute =>
            attribute.Name.LocalName == "ClipboardCopyBehavior.Text"
            && attribute.Name.NamespaceName.Contains(
                "SrvSurvey.Desktop.Behaviors",
                StringComparison.Ordinal));
    }

    private static string[] Values(XDocument document) => document.Descendants()
        .SelectMany(element => element.Attributes())
        .Select(attribute => attribute.Value)
        .ToArray();

    private static XDocument Load(string? subdirectory, string fileName)
    {
        var segments = new List<string>
        {
            FindRepositoryRoot(),
            "src",
            "SrvSurvey.Desktop",
        };
        if (subdirectory is not null)
        {
            segments.Add(subdirectory);
        }

        segments.Add(fileName);
        return XDocument.Load(Path.Combine(segments.ToArray()));
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
