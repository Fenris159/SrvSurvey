using Avalonia.Headless.XUnit;
using SrvSurvey.Desktop.Views;
using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class BoxelViewMarkupTests
{
    [AvaloniaFact]
    public void ConstructorLoadsTheBoxelWorkspace()
    {
        var view = new BoxelView();

        Assert.NotNull(view.Content);
    }

    [Fact]
    public void SelectedSystemPageUsesAccentForegroundForText()
    {
        var styles = LoadStyles().Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .ToDictionary(
                element => element.Attribute("Selector")?.Value ?? string.Empty,
                StringComparer.Ordinal);
        var selectedTextStyle = styles[
            "ListBox.system-pages ListBoxItem:selected TextBlock"];

        var foreground = Assert.Single(selectedTextStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "Foreground");
        Assert.Equal(
            "{DynamicResource RavenAccentForegroundBrush}",
            foreground.Attribute("Value")?.Value);
    }

    [Fact]
    public void DedicatedPageOwnsTheWholeBoxelWorkspace()
    {
        var boxel = LoadView("BoxelView.axaml");
        var search = LoadView("SearchView.axaml");
        var boxelBindings = boxel.Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();
        var searchBindings = search.Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();

        Assert.Contains("{Binding BoxelSearch.TopBoxelText, Mode=TwoWay}", boxelBindings);
        Assert.Contains("{Binding BoxelSearch.AutoCopy, Mode=TwoWay}", boxelBindings);
        Assert.Contains("{Binding BoxelSearch.AuditAllCommand}", boxelBindings);
        Assert.Contains("{Binding BoxelSearch.Systems}", boxelBindings);
        Assert.Contains(
            "{Binding BoxelSearch.LibrarySaveButtonText}",
            boxelBindings);
        Assert.Contains("Open Library", boxelBindings);
        Assert.Contains("Boxel Stats", boxelBindings);
        Assert.Contains("BoxelStats_Click", boxelBindings);
        Assert.Contains("Open stats", boxelBindings);
        Assert.Contains("{Binding BoxelSearch.StatsGlanceText}", boxelBindings);
        Assert.Contains("VoxStellar", boxelBindings);
        Assert.Contains(
            "Send Journal to VoxStellar for boxel surveying",
            boxelBindings);
        Assert.Contains(
            "{Binding VoxStellar.JournalUploadEnabled, Mode=TwoWay}",
            boxelBindings);
        Assert.Contains(
            "{Binding VoxStellar.CanChangeUploadPreference}",
            boxelBindings);
        Assert.Contains("VoxStellar_Click", boxelBindings);
        Assert.Contains("VoxStellarInfo_Click", boxelBindings);
        Assert.Contains(
            "avares://SrvSurvey.Desktop/Assets/VoxStellar/voxstellar.png",
            boxelBindings);
        var voxStellarImage = boxel.Descendants().Single(element =>
            element.Name.LocalName == "Image"
            && element.Attribute("Source")?.Value.Contains(
                "VoxStellar",
                StringComparison.Ordinal) == true);
        Assert.Equal("30", voxStellarImage.Attribute("Height")?.Value);
        var boxelStatsButton = boxel.Descendants().Single(element =>
            element.Name.LocalName == "Button"
            && element.Attribute("Click")?.Value == "BoxelStats_Click"
            && element.Descendants().Any(descendant =>
                descendant.Attribute("Text")?.Value == "Boxel Stats"));
        var boxelStatsIcon = Assert.Single(boxelStatsButton.Descendants(), element =>
            element.Name.LocalName == "PathIcon");
        Assert.Equal(
            "{StaticResource data_pie_regular}",
            boxelStatsIcon.Attribute("Data")?.Value);
        Assert.Equal("30", boxelStatsIcon.Attribute("Width")?.Value);
        Assert.Equal("30", boxelStatsIcon.Attribute("Height")?.Value);
        Assert.Contains(
            "{Binding BoxelSearch.SystemNameSuggestions}",
            boxelBindings);
        Assert.Contains(
            "{Binding BoxelSearch.SelectedSystemSuggestionIndex, Mode=TwoWay}",
            boxelBindings);
        Assert.Contains("{Binding Source}", boxelBindings);
        Assert.Contains("TopBoxelTextBox_KeyDown", boxelBindings);
        Assert.Contains("CURRENT BOXEL PREFIX", boxelBindings);
        Assert.Contains("NEXT INCOMPLETE SYSTEM", boxelBindings);
        Assert.Contains(
            "Next incomplete suffix in the current boxel.",
            boxelBindings);
        Assert.DoesNotContain(
            "Lowest incomplete suffix in the current boxel.",
            boxelBindings);
        Assert.Contains("Mark Next Empty", boxelBindings);
        Assert.Contains("{Binding BoxelSearch.MarkNextEmptyCommand}", boxelBindings);
        Assert.Contains("LAST SYSTEM AVAILABLE", boxelBindings);
        Assert.Contains(
            "{Binding BoxelSearch.LastSystemAvailable, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}",
            boxelBindings);
        Assert.Contains(
            "{Binding BoxelSearch.HasLastSystemAvailableError}",
            boxelBindings);
        Assert.Contains(
            "{Binding BoxelSearch.LastSystemAvailableValidationMessage}",
            boxelBindings);
        var lastSystemAvailableTextBox = boxel.Descendants().Single(element =>
            element.Name.LocalName == "TextBox"
            && element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name"
                && attribute.Value == "LastSystemAvailableTextBox"));
        Assert.Equal(
            "LastSystemAvailable_LostFocus",
            lastSystemAvailableTextBox.Attribute("LostFocus")?.Value);
        Assert.Contains("LastSystemAvailable_LostFocus", boxelBindings);
        Assert.Contains("ApplyLastSystemAvailable_LostFocus", boxelBindings);
        Assert.Contains(
            "{Binding BoxelSearch.SortDescending, Mode=TwoWay}",
            boxelBindings);
        Assert.Contains(
            "Sort (descending) for working results backwards.",
            boxelBindings);
        var stopSearchButton = boxel.Descendants().Single(element =>
            element.Name.LocalName == "Button"
            && element.Attribute("Content")?.Value == "Stop search");
        Assert.Equal("danger", stopSearchButton.Attribute("Classes")?.Value);
        Assert.Contains(
            "{Binding ShowNextIncompleteHighlight}",
            boxelBindings);
        Assert.Contains(
            "{Binding ShowCurrentNextHighlight}",
            boxelBindings);
        Assert.Contains("{Binding RowIndicator}", boxelBindings);
        Assert.Contains("ACTION", boxelBindings);
        Assert.Contains("Show Only Deferred", boxelBindings);
        Assert.Contains(
            "{Binding BoxelSearch.ShowOnlyDeferred, Mode=TwoWay}",
            boxelBindings);
        Assert.Contains(boxel.Descendants(), element =>
            element.Name.LocalName == "BoxelSystemActionMenu");
        Assert.Contains(
            "{Binding BoxelSearch.PreviousSystemPageCommand}",
            boxelBindings);
        Assert.Contains(
            "{Binding BoxelSearch.NextJumpPageCommand}",
            boxelBindings);
        Assert.Contains(
            "{Binding BoxelSearch.NextSystemPageCommand}",
            boxelBindings);
        Assert.Contains("{Binding BoxelSearch.SystemPageText}", boxelBindings);
        Assert.Contains("Previous page", boxelBindings);
        Assert.Contains("Next page", boxelBindings);
        Assert.Contains("Next Jump Page", boxelBindings);
        Assert.Contains("Select page", boxelBindings);
        Assert.Contains(
            "{Binding BoxelSearch.SystemPageNumbers}",
            boxelBindings);
        Assert.Contains(
            "{Binding BoxelSearch.SelectedSystemPageIndex, Mode=TwoWay}",
            boxelBindings);
        Assert.Contains(
            "{Binding BoxelSearch.SystemPagePickerWidth}",
            boxelBindings);
        var systemPageFlyout = boxel.Descendants().Single(element =>
            element.Name.LocalName == "Flyout"
            && element.Descendants().Any(descendant =>
                descendant.Attribute("Classes")?.Value == "system-pages"));
        Assert.Equal(
            "TopEdgeAlignedRight",
            systemPageFlyout.Attribute("Placement")?.Value);
        Assert.Equal(
            "system-page-picker",
            systemPageFlyout.Attribute("FlyoutPresenterClasses")?.Value);
        var systemPagePickerButton = boxel.Descendants().Single(element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name"
                && attribute.Value == "SystemPagePickerButton"));
        Assert.Equal(
            "{Binding BoxelSearch.SystemPagePickerWidth}",
            systemPagePickerButton.Attribute("Width")?.Value);
        var systemPageList = systemPageFlyout.Descendants().Single(element =>
            element.Name.LocalName == "ListBox");
        Assert.Equal("362", systemPageList.Attribute("MaxHeight")?.Value);
        Assert.Equal(
            "{Binding BoxelSearch.SystemPagePickerWidth}",
            systemPageList.Attribute("Width")?.Value);
        Assert.Equal(
            "SystemPageList_SelectionChanged",
            systemPageList.Attribute("SelectionChanged")?.Value);
        Assert.DoesNotContain("500", boxelBindings);
        Assert.Contains("{StaticResource question_circle_regular}", boxelBindings);
        Assert.Contains("Explain last system available", boxelBindings);
        Assert.Contains("ExpectedSystemsInfo_Click", boxelBindings);
        Assert.Contains(
            "{Binding BoxelSearch.CanNavigateSearchTree}",
            boxelBindings);
        Assert.Contains("Boxel hierarchy", boxelBindings);
        Assert.Contains("LOCATION IN SEARCH", boxelBindings);
        Assert.Contains("UP ONE LEVEL", boxelBindings);
        Assert.Contains("← PREVIOUS AT THIS LEVEL", boxelBindings);
        Assert.Contains("NEXT AT THIS LEVEL →", boxelBindings);
        Assert.Contains("CHILD BOXELS · ONE LEVEL SMALLER", boxelBindings);
        Assert.Contains(
            "{Binding BoxelSearch.BreadcrumbBoxels}",
            boxelBindings);
        Assert.Contains(
            "{Binding BoxelSearch.CurrentHierarchyBoxelProgressLabel}",
            boxelBindings);
        Assert.Contains("{Binding BoxelSearch.NavigateParentCommand}", boxelBindings);
        Assert.Contains("{Binding BoxelSearch.NavigatePreviousCommand}", boxelBindings);
        Assert.Contains("{Binding BoxelSearch.NavigateNextCommand}", boxelBindings);
        Assert.DoesNotContain(
            boxelBindings,
            value => value.Contains("BoxelSearch.ParentBoxel.", StringComparison.Ordinal)
                || value.Contains("BoxelSearch.PreviousSiblingBoxel.", StringComparison.Ordinal)
                || value.Contains("BoxelSearch.CurrentHierarchyBoxel.", StringComparison.Ordinal)
                || value.Contains("BoxelSearch.NextSiblingBoxel.", StringComparison.Ordinal));
        Assert.Contains(
            "{Binding ProgressLabel}",
            boxelBindings);
        Assert.Contains(
            "{Binding StatusLabel}",
            boxelBindings);
        var centeredHierarchyControls = boxel.Descendants().Single(element =>
            element.Name.LocalName == "Grid"
            && element.Attribute("ColumnDefinitions")?.Value == "Auto,Auto,Auto"
            && element.Attribute("HorizontalAlignment")?.Value == "Center"
            && element.Elements().Any(child => child.Name.LocalName == "Border"));
        Assert.Equal(
            "Center",
            centeredHierarchyControls.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal(
            3,
            centeredHierarchyControls.Elements().Count(element =>
                element.Name.LocalName is "Button" or "Border"));
        Assert.Contains(
            "Treat systems whose Spansh body data predates the start date as complete",
            boxelBindings);
        Assert.Contains(
            "This is an explicit completion rule when Spansh has body records older than the search start date, even when full-FSS completion is required.",
            boxelBindings);
        Assert.Contains(
            "Requires FSSAllBodiesFound for new local visits. Enabled earlier-visit and older-Spansh completion rules still apply.",
            boxelBindings);
        Assert.Equal(
            2,
            boxel.Descendants().Count(element =>
                element.Name.LocalName == "Grid"
                && element.Attribute("ColumnDefinitions")?.Value
                    == "2.05*,0.75*,1.3*,1.3*,84,110"));
        Assert.DoesNotContain(
            boxel.Descendants(),
            element => element.Attribute("ColumnDefinitions")?.Value
                == "2.2*,0.8*,1.15*,1.15*,110,110");
        Assert.DoesNotContain(
            searchBindings,
            binding => binding.Contains("BoxelSearch", StringComparison.Ordinal));
    }

    [Fact]
    public void SystemActionMenuProvidesFourThemedRadialActions()
    {
        var menu = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SrvSurvey.Desktop",
            "Controls",
            "BoxelSystemActionMenu.axaml"));
        var values = menu.Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();

        Assert.Contains("{Binding CompleteCommand}", values);
        Assert.Contains("{Binding ReopenCommand}", values);
        Assert.Contains("{Binding DeferCommand}", values);
        Assert.Contains("{Binding StartHereCommand}", values);
        Assert.Contains("Complete", values);
        Assert.Contains("Reopen", values);
        Assert.Contains("Defer", values);
        Assert.Contains("Start Here", values);
        Assert.Contains("radial-menu-surface", values);
        Assert.Contains("radial-slice slice-top-right", values);
        Assert.Contains("radial-slice slice-bottom-right", values);
        Assert.Contains("radial-slice slice-bottom-left", values);
        Assert.Contains("radial-slice slice-top-left", values);
        Assert.Contains("0:0:1", values);
        Assert.Contains("0:0:1.5", values);
        Assert.Contains(
            "Button.radial-launcher.engaged Canvas.radial-glyph",
            values);
        Assert.Contains(
            "Button.radial-launcher.engaged Path.slice-top-right",
            values);
        Assert.Contains(
            "Button.radial-launcher.engaged Path.radial-slice",
            values);
        Assert.Contains(
            "Button.radial-launcher.engaged Ellipse.radial-center",
            values);
        Assert.Contains("HorizontalContentAlignment", values);
        Assert.Contains("VerticalContentAlignment", values);
        Assert.Contains("option-chrome", values);
        Assert.Contains("{TemplateBinding Clip}", values);
        Assert.Contains("{StaticResource RadialOptionTopGeometry}", values);
        Assert.Contains("{StaticResource RadialOptionLeftGeometry}", values);
        Assert.Contains("{StaticResource RadialOptionRightGeometry}", values);
        Assert.Contains("{StaticResource RadialOptionBottomGeometry}", values);
        Assert.Contains(
            "Button.radial-option:pointerover /template/ Path.option-chrome",
            values);
        Assert.Contains("Transparent", values);
        Assert.Contains("{DynamicResource RavenAccentBrush}", values);
        Assert.Contains("{DynamicResource RavenAccentHoverBrush}", values);
        Assert.Contains("{DynamicResource RavenWarningBrush}", values);
        Assert.DoesNotContain("System actions", values);
        var launcher = menu.Descendants().Single(element =>
            element.Name.LocalName == "Button"
            && element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name"
                && attribute.Value == "Launcher"));
        Assert.Equal("Menu_PointerEntered", launcher.Attribute("PointerEntered")?.Value);
        Assert.Equal("Menu_PointerExited", launcher.Attribute("PointerExited")?.Value);
        var launcherStyle = menu.Descendants().Single(element =>
            element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value == "Button.radial-launcher");
        Assert.Contains(launcherStyle.Elements(), element =>
            element.Attribute("Property")?.Value == "BorderThickness"
            && element.Attribute("Value")?.Value == "0");
        Assert.Contains(launcherStyle.Elements(), element =>
            element.Attribute("Property")?.Value == "Template");
        var optionStyle = menu.Descendants().Single(element =>
            element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value == "Button.radial-option");
        Assert.Contains(optionStyle.Elements(), element =>
            element.Attribute("Property")?.Value == "Background"
            && element.Attribute("Value")?.Value == "{DynamicResource RavenAccentMutedBrush}");
        var optionHoverStyle = menu.Descendants().Single(element =>
            element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value == "Button.radial-option:pointerover");
        Assert.Contains(optionHoverStyle.Elements(), element =>
            element.Attribute("Property")?.Value == "Background"
            && element.Attribute("Value")?.Value == "{DynamicResource RavenAccentMutedBrush}");
        Assert.Contains(optionHoverStyle.Elements(), element =>
            element.Attribute("Property")?.Value == "BorderBrush"
            && element.Attribute("Value")?.Value == "{DynamicResource RavenWarningBrush}");
        Assert.Contains(optionHoverStyle.Elements(), element =>
            element.Attribute("Property")?.Value == "BorderThickness"
            && element.Attribute("Value")?.Value == "2");
        var disabledOptionStyle = menu.Descendants().Single(element =>
            element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value == "Button.radial-option:disabled");
        Assert.Contains(disabledOptionStyle.Elements(), element =>
            element.Attribute("Property")?.Value == "Opacity"
            && element.Attribute("Value")?.Value == "1");
        Assert.Contains(disabledOptionStyle.Elements(), element =>
            element.Attribute("Property")?.Value == "Background"
            && element.Attribute("Value")?.Value == "{DynamicResource RavenAccentMutedBrush}");
        Assert.Contains(disabledOptionStyle.Elements(), element =>
            element.Attribute("Property")?.Value == "BorderBrush"
            && element.Attribute("Value")?.Value == "{DynamicResource RavenBorderBrush}");
        Assert.Contains(disabledOptionStyle.Elements(), element =>
            element.Attribute("Property")?.Value == "Cursor"
            && element.Attribute("Value")?.Value == "Arrow");
        var disabledShadeStyle = menu.Descendants().Single(element =>
            element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value
                == "Button.radial-option:disabled /template/ Path.option-disabled-shade");
        Assert.Contains(disabledShadeStyle.Elements(), element =>
            element.Attribute("Property")?.Value == "Opacity"
            && element.Attribute("Value")?.Value == "0.45");
        Assert.DoesNotContain(menu.Descendants(), element =>
            element.Name.LocalName == "Ellipse"
            && element.Attribute("Stroke") is not null);
        var menuHitSurface = menu.Descendants().Single(element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name"
                && attribute.Value == "MenuHitSurface"));
        Assert.Equal(
            "Menu_PointerWheelChanged",
            menuHitSurface.Attribute("PointerWheelChanged")?.Value);
    }

    [Fact]
    public void ExpectedSystemsInformationUsesOneLocalizedExampleTemplate()
    {
        var repositoryRoot = FindRepositoryRoot();
        var window = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "SrvSurvey.Desktop",
            "ExpectedSystemsInformationWindow.axaml"));
        var codeBehind = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "SrvSurvey.Desktop",
            "ExpectedSystemsInformationWindow.axaml.cs"));
        var values = window.Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();
        var example = window.Descendants().Single(element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute(XName.Get(
                "Name",
                "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value
                == "ExpectedSystemsExample");
        Assert.Contains(values, value => value.Contains(
            "The number produced is an estimate.",
            StringComparison.Ordinal));
        Assert.DoesNotContain(values, value => value.Contains(
            "Then add 1 to that number",
            StringComparison.Ordinal));
        Assert.Contains(values, value => value.Contains(
            "SrvSurvey includes system 0 when it calculates the total.",
            StringComparison.Ordinal));
        Assert.Empty(example.Elements());
        Assert.Contains(
            "For example the end system of the {0} boxel is: {1} "
                + "so you would enter 7640 and choose APPLY",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains(
            "LocalizationCatalog.Translate(ExampleTemplate)",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains("RavenAccentBrush", codeBehind, StringComparison.Ordinal);
        Assert.Contains("FontWeight.SemiBold", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Close", values);
        Assert.DoesNotContain(window.Descendants(), element =>
            element.Name.LocalName == "Button"
            && element.Attribute("Classes")?.Value.Contains(
                "link",
                StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            values,
            value => value.Contains("Underline", StringComparison.Ordinal));
    }

    [Fact]
    public void VoxStellarInformationExplainsConsentDataAndLicensing()
    {
        var window = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SrvSurvey.Desktop",
            "VoxStellarInformationWindow.axaml"));
        var values = window.Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();

        Assert.Contains(values, value => value.Contains(
            "commander name and the complete JSON data",
            StringComparison.Ordinal));
        Assert.Contains(values, value => value.Contains(
            "does not submit this data to EDDN",
            StringComparison.Ordinal));
        Assert.Contains(values, value => value.Contains(
            "worldwide, non-exclusive, royalty-free license",
            StringComparison.Ordinal));
        Assert.Contains(values, value => value.Contains(
            "Copyright © 2023 Sven Ziereis",
            StringComparison.Ordinal));
        Assert.Contains("Privacy policy", values);
        Assert.Contains("Terms of service", values);
        Assert.Contains("Plugin source", values);
    }

    [Fact]
    public void SavedSearchWindowProvidesRequestedSingleSearchLibraryColumns()
    {
        var library = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SrvSurvey.Desktop",
            "BoxelSearchLibraryWindow.axaml"));
        var values = library.Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();

        Assert.Contains("Resume Selected", values);
        Assert.Contains("Favorites first", values);
        Assert.Contains("SELECT", values);
        Assert.Contains("NAME", values);
        Assert.Contains("DATE CREATED", values);
        Assert.Contains("LAST MODIFIED", values);
        Assert.Contains("PROGRESS COMPLETED", values);
        Assert.Contains("NOTES", values);
        Assert.Contains("STATS", values);
        Assert.Contains("EDIT", values);
        Assert.Contains("Open boxel statistics", values);
        Assert.Contains("{StaticResource data_pie_regular}", values);
        Assert.Contains("{Binding OpenStatisticsCommand}", values);
        Assert.Contains("{Binding IsSelected, Mode=TwoWay}", values);
        Assert.DoesNotContain("Select all", values);
        Assert.Contains("Delete selected saved search", values);
        Assert.Contains("Add or remove saved search favorite", values);
        Assert.Contains("Rename saved search", values);
        Assert.Contains("Escape", values);
        Assert.Contains("{Binding !IsDialogVisible}", values);
        Assert.Contains("RenameTextBox", values);
        Assert.Contains("NotesTextBox", values);
        Assert.Contains("{Binding IsDeleteConfirmationVisible}", values);
    }

    [Fact]
    public void BoxelStatisticsCanRebuildBeforeAnyBoxelIsSelected()
    {
        var window = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SrvSurvey.Desktop",
            "BoxelStatsWindow.axaml"));
        var header = window.Descendants().Single(element =>
            element.Name.LocalName == "StackPanel"
            && element.Attribute("Grid.Column")?.Value == "1"
            && element.Attribute("Orientation")?.Value == "Horizontal");

        Assert.Contains(header.Elements(), element =>
            element.Attribute("Command")?.Value == "{Binding RebuildCommand}");
    }

    [Fact]
    public void BoxelStatisticsMassCodeButtonsLeaveRoomForDescenders()
    {
        var window = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SrvSurvey.Desktop",
            "BoxelStatsWindow.axaml"));
        var button = window.Descendants().Single(element =>
            element.Name.LocalName == "Button"
            && element.Attribute("CommandParameter")?.Value == "{Binding MassCode}");
        var label = Assert.Single(button.Elements(), element =>
            element.Name.LocalName == "TextBlock");

        Assert.Null(button.Attribute("Height"));
        Assert.Equal("42", button.Attribute("MinHeight")?.Value);
        Assert.Equal("12,6,12,8", button.Attribute("Padding")?.Value);
        Assert.Equal("Center", button.Attribute("VerticalContentAlignment")?.Value);
        Assert.Equal("22", label.Attribute("LineHeight")?.Value);
    }

    [Fact]
    public void BoxelStatisticsOffersDedicatedAverageExplanation()
    {
        var repositoryRoot = FindRepositoryRoot();
        var window = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "SrvSurvey.Desktop",
            "BoxelStatsWindow.axaml"));
        var dialog = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "SrvSurvey.Desktop",
            "BoxelAverageHelpDialog.axaml"));
        var windowValues = window.Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();
        var dialogValues = dialog.Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();

        Assert.Contains("How are Averages Calculated?", windowValues);
        Assert.Contains("AverageHelp_Click", windowValues);
        Assert.Contains(
            "Body count for that type \u00f7 systems recorded",
            dialogValues);
        Assert.Contains(dialogValues, value => value.Contains(
            "Selected boxel uses only",
            StringComparison.Ordinal));
        Assert.Contains(dialogValues, value => value.Contains(
            "Entire saved search combines",
            StringComparison.Ordinal));
        Assert.Contains(dialogValues, value => value.Contains(
            "Nav Beacon setting affects only",
            StringComparison.Ordinal));
    }

    [Fact]
    public void BoxelStatisticsExplainsExactFilteringSettingsAndNativeExport()
    {
        var repositoryRoot = FindRepositoryRoot();
        var window = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "SrvSurvey.Desktop",
            "BoxelStatsWindow.axaml"));
        var codeBehind = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "SrvSurvey.Desktop",
            "BoxelStatsWindow.axaml.cs"));
        var values = window.Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();

        Assert.Contains("RECENTLY RECORDED", values);
        Assert.Contains("{Binding BrowserTitle}", values);
        Assert.Contains("{Binding BrowserDescription}", values);
        Assert.Contains("{Binding ExploreChildrenCommand}", values);
        Assert.Contains("{Binding HighestRecordedSuffixText}", values);
        Assert.Contains("{Binding ConfiguredSystemsText}", values);
        Assert.DoesNotContain(values, value => value.Contains(
            "Unvisited children still show",
            StringComparison.Ordinal));
        Assert.Contains(
            "Count Nav Beacon scans as FSS complete (statistics only)",
            values);
        Assert.Contains(values, value => value.Contains(
            "does not affect boxel-search completion",
            StringComparison.Ordinal));
        Assert.Contains(values, value => value.Contains(
            "Settings apply immediately",
            StringComparison.Ordinal));
        Assert.Contains("STATISTICS SCOPE", values);
        Assert.Contains("Selected boxel", values);
        Assert.Contains("{Binding EntireSavedSearchScopeText}", values);
        Assert.Contains("{Binding StatisticsScopeDescription}", values);
        Assert.Contains(
            "{Binding IsSelectedBoxelScope, Mode=TwoWay}",
            values);
        Assert.Contains(
            "{Binding IsEntireSavedSearchScope, Mode=TwoWay}",
            values);
        var scopeTitle = window.Descendants().Single(element =>
            element.Attribute("Text")?.Value == "STATISTICS SCOPE");
        var scopePanel = scopeTitle.Ancestors().First(element =>
            element.Name.LocalName == "Border");
        Assert.Null(scopePanel.Attribute("IsVisible"));
        var entireSearchScope = scopePanel.Descendants().Single(element =>
            element.Name.LocalName == "RadioButton"
            && element.Attribute("Content")?.Value
                == "{Binding EntireSavedSearchScopeText}");
        Assert.Equal(
            "{Binding CanShowSearchRollup}",
            entireSearchScope.Attribute("IsEnabled")?.Value);
        Assert.Contains(
            "{Binding MinSystemsForAverages, Mode=TwoWay}",
            values);
        Assert.Contains(
            "{Binding MinSystemsForExport, Mode=TwoWay}",
            values);
        Assert.Equal(
            2,
            window.Descendants().Count(element =>
                element.Name.LocalName == "NumericUpDown"
                && element.Attribute("Minimum")?.Value == "1"
                && element.Attribute("Maximum")?.Value == "1000"));
        Assert.Contains("Export_Click", values);
        Assert.Contains("OpenFolderPickerAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TryGetLocalPath", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void BoxelOverlaySettingsContainOnlyAccurateBoxelControls()
    {
        var settings = LoadView("OverlaySettingsView.axaml");
        var card = settings.Descendants().Single(element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name"
                && attribute.Value == "BoxelOverlayCard"));
        var values = card.Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();

        Assert.Contains("{Binding BoxelSearch.AutoCopy, Mode=TwoWay}", values);
        Assert.Contains(
            "{Binding Notifications.CurrentBoxelSearchStatus, Mode=TwoWay}",
            values);
        Assert.Contains(
            "{Binding Notifications.ShowNextBoxelToSearch, Mode=TwoWay}",
            values);
        Assert.Contains(values, value => value.Contains(
            "mutually exclusive",
            StringComparison.Ordinal));
    }

    private static XDocument LoadView(string fileName) => XDocument.Load(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "SrvSurvey.Desktop",
        "Views",
        fileName));

    private static XDocument LoadStyles() => XDocument.Load(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "SrvSurvey.Desktop",
        "Styles",
        "RavenStyles.axaml"));

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
