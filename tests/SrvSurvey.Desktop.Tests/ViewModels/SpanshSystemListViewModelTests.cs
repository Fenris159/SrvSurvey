using SrvSurvey.Core.Routes;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class SpanshSystemListViewModelTests
{
    [Fact]
    public void ReusableEntryAddsEditsReordersAndRemovesDestinationsWithoutLosingRefuelling()
    {
        SpanshPlannerFieldViewModel field = CreateField();
        SpanshSystemListViewModel list = field.SystemList!;
        Assert.False(list.HasRows);
        Assert.False(list.CommitCommand.CanExecute(null));
        list.CommitCommand.Execute(null);
        Add(list, " Sol ");
        Add(list, "Achenar");
        Add(list, "Col 285 Sector CC-K a38-2");
        Assert.Equal("Sol\nAchenar\nCol 285 Sector CC-K a38-2", field.Value);
        Assert.Empty(list.Draft);
        Assert.False(list.HasPendingEntry);
        Assert.True(list.HasRows);
        SpanshDestinationViewModel first = list.Rows[0];
        Assert.False(first.UpCommand.CanExecute(null));
        Assert.True(first.DownCommand.CanExecute(null));
        first.UpCommand.Execute(null);
        first.Refuel = true;
        first.EditCommand.Execute(null);
        Assert.True(list.IsEditing);
        Assert.True(list.HasPendingEntry);
        Assert.Equal("Save", list.CommitLabel);
        Assert.Equal("Sol", list.Draft);
        list.Draft = "Sirius";
        list.CommitCommand.Execute(null);
        Assert.Equal("Sirius", first.Name);
        Assert.Equal("Sirius", list.RefuelValue);
        first.DownCommand.Execute(null);
        Assert.Equal("2.", first.Number);
        Assert.Equal("Achenar\nSirius\nCol 285 Sector CC-K a38-2", field.Value);
        first.UpCommand.Execute(null);
        Assert.Equal("1.", first.Number);
        first.EditCommand.Execute(null);
        list.Draft = "Unsaved";
        list.CancelEditCommand.Execute(null);
        Assert.Equal("Sirius", first.Name);
        Assert.Empty(list.Draft);
        first.EditCommand.Execute(null);
        first.RemoveCommand.Execute(null);
        Assert.False(list.IsEditing);
        Assert.Empty(list.RefuelValue);
        Assert.Equal(2, list.Rows.Count);
        Assert.False(list.Rows[^1].DownCommand.CanExecute(null));
        list.Rows[^1].DownCommand.Execute(null);
        Assert.False(list.Rows[0].ShowRefuel);
        list.ShowRefuel = true;
        Assert.True(list.Rows[0].ShowRefuel);
        list.Rows[0].Refuel = true;
        field.Value = " Achenar \nVega\n";
        Assert.Equal("Achenar\nVega", field.Value);
        Assert.Equal("Achenar", list.RefuelValue);
        list.SetRefuel("Vega");
        Assert.Equal("Vega", list.RefuelValue);
        Assert.False(list.Rows[0].Refuel);
        Assert.True(list.Rows[1].Refuel);
        list.Move(first, 1);
        Assert.Equal("Achenar\nVega", field.Value);
        field.Value = "";
        Assert.False(list.HasRows);
        Assert.Empty(list.Rows);
    }

    [Fact]
    public void RadioChoicesMatchTheWebsiteAndWriteTheOriginalRequestValues()
    {
        SpanshPlannerField definition = SpanshPlannerCatalog
            .ShipPlanners[0]
            .Fields.Single(field => field.Key == "supercharge_multiplier");
        var field = new SpanshPlannerFieldViewModel(definition);
        Assert.True(field.IsRadioChoice);
        Assert.False(field.IsDropdownChoice);
        Assert.Equal("Normal Supercharge", field.RadioChoices[0].Name);
        Assert.Equal("supercharge_multiplier", field.RadioChoices[0].Group);
        Assert.True(field.RadioChoices[0].IsChecked);
        field.RadioChoices[1].IsChecked = true;
        Assert.Equal("6", field.Value);
        Assert.False(field.RadioChoices[0].IsChecked);
        field.RadioChoices[0].IsChecked = false;
        Assert.Equal("6", field.Value);
        var algorithm = new SpanshPlannerFieldViewModel(
            SpanshPlannerCatalog.ShipPlanners[3].Fields.Single(field => field.Key == "algorithm")
        );
        Assert.True(algorithm.IsDropdownChoice);
        Assert.False(algorithm.IsRadioChoice);
        var build = new SpanshPlannerFieldViewModel(new("ship_build", "Build", SpanshPlannerFieldType.ShipBuild));
        Assert.True(build.IsText);
        Assert.True(build.IsMultiline);
        Assert.Null(build.SystemList);
    }

    private static SpanshPlannerFieldViewModel CreateField() =>
        new(new("destination", "Destinations", SpanshPlannerFieldType.Systems));

    private static void Add(SpanshSystemListViewModel list, string name)
    {
        list.Draft = name;
        Assert.True(list.CommitCommand.CanExecute(null));
        list.CommitCommand.Execute(null);
    }
}
