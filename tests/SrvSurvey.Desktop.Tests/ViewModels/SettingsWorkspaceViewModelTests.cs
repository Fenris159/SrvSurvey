using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.ViewModels;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class SettingsWorkspaceViewModelTests
{
    [Fact]
    public void CategoriesPreserveGlobalOverlayBoundaryAndMigrationPath()
    {
        var viewModel = new SettingsWorkspaceViewModel();

        Assert.Equal(
            [
                "Application",
                "Desktop",
                "Global overlays",
                "Input",
                "Privacy & sharing",
                "Screenshots",
                "Data & migration",
            ],
            viewModel.Categories.Select(category => category.Name));
        Assert.Contains(
            viewModel.SearchCatalog,
            entry => entry.Title == "Import SrvSurvey User Data"
                && entry.CategoryKey == "data");
    }

    [Fact]
    public void SearchUsesAliasesGroupsResultsAndSupportsKeyboardSelection()
    {
        var viewModel = new SettingsWorkspaceViewModel
        {
            SearchQuery = "hotkey",
        };

        var group = Assert.Single(viewModel.GroupedSearchResults);
        Assert.Equal("Input", group.CategoryName);
        Assert.Equal(2, group.Results.Count);
        Assert.True(group.Results[0].IsSelected);

        viewModel.MoveSearchSelection(1);

        Assert.False(group.Results[0].IsSelected);
        Assert.True(group.Results[1].IsSelected);
        var activated = viewModel.ActivateSelectedSearchResult();
        Assert.Same(group.Results[1], activated);
        Assert.True(viewModel.IsInputSelected);
        Assert.False(viewModel.HasSearchQuery);
    }

    [AvaloniaFact]
    public void EverySearchEntryTargetsARealControlAndHighlightContainer()
    {
        var viewModel = new SettingsWorkspaceViewModel();
        var view = new SettingsView();

        foreach (var entry in viewModel.SearchCatalog)
        {
            Assert.NotNull(view.FindControl<Control>(entry.TargetControlName));
            Assert.NotNull(view.FindControl<Control>(entry.HighlightControlName));
        }
    }
}
