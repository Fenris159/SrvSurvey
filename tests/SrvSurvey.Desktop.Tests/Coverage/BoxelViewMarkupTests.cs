using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class BoxelViewMarkupTests
{
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
        Assert.Contains("Save Progress", boxelBindings);
        Assert.Contains("Resume Search", boxelBindings);
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
        Assert.Contains("Mark Next Empty", boxelBindings);
        Assert.Contains("{Binding BoxelSearch.MarkNextEmptyCommand}", boxelBindings);
        Assert.Contains("LAST SYSTEM AVAILABLE", boxelBindings);
        Assert.Contains(
            "{Binding BoxelSearch.LastSystemAvailable, Mode=TwoWay}",
            boxelBindings);
        Assert.Contains(
            "{Binding ShowNextIncompleteHighlight}",
            boxelBindings);
        Assert.Contains(
            "{Binding ShowCurrentNextHighlight}",
            boxelBindings);
        Assert.Contains("{Binding RowIndicator}", boxelBindings);
        Assert.Contains("ACTION", boxelBindings);
        Assert.Contains(
            "{Binding BoxelSearch.PreviousSystemPageCommand}",
            boxelBindings);
        Assert.Contains(
            "{Binding BoxelSearch.NextSystemPageCommand}",
            boxelBindings);
        Assert.Contains("{Binding BoxelSearch.SystemPageText}", boxelBindings);
        Assert.Contains("Previous page", boxelBindings);
        Assert.Contains("Next page", boxelBindings);
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
        Assert.DoesNotContain(
            searchBindings,
            binding => binding.Contains("BoxelSearch", StringComparison.Ordinal));
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

        Assert.Contains("Open Selected", values);
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
