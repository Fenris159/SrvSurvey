using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class BoxelSearchLibraryViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-BoxelLibraryTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SelectingAnotherSearchClearsThePreviousSelection()
    {
        var store = new SavedBoxelSearchStore(temporaryDirectory);
        var top = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        var snapshot = new BoxelSearchSnapshot
        {
            Active = true,
            TopBoxel = top,
            Current = top,
            CurrentCount = 2,
            ProgressByPrefix = new Dictionary<string, int>
            {
                [top.Prefix] = 2,
            },
        };
        await store.CreateAsync("F123", "First", null, snapshot);
        await store.CreateAsync("F123", "Second", null, snapshot);
        var boxel = new BoxelSearchViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new EmptyResolver(),
            savedSearchStore: store);
        await boxel.LoadProfileAsync("F123", "Drew", true, BoxelSearchSnapshot.Empty);
        var library = new BoxelSearchLibraryViewModel(boxel);
        await library.RefreshAsync();

        library.Searches[0].IsSelected = true;
        library.Searches[1].IsSelected = true;

        Assert.False(library.Searches[0].IsSelected);
        Assert.True(library.Searches[1].IsSelected);
        Assert.Same(library.Searches[1], library.SelectedSearch);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private sealed class EmptyResolver : IBoxelSystemResolver
    {
        public Task<IReadOnlyList<BoxelSystemObservation>> SearchAsync(
            BoxelAddress boxel,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<BoxelSystemObservation>>([]);
        }
    }
}
