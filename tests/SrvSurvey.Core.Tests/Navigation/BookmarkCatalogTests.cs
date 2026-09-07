using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Tests.Navigation;

public sealed class BookmarkCatalogTests
{
    [Fact]
    public void RestoreRecoversBackedUpEditsAndRetainsPreviousCatalogOnDisk()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var catalog = new BookmarkCatalog(directory);
            var bookmark = new GalacticBookmark { System = "Sol", Notes = "Original" };
            catalog.Save(bookmark); var backup = catalog.Export();
            catalog.Save(bookmark with { Notes = "Edited" });
            catalog.Restore(backup);
            Assert.Equal("Original", Assert.Single(catalog.Items).Notes);
            Assert.Contains("Edited", File.ReadAllText(Path.Combine(directory, "bookmarks.json.before-restore")));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void ImportsReferenceBookmarksAndRejectsNonObjectsWithoutChangingCatalog()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var catalog = new BookmarkCatalog(directory);
            catalog.Import("""[{"system":"Sol","body":"Earth A Ring","materials":"Platinum","rating":"4","overlap_type":"2x","notes":"Test"}]""");
            Assert.Equal("2x", Assert.Single(catalog.Items).Overlaps);
            Assert.ThrowsAny<System.Text.Json.JsonException>(() => catalog.Import("[42]"));
            Assert.Single(catalog.Items);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void CategoriesSurviveRestartAndImportMergesWithoutDuplicatingLocations()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var catalog = new BookmarkCatalog(directory);
            catalog.Save(new GalacticBookmark { System = "Sol", Body = "Earth A Ring", Category = "Mining", Notes = "Test", Rating = 4 });
            catalog.Save(new GalacticBookmark { System = "Achenar", Category = "Home" });
            var backup = catalog.Export();
            var restored = new BookmarkCatalog(directory);
            restored.Import(backup);
            Assert.Equal(2, restored.Items.Count);
            Assert.Single(restored.Filter("Mining", "earth"));
            Assert.Contains("Home", restored.Categories);
            Assert.Equal(4, restored.Filter("Mining", "")[0].Rating);
            Assert.ThrowsAny<System.Text.Json.JsonException>(() => restored.Import("not json"));
            Assert.Equal(2, new BookmarkCatalog(directory).Items.Count);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
