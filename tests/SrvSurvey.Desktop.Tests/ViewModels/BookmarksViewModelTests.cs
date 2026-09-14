using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class BookmarksViewModelTests
{
    [Fact]
    public void RepeatedSaveEditsOneBookmarkAndDeletionCanBeUndone()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var vm = new BookmarksViewModel(directory) { System = "Sol", Category = "Location" };
            vm.SaveCommand.Execute(null);
            vm.Notes = "Updated";
            vm.SaveCommand.Execute(null);
            Assert.Equal("Updated", Assert.Single(vm.Items).Notes);
            vm.DeleteCommand.Execute(null);
            Assert.Empty(vm.Items);
            vm.UndoDelete();
            Assert.Equal("Location", Assert.Single(new BookmarksViewModel(directory).Items).Category);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void HeadersSortTheSharedCatalogAscendingThenDescending()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var vm = new BookmarksViewModel(directory);
            vm.System = "Zulu";
            vm.SaveCommand.Execute(null);
            vm.NewCommand.Execute(null);
            vm.System = "Alpha";
            vm.SaveCommand.Execute(null);

            vm.SortCommand.Execute("System");
            Assert.Equal(["Alpha", "Zulu"], vm.Items.Select(item => item.System));
            Assert.Equal("↑", vm.SortIndicators["System"]);
            Assert.Equal(string.Empty, vm.SortIndicators["DisplayBody"]);
            vm.SortCommand.Execute("System");
            Assert.Equal(["Zulu", "Alpha"], vm.Items.Select(item => item.System));
            Assert.Equal("↓", vm.SortIndicators["System"]);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void OpeningSelectedSurfaceMiningBookmarkRequestsItsSurveyMap()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var opened = new List<Guid>();
            var vm = new BookmarksViewModel(directory, opened.Add);
            var id = Guid.NewGuid();
            var bookmark = new GalacticBookmark
            {
                Id = id,
                System = "LTT 4428",
                Body = "LTT 4428 E 5 a",
                Category = BookmarkCategoryCatalog.SurfaceMining,
                SurfaceMiningMap = new MineMapSurvey
                {
                    Id = id,
                    FrontierId = "F123",
                    SystemName = "LTT 4428",
                    SystemAddress = 42,
                    SystemPosition = new SrvSurvey.Core.Search.GalacticCoordinate(1, 2, 3),
                    BodyId = 5,
                    BodyName = "LTT 4428 E 5 a",
                    BodyType = "Rocky body",
                    LocationSignal = 4,
                    LocationRadiusMeters = 6_380,
                    PlanetRadiusMeters = 855_573,
                },
            };
            vm.AddMiningLocation(bookmark);
            vm.Selected = Assert.Single(vm.Items);

            Assert.True(vm.OpenInWorkspaceCommand.CanExecute(null));
            vm.OpenInWorkspaceCommand.Execute(null);
            vm.OpenInWorkspaceCommand.Execute(null);

            Assert.Equal([bookmark.Id, bookmark.Id], opened);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void ExportingSelectedSurfaceMiningBookmarkExcludesAdjacentLocations()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var vm = new BookmarksViewModel(directory);
            GalacticBookmark selected = CreateSurfaceMiningBookmark(Guid.NewGuid(), 4);
            GalacticBookmark adjacent = CreateSurfaceMiningBookmark(Guid.NewGuid(), 5);
            vm.AddMiningLocation(selected);
            vm.AddMiningLocation(adjacent);
            vm.Selected = vm.Items.Single(bookmark => bookmark.Id == selected.Id);

            GalacticBookmark exported = Assert.Single(BookmarkCatalog.Parse(vm.Export()));

            Assert.Equal(selected.Id, exported.Id);
            Assert.Equal(4, exported.SurfaceMiningMap?.LocationSignal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void CatalogChangesRehydrateEverySelectedEditorField()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var vm = new BookmarksViewModel(directory);
            vm.System = "Wille";
            vm.Minerals = "Ruby";
            vm.Hotspot = "Signal 4";
            vm.SaveCommand.Execute(null);
            GalacticBookmark original = Assert.Single(vm.Items);
            vm.Selected = original;

            vm.Catalog!.Save(original with { Minerals = "Gold", Hotspot = "Signal 5", Notes = "Updated externally" });

            Assert.Equal("Gold", vm.Minerals);
            Assert.Equal("Signal 5", vm.Hotspot);
            Assert.Equal("Updated externally", vm.Notes);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static GalacticBookmark CreateSurfaceMiningBookmark(Guid id, int signal) =>
        new()
        {
            Id = id,
            System = "LTT 4428",
            Body = "LTT 4428 E 5 a",
            Category = BookmarkCategoryCatalog.SurfaceMining,
            SurfaceMiningMap = new MineMapSurvey
            {
                Id = id,
                FrontierId = "F123",
                SystemName = "LTT 4428",
                SystemAddress = 42,
                SystemPosition = new SrvSurvey.Core.Search.GalacticCoordinate(1, 2, 3),
                BodyId = 5,
                BodyName = "LTT 4428 E 5 a",
                BodyType = "Rocky body",
                LocationSignal = signal,
                LocationRadiusMeters = 6_380,
                PlanetRadiusMeters = 855_573,
            },
        };
}
