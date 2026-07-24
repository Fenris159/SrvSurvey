using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class SphereLimitViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-sphere-vm-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task SearchSelectsExactMatchAndEnablePersistsLegacyState()
    {
        var store = new CommanderProfileStore(temporaryDirectory);
        var resolver = new StubResolver(
        [
            new StarSystemReference(
                "Solati",
                1458376315610,
                new GalacticCoordinate(66.53125, 29.1875, 34.6875)),
            new StarSystemReference(
                "Sol",
                10477373803,
                new GalacticCoordinate(0, 0, 0)),
        ]);
        var viewModel = new SphereLimitViewModel(store, resolver);
        viewModel.LoadProfile(
            "F123",
            "Drew",
            true,
            SphereLimitSnapshot.Empty);
        viewModel.UpdateCurrentSystem(
            "Alpha Centauri",
            new GalacticCoordinate(3.03125, -0.09375, 3.15625));
        viewModel.Query = "Sol";

        await viewModel.SearchSystemsAsync();
        viewModel.Radius = "50";
        await viewModel.EnableAsync();

        Assert.Equal("Sol", viewModel.SelectedCenterSystem?.Name);
        Assert.True(viewModel.IsActive);
        Assert.Contains("inside", viewModel.CurrentSystemResult);
        var loaded = await store.LoadAsync("F123", true);
        Assert.Equal(
            new SphereLimitSnapshot(
                true,
                "Sol",
                new GalacticCoordinate(0, 0, 0),
                50),
            loaded.Data?.SphereLimit);
    }

    [Fact]
    public async Task DisableRetainsSavedCenterAndRadius()
    {
        var store = new CommanderProfileStore(temporaryDirectory);
        var snapshot = new SphereLimitSnapshot(
            true,
            "Sol",
            new GalacticCoordinate(0, 0, 0),
            250);
        var viewModel = new SphereLimitViewModel(store, new StubResolver([]));
        viewModel.LoadProfile("F123", "Drew", true, snapshot);

        await viewModel.DisableAsync();

        var loaded = await store.LoadAsync("F123", true);
        Assert.Equal(snapshot with { Active = false }, loaded.Data?.SphereLimit);
        Assert.False(viewModel.IsActive);
        Assert.Equal("250 ly around Sol", viewModel.LimitSummary);
    }

    [Fact]
    public async Task InvalidRadiusDoesNotChangeOrPersistState()
    {
        var store = new CommanderProfileStore(temporaryDirectory);
        var viewModel = new SphereLimitViewModel(
            store,
            new StubResolver(
            [
                new StarSystemReference(
                    "Sol",
                    10477373803,
                    new GalacticCoordinate(0, 0, 0)),
            ]));
        viewModel.LoadProfile("F123", "Drew", true, SphereLimitSnapshot.Empty);
        viewModel.Query = "Sol";
        await viewModel.SearchSystemsAsync();
        viewModel.Radius = "1001";

        await viewModel.EnableAsync();

        Assert.False(viewModel.IsActive);
        Assert.Contains("between", viewModel.StatusMessage);
        Assert.False(File.Exists(store.GetProfilePath("F123", true)));
    }

    [Fact]
    public async Task LookupFailureDoesNotReplaceLoadedConfiguration()
    {
        var snapshot = new SphereLimitSnapshot(
            true,
            "Sol",
            new GalacticCoordinate(0, 0, 0),
            100);
        var viewModel = new SphereLimitViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new StubResolver([], new HttpRequestException("offline")));
        viewModel.LoadProfile("F123", "Drew", true, snapshot);
        viewModel.Query = "Colonia";

        await viewModel.SearchSystemsAsync();

        Assert.True(viewModel.IsActive);
        Assert.Equal("100 ly around Sol", viewModel.LimitSummary);
        Assert.Contains("failed without changing", viewModel.StatusMessage);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private sealed class StubResolver(
        IReadOnlyList<StarSystemReference> results,
        Exception? exception = null) : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            return exception is null
                ? Task.FromResult(results)
                : Task.FromException<IReadOnlyList<StarSystemReference>>(exception);
        }
    }
}
