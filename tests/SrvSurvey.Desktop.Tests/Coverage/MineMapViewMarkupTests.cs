using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class MineMapViewMarkupTests
{
    [Fact]
    public void WorkspaceProvidesCatalogInstructionsAndGuardianStyleMapNavigation()
    {
        var document = XDocument.Load(
            Path.Combine(FindRepositoryRoot(), "src", "SrvSurvey.Desktop", "Views", "MineMapView.axaml")
        );
        XNamespace avalonia = "https://github.com/avaloniaui";
        var headers = document
            .Descendants(avalonia + "TabItem")
            .Select(item => item.Attribute("Header")?.Value ?? string.Empty)
            .ToArray();

        Assert.Equal(["Surface Maps", "Survey map", "Hotspot List", "Surface Hunt", "Instructions"], headers);
        var map = Assert.Single(document.Descendants(), element => element.Name.LocalName == "MineMapControl");
        Assert.Equal("True", map.Attribute("AllowViewportInteraction")?.Value);
        Assert.Contains("ViewportZoom", map.Attribute("ViewportZoom")?.Value);
        Assert.Equal("{Binding VisibleMarkerIds}", map.Attribute("VisibleMarkerIds")?.Value);
        Assert.Equal("{Binding PlanningCircleCenter, Mode=TwoWay}", map.Attribute("PlanningCircleCenter")?.Value);
        var slider = Assert.Single(document.Descendants(avalonia + "Slider"));
        Assert.Equal("1", slider.Attribute("Minimum")?.Value);
        Assert.Equal("15", slider.Attribute("Maximum")?.Value);
        Assert.Contains(
            document.Descendants(avalonia + "ItemsControl"),
            control => control.Attribute("ItemsSource")?.Value == "{Binding HotspotRows}"
        );
        Assert.Contains(
            document.Descendants(avalonia + "ItemsControl"),
            control => control.Attribute("ItemsSource")?.Value == "{Binding SurfaceHuntRows}"
        );
        Assert.Contains(
            document.Descendants(avalonia + "TextBlock"),
            text => text.Attribute("Text")?.Value == "OVERLAY"
        );
        Assert.Contains(
            document.Descendants(avalonia + "CheckBox"),
            checkBox => checkBox.Attribute("IsChecked")?.Value == "{Binding IsInOverlay, Mode=TwoWay}"
        );
        Assert.Contains(
            document.Descendants(avalonia + "CheckBox"),
            checkBox => checkBox.Attribute("IsChecked")?.Value == "{Binding FavoritesOnly, Mode=TwoWay}"
        );
        Assert.Contains(
            document.Descendants(avalonia + "Button"),
            button => button.Attribute("Content")?.Value == "For Rig tracking and full command details click here"
        );
        Assert.Contains(
            document.Descendants(avalonia + "TextBlock"),
            text => text.Attribute("Text")?.Value == "SIGNAL #"
        );
        Assert.Contains(
            document.Descendants(avalonia + "TextBlock"),
            text => text.Attribute("Text")?.Value == "FROM SOL"
        );
        Assert.DoesNotContain(
            document.Descendants(avalonia + "TextBlock"),
            text => text.Attribute("Text")?.Value == "ACTIONS"
        );
        Assert.DoesNotContain(
            document.Descendants(avalonia + "MenuItem"),
            item => item.Attribute("Header")?.Value?.Contains("Delete", StringComparison.OrdinalIgnoreCase) == true
        );
        Assert.Contains(
            document.Descendants(avalonia + "ListBox"),
            list =>
                list.Attribute("SelectedItem")?.Value?.Contains("SelectedSurveyRow", StringComparison.Ordinal) == true
        );
        var surfaceMapsScroller = Assert.Single(
            document.Descendants(avalonia + "ScrollViewer"),
            scroller =>
                scroller.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value
                == "SurfaceMapsHorizontalScroller"
        );
        Assert.Equal("Auto", surfaceMapsScroller.Attribute("HorizontalScrollBarVisibility")?.Value);
        Assert.Equal("Disabled", surfaceMapsScroller.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Contains(document.Descendants(), element => element.Attribute("Tapped")?.Value == "OnSurveyRowTapped");
        var expandButton = Assert.Single(
            document.Descendants(avalonia + "Button"),
            button => button.Attribute("Command")?.Value == "{Binding ToggleExpandedCommand}"
        );
        Assert.Equal("link survey-expand", expandButton.Attribute("Classes")?.Value);
        Assert.Equal("36", expandButton.Attribute("Height")?.Value);
        Assert.DoesNotContain(
            document.Descendants(avalonia + "ToggleButton"),
            toggle => toggle.Attribute("IsChecked")?.Value?.Contains("IsExpanded", StringComparison.Ordinal) == true
        );
        Assert.Contains(
            document.Descendants(avalonia + "ItemsControl"),
            control => control.Attribute("ItemsSource")?.Value == "{Binding Deposits}"
        );
        Assert.Contains(
            document.Descendants(avalonia + "Image"),
            image =>
                image.Attribute("Source")?.Value
                == "avares://SrvSurvey.Desktop/Assets/SurfaceMining/surface-mining-command-examples.png"
        );
        Assert.Contains(
            document.Descendants(avalonia + "TextBlock"),
            text =>
                text.Attribute("Text")?.Value
                == "Drive to the orange mining-location border and face the center marker."
        );
        Assert.Contains(
            document.Descendants(avalonia + "TextBlock"),
            text => text.Attribute("Text")?.Value == ".mining center here"
        );
        Assert.Contains(
            document.Descendants(avalonia + "TextBlock"),
            text => text.Attribute("Text")?.Value == ".mine move <commodity> here"
        );
        Assert.Contains(
            document.Descendants(avalonia + "MenuItem"),
            item => item.Attribute("Header")?.Value == "Copy system name"
        );
        Assert.Contains(
            document.Descendants(avalonia + "MenuItem"),
            item => item.Attribute("Header")?.Value == "Edit bookmark"
        );
        Assert.Equal(
            ["1.", "2.", "3."],
            document
                .Descendants(avalonia + "TextBlock")
                .Select(text => text.Attribute("Text")?.Value ?? string.Empty)
                .Where(text => text is "1." or "2." or "3.")
                .ToArray()
        );
        Assert.Contains(
            document.Descendants(avalonia + "TextBlock"),
            text => text.Attribute("Text")?.Value == "Examples"
        );
        var rootContent = document.Root?.Elements().Last();
        Assert.Equal("ScrollViewer", rootContent?.Name.LocalName);
        var mapBackground = map.Attribute("MapBackground")?.Value;
        Assert.Equal("{DynamicResource RavenRaisedSurfaceBrush}", mapBackground);
        Assert.Equal("{DynamicResource RavenMapGridBrush}", map.Attribute("GridBrush")?.Value);
        Assert.Equal("{DynamicResource RavenTextBrush}", map.Attribute("TextBrush")?.Value);
        Assert.Equal("{DynamicResource RavenSuccessBrush}", map.Attribute("PlayerBrush")?.Value);
        var overlay = XDocument.Load(
            Path.Combine(FindRepositoryRoot(), "src", "SrvSurvey.Desktop", "MineMapOverlayPresentation.axaml")
        );
        var overlayMap = Assert.Single(overlay.Descendants(), element => element.Name.LocalName == "MineMapControl");
        Assert.Equal("{Binding ShowMarkerLabelsInOverviewMap}", overlayMap.Attribute("ShowMarkerLabels")?.Value);
        var overlaySettings = XDocument.Load(
            Path.Combine(FindRepositoryRoot(), "src", "SrvSurvey.Desktop", "Views", "OverlaySettingsView.axaml")
        );
        Assert.Contains(
            overlaySettings.Descendants(avalonia + "CheckBox"),
            checkBox =>
                checkBox.Attribute("IsChecked")?.Value == "{Binding MineMap.ShowMarkerLabelsInOverviewMap, Mode=TwoWay}"
                && checkBox.Attribute("Content")?.Value == "Show Marker Labels in Overview Map"
        );
        Assert.Equal("{DynamicResource RavenMapGridBrush}", overlayMap.Attribute("GridBrush")?.Value);
        Assert.Equal("{DynamicResource RavenTextBrush}", overlayMap.Attribute("TextBrush")?.Value);
        Assert.Contains("ElementName=MineSurveyMap", slider.Attribute("Value")?.Value);
        var mapViewbox = Assert.Single(map.Ancestors(avalonia + "Viewbox"));
        Assert.Equal("640", mapViewbox.Attribute("MaxHeight")?.Value);
        Assert.Equal("Uniform", mapViewbox.Attribute("Stretch")?.Value);
        Assert.DoesNotContain(
            document.Descendants(avalonia + "Button"),
            button => button.Attribute("Content")?.Value is "−" or "+"
        );
        var favoriteButton = Assert.Single(
            document.Descendants(avalonia + "Button"),
            button => button.Attribute("Command")?.Value == "{Binding ToggleFavoriteCommand}"
        );
        Assert.Equal("9", favoriteButton.Attribute("Grid.Column")?.Value);
        Assert.Equal("favorite-star", favoriteButton.Attribute("Classes")?.Value);
        Assert.All(
            document
                .Descendants(avalonia + "Grid")
                .Where(grid =>
                    grid.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value
                        is "SurfaceMapsHeader"
                            or "HotspotListHeader"
                            or "SurfaceHuntHeader"
                ),
            header =>
                Assert.All(
                    header.Elements(avalonia + "Button"),
                    button =>
                    {
                        Assert.NotNull(button.Attribute("CommandParameter"));
                        Assert.Contains(
                            button.Descendants(avalonia + "TextBlock"),
                            text =>
                                text.Attribute("Text")
                                    ?.Value?.Contains("WorkspaceSortIndicatorConverter", StringComparison.Ordinal)
                                == true
                        );
                    }
                )
        );
        var surfaceHuntHeader = Assert.Single(
            document.Descendants(avalonia + "Grid"),
            grid =>
                grid.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value
                == "SurfaceHuntHeader"
        );
        Assert.Equal(8, surfaceHuntHeader.Elements(avalonia + "Button").Count());
        Assert.Equal(
            [
                "SEARCH GROUP",
                "MATERIAL",
                "START WITH",
                "ALSO POSSIBLE ON",
                "GEOLOGY TO LOOK FOR",
                "SPECIAL CLUE",
                "AVG GALACTIC PRICE",
                "PEAK SELL SNAPSHOT",
            ],
            surfaceHuntHeader
                .Descendants(avalonia + "TextBlock")
                .Select(text => text.Attribute("Text")?.Value ?? string.Empty)
                .Where(text => !text.Contains("WorkspaceSortIndicatorConverter", StringComparison.Ordinal))
                .ToArray()
        );
        var cardTitles = document
            .Descendants(avalonia + "TextBlock")
            .Where(text => text.Attribute("Classes")?.Value?.Contains("card-title", StringComparison.Ordinal) == true)
            .Select(text => text.Attribute("Text")?.Value ?? string.Empty)
            .ToArray();
        Assert.True(Array.IndexOf(cardTitles, "Selected map") < Array.IndexOf(cardTitles, "Marker visibility"));
        var markerVisibilityLayout = Assert.Single(
            document.Descendants(avalonia + "Grid"),
            grid =>
                grid.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value
                == "MarkerVisibilityLayout"
        );
        Assert.Equal("*,Auto,Auto", markerVisibilityLayout.Attribute("ColumnDefinitions")?.Value);
        Assert.Contains(
            markerVisibilityLayout.Descendants(avalonia + "TextBlock"),
            text => text.Attribute("Text")?.Value == "Mineral\nAmount:"
        );
        Assert.Contains(
            document.Descendants(avalonia + "Border"),
            border =>
                border.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value
                    == "MarkerRatingFilterDivider"
                && border.Attribute("Width")?.Value == "1"
        );
        Assert.Contains(
            document.Descendants(avalonia + "ComboBox"),
            comboBox =>
                comboBox.Attribute("ItemsSource")?.Value == "{Binding MarkerRatingFilterOptions}"
                && comboBox.Attribute("SelectedItem")?.Value == "{Binding SelectedMineralAmountFilter, Mode=TwoWay}"
        );
        Assert.Contains(
            document.Descendants(avalonia + "ComboBox"),
            comboBox =>
                comboBox.Attribute("ItemsSource")?.Value == "{Binding MarkerRatingFilterOptions}"
                && comboBox.Attribute("SelectedItem")?.Value == "{Binding SelectedDensityFilter, Mode=TwoWay}"
        );
        foreach (
            var binding in new[]
            {
                "{Binding SelectedMapSystem}",
                "{Binding SelectedMapBody}",
                "{Binding SelectedMapSignal}",
                "{Binding SelectedMapRadius}",
            }
        )
        {
            Assert.Contains(
                document.Descendants(avalonia + "TextBlock"),
                text => text.Attribute("Text")?.Value == binding
            );
        }
    }

    [Fact]
    public void SharedBookmarkWorkspacePresentsSurfaceMapDetailsInCompactColumns()
    {
        var document = XDocument.Load(
            Path.Combine(FindRepositoryRoot(), "src", "SrvSurvey.Desktop", "Views", "BookmarksView.axaml")
        );
        XNamespace avalonia = "https://github.com/avaloniaui";

        Assert.Contains(
            document.Descendants(avalonia + "TextBlock"),
            text =>
                text.Attribute("Text")?.Value?.Contains("saved surface mining maps", StringComparison.OrdinalIgnoreCase)
                == true
        );
        Assert.Contains(
            document.Descendants(avalonia + "TextBlock"),
            text => text.Attribute("Text")?.Value == "DETAILS"
        );
        Assert.Contains(document.Descendants(avalonia + "TextBlock"), text => text.Attribute("Text")?.Value == "NOTES");
        Assert.Contains(
            document.Descendants(avalonia + "Border"),
            border => border.Attribute("IsVisible")?.Value == "{Binding IsSurfaceMiningMap}"
        );
        Assert.Contains(
            document.Descendants(avalonia + "NumericUpDown"),
            input => input.Attribute("Value")?.Value == "{Binding SurfaceSignal}"
        );
        Assert.Contains(
            document.Descendants(avalonia + "NumericUpDown"),
            input => input.Attribute("Value")?.Value == "{Binding SurfaceLocationRadiusKm}"
        );
        Assert.Contains(
            document.Descendants(avalonia + "TextBox"),
            input => input.Attribute("Text")?.Value == "{Binding Notes}"
        );
        Assert.Equal(
            5,
            document
                .Descendants(avalonia + "CheckBox")
                .Count(checkBox =>
                    checkBox.Attribute("Content")?.Value
                        is "Mining"
                            or "Surface Mining"
                            or "Location"
                            or "POI"
                            or "Other"
                )
        );
        Assert.Equal(
            6,
            document
                .Descendants(avalonia + "Grid")
                .Single(grid =>
                    grid.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value
                    == "BookmarksHeader"
                )
                .Elements(avalonia + "Button")
                .Count()
        );
        var rows = Assert.Single(
            document.Descendants(avalonia + "ListBox"),
            list =>
                list.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value
                == "BookmarkRows"
        );
        Assert.Contains(
            rows.Descendants(avalonia + "Grid"),
            grid =>
                grid.Attribute("PointerPressed")?.Value == "BookmarkRow_PointerPressed"
                && grid.Attribute("Tag")?.Value == "{Binding Id}"
        );
        Assert.Contains(
            rows.Descendants(avalonia + "TextBlock"),
            text =>
                text.Attribute("Text")?.Value == "{Binding Notes}"
                && text.Attribute("TextTrimming")?.Value == "CharacterEllipsis"
                && text.Attribute("TextWrapping")?.Value == "NoWrap"
        );
        Assert.Contains(document.Descendants(avalonia + "TextBlock"), text => text.Attribute("Text")?.Value == "BODY");
        Assert.Contains(document.Descendants(avalonia + "TextBlock"), text => text.Attribute("Text")?.Value == "RING");
        Assert.Contains(
            document.Descendants(avalonia + "TextBox"),
            input => input.Attribute("Text")?.Value == "{Binding Ring}"
        );
        Assert.Contains(
            document.Descendants(avalonia + "ColumnDefinition"),
            column => column.Attribute("SharedSizeGroup") is not null
        );
        var header = document
            .Descendants(avalonia + "Grid")
            .Single(grid =>
                grid.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value
                == "BookmarksHeader"
            );
        Assert.All(
            header.Elements(avalonia + "Button"),
            button =>
                Assert.Contains(
                    button.Descendants(avalonia + "TextBlock"),
                    text =>
                        text.Attribute("Text")
                            ?.Value?.Contains("WorkspaceSortIndicatorConverter", StringComparison.Ordinal) == true
                )
        );
    }

    [Fact]
    public void LiveOverlayHostsOneOwnedMinusPlusControlSet()
    {
        var root = FindRepositoryRoot();
        var presentation = XDocument.Load(
            Path.Combine(root, "src", "SrvSurvey.Desktop", "MineMapOverlayPresentation.axaml")
        );
        var controls = XDocument.Load(
            Path.Combine(root, "src", "SrvSurvey.Desktop", "MineMapZoomOverlayPresentation.axaml")
        );
        XNamespace avalonia = "https://github.com/avaloniaui";

        Assert.DoesNotContain(
            presentation.Descendants(),
            element => element.Name.LocalName == "MineMapZoomOverlayPresentation"
        );
        var coordinator = File.ReadAllText(
            Path.Combine(root, "src", "SrvSurvey.Desktop", "Platform", "Overlay", "MineMapOverlayCoordinator.cs")
        );
        Assert.Contains("overlay.Show(mapWindow);", coordinator, StringComparison.Ordinal);
        Assert.Contains(
            presentation.Descendants().Where(element => element.Name.LocalName == "MineMapControl"),
            map =>
                map.Attribute("ViewportZoom")?.Value?.Contains("ViewportZoom") == true
                && map.Attribute("PlayerLocation")?.Value == "{Binding PlayerLocation}"
                && map.Attribute("PlayerHeading")?.Value == "{Binding PlayerHeading}"
                && map.Attribute("VisibleMarkerIds")?.Value == "{Binding VisibleMarkerIds}"
                && map.Attribute("PlanningCircleCenter")?.Value == "{Binding PlanningCircleCenter}"
                && map.Attribute("PlayerBrush")?.Value == "{DynamicResource RavenSuccessBrush}"
        );
        var workspace = XDocument.Load(Path.Combine(root, "src", "SrvSurvey.Desktop", "Views", "MineMapView.axaml"));
        Assert.Contains(
            workspace.Descendants().Where(element => element.Name.LocalName == "MineMapControl"),
            map =>
                map.Attribute("PlayerLocation")?.Value == "{Binding PlayerLocation}"
                && map.Attribute("PlayerHeading")?.Value == "{Binding PlayerHeading}"
                && map.Attribute("VisibleMarkerIds")?.Value == "{Binding VisibleMarkerIds}"
                && map.Attribute("PlanningCircleCenter")?.Value == "{Binding PlanningCircleCenter, Mode=TwoWay}"
                && map.Attribute("PlayerBrush")?.Value == "{DynamicResource RavenSuccessBrush}"
        );
        Assert.Equal(
            ["−", "+"],
            controls
                .Descendants(avalonia + "Button")
                .Select(button => button.Attribute("Content")?.Value ?? string.Empty)
                .ToArray()
        );
        var guardianButtons = XDocument
            .Load(Path.Combine(root, "src", "SrvSurvey.Desktop", "GuardianZoomOverlayPresentation.axaml"))
            .Descendants(avalonia + "Button")
            .ToArray();
        var mineButtons = controls.Descendants(avalonia + "Button").ToArray();
        Assert.Equal(
            guardianButtons.Select(button => button.Attribute("Background")?.Value),
            mineButtons.Select(button => button.Attribute("Background")?.Value)
        );
        Assert.Equal(
            guardianButtons.Select(button => button.Attribute("BorderBrush")?.Value),
            mineButtons.Select(button => button.Attribute("BorderBrush")?.Value)
        );
    }

    [Fact]
    public void MiningReferenceUsesCompactContentColumnsAndCenteredToggles()
    {
        var root = FindRepositoryRoot();
        var reference = XDocument.Load(
            Path.Combine(root, "src", "SrvSurvey.Desktop", "MiningReferenceOverlayPresentation.axaml")
        );
        var workspace = XDocument.Load(Path.Combine(root, "src", "SrvSurvey.Desktop", "Views", "MineMapView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";

        Assert.Null(reference.Root?.Attribute("Width"));
        Assert.All(
            reference.Descendants(avalonia + "ColumnDefinition"),
            column =>
            {
                Assert.Equal("Auto", column.Attribute("Width")?.Value);
                Assert.StartsWith("MiningRef", column.Attribute("SharedSizeGroup")?.Value);
            }
        );
        var toggle = Assert.Single(
            workspace.Descendants(avalonia + "CheckBox"),
            checkBox => checkBox.Attribute("IsChecked")?.Value == "{Binding IsInOverlay, Mode=TwoWay}"
        );
        Assert.Equal("Center", toggle.Attribute("VerticalAlignment")?.Value);
        Assert.Equal("0,-4,0,4", toggle.Attribute("Margin")?.Value);
    }

    [Theory]
    [InlineData("MiningView.axaml", 8)]
    [InlineData("MiningSearchView.axaml", 4)]
    public void MiningTableHeadersAreClickableAndShareCompactContentWidths(string fileName, int expectedTables)
    {
        var document = XDocument.Load(
            Path.Combine(FindRepositoryRoot(), "src", "SrvSurvey.Desktop", "Views", fileName)
        );
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var headers = document
            .Descendants(avalonia + "Grid")
            .Where(grid => grid.Attribute(x + "Name")?.Value.EndsWith("Header", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(expectedTables, headers.Length);
        Assert.All(
            headers,
            header =>
            {
                _ = Assert.Single(
                    header.Ancestors(avalonia + "ScrollViewer"),
                    scroller =>
                        scroller.Attribute("HorizontalScrollBarVisibility")?.Value == "Auto"
                        && scroller.Attribute("VerticalScrollBarVisibility")?.Value == "Disabled"
                );
                var columns = header.Descendants(avalonia + "ColumnDefinition").ToArray();
                var buttons = header.Elements(avalonia + "Button").ToArray();
                Assert.NotEmpty(buttons);
                Assert.Equal(columns.Length, buttons.Length);
                Assert.All(
                    columns,
                    column =>
                    {
                        Assert.Equal("Auto", column.Attribute("Width")?.Value);
                        Assert.NotNull(column.Attribute("SharedSizeGroup"));
                    }
                );
                Assert.All(buttons, button => Assert.NotNull(button.Attribute("CommandParameter")));
                Assert.All(
                    buttons,
                    button =>
                        Assert.Contains(
                            button.Descendants(avalonia + "TextBlock"),
                            text =>
                                text.Attribute("Text")
                                    ?.Value?.Contains("WorkspaceSortIndicatorConverter", StringComparison.Ordinal)
                                == true
                        )
                );
            }
        );
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SrvSurvey.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
