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
        Assert.Contains("ACTION", boxelBindings);
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
            && element.Attribute("ColumnDefinitions")?.Value == "Auto,Auto,Auto");
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
        Assert.Contains("EDIT", values);
        Assert.Contains("{Binding IsSelected, Mode=TwoWay}", values);
        Assert.DoesNotContain("Select all", values);
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
