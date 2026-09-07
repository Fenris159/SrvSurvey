using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;
namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class MiningSearchViewModelTests
{
    [Fact]
    public async Task PlainBookmarkDoesNotEraseKnownOverlapOrResAnnotations()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var bookmarks = new BookmarksViewModel(directory);
            bookmarks.AddMiningLocation(new GalacticBookmark { System = "Review Test", Body = "Review Test A Ring" });
            var ring = new MiningRing { System = "Review Test", Body = "Review Test A Ring", Position = new GalacticCoordinate(0, 0, 0), Hotspots = new() { ["Platinum"] = 2 }, Overlaps = "Platinum x2", ResourceExtractionSites = "High" };
            using var model = new MiningSearchViewModel(new MiningSearchClient(), bookmarks, _ => { }, () => [ring], new Resolver()) { Source = "Local", Reference = ring.System, Radius = 1, OnlyOverlaps = true, OnlyRes = true };
            await model.SearchRingsAsync();
            Assert.Contains(model.Rings, r => r.System == ring.System && r.Overlaps == ring.Overlaps && r.ResourceExtractionSites == "High");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void SearchPreferencesRoundTripAndResetForAnotherCommander()
    {
        using var model = new MiningSearchViewModel(new MiningSearchClient(), new BookmarksViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())), _ => { }, () => [], new Resolver());
        model.Reference = "Sol"; model.Radius = 240; model.OnlyRes = true; model.TraderType = "Encoded";
        var store = new MiningStore("unused");
        var restored = MiningStore.Parse(store.Export(new MiningCommanderData { Settings = new MiningPreferences { SearchOptions = model.SaveOptions() } }));
        model.LoadOptions(new());
        Assert.Equal("", model.Reference); Assert.Equal(100, model.Radius); Assert.False(model.OnlyRes);
        model.LoadOptions(restored.Settings.SearchOptions);
        Assert.Equal("Sol", model.Reference); Assert.Equal(240, model.Radius); Assert.True(model.OnlyRes); Assert.Equal("Encoded", model.TraderType);
    }
    private sealed class Resolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(string query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
    }
}
