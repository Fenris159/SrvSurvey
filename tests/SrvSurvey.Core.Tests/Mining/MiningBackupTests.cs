using System.Text.Json;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class MiningBackupTests
{
    [Fact]
    public void ArchiveCarriesBookmarksAndScreenshotsWithoutDependingOnOriginalPath()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        try
        {
            var image = Path.Combine(root, "capture.png");
            File.WriteAllBytes(image, [137, 80, 78, 71, 13, 10, 26, 10]);
            var state = new MiningCommanderData();
            state.History.Add(new MiningSession { Screenshots = [image] });
            var archive = MiningBackup.Create(state, JsonSerializer.Serialize(new[] { new GalacticBookmark { System = "Sol", Screenshots = [image] } }));
            File.Delete(image);
            var restored = MiningBackup.Read(archive, Path.Combine(root, "restored"));
            Assert.True(File.Exists(Assert.Single(BookmarkCatalog.Parse(restored.Bookmarks)).Screenshots[0]));
            Assert.True(File.Exists(restored.Data.History[0].Screenshots[0]));
            Assert.NotEqual(image, restored.Data.History[0].Screenshots[0]);
        }
        finally { Directory.Delete(root, true); }
    }
}
