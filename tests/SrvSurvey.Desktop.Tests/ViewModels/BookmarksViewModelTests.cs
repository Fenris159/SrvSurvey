using SrvSurvey.Desktop.ViewModels;
namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class BookmarksViewModelTests
{
    [Fact]
    public void RepeatedSaveEditsOneBookmarkAndDeletionCanBeUndone()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var vm = new BookmarksViewModel(directory) { System = "Sol", Category = "Home" };
            vm.SaveCommand.Execute(null); vm.Notes = "Updated"; vm.SaveCommand.Execute(null);
            Assert.Equal("Updated", Assert.Single(vm.Items).Notes);
            vm.DeleteCommand.Execute(null); Assert.Empty(vm.Items); vm.UndoDelete();
            Assert.Equal("Home", Assert.Single(new BookmarksViewModel(directory).Items).Category);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
