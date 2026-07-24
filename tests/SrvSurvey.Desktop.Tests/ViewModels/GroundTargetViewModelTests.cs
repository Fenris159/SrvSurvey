using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class GroundTargetViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-ground-target-vm-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadsGuidesPastesAndClearsLegacyGroundTarget()
    {
        var store = new GroundTargetSettingsStore(temporaryDirectory);
        await store.SaveAsync(
            new GroundTargetSnapshot(true, new SurfaceCoordinate(0, 1)));
        var viewModel = new GroundTargetViewModel(store);

        Assert.True(viewModel.IsTargetActive);
        Assert.Equal("ACTIVE", viewModel.TargetStatusLabel);
        Assert.Equal("0", viewModel.TargetLatitude);
        Assert.Equal("1", viewModel.TargetLongitude);

        viewModel.UpdateStatus(new EliteStatus
        {
            Flags = StatusFlags.HasLatLong,
            Latitude = 0,
            Longitude = 0,
            PlanetRadius = 1_000,
            Heading = 45,
            Altitude = 17.4532925,
        });

        Assert.Equal("17 m", viewModel.DistanceToTarget);
        Assert.Equal("90°", viewModel.TargetBearing);
        Assert.Equal("45° relative", viewModel.RelativeHeading);
        Assert.Equal("45°", viewModel.ApproachAngle);
        Assert.Equal("Ideal approach", viewModel.ApproachStatus);

        await viewModel.ApplyPastedTextAsync("12.5°N 45.25°W");

        Assert.Equal("12.5", viewModel.TargetLatitude);
        Assert.Equal("-45.25", viewModel.TargetLongitude);
        var saved = store.Load();
        Assert.Equal(
            new SurfaceCoordinate(12.5, -45.25),
            saved.Snapshot!.Target);

        await viewModel.ClearTargetAsync();

        Assert.False(viewModel.IsTargetActive);
        Assert.Equal("INACTIVE", viewModel.TargetStatusLabel);
        Assert.Equal("0", viewModel.TargetLatitude);
        Assert.Equal("0", viewModel.TargetLongitude);
        Assert.False(store.Load().Snapshot!.IsActive);
    }

    [Fact]
    public async Task InvalidInputDoesNotReplaceSavedTarget()
    {
        var store = new GroundTargetSettingsStore(temporaryDirectory);
        await store.SaveAsync(
            new GroundTargetSnapshot(true, new SurfaceCoordinate(1, 2)));
        var viewModel = new GroundTargetViewModel(store)
        {
            TargetLatitude = "91",
            TargetLongitude = "2",
        };

        await viewModel.SetTargetAsync();

        Assert.Contains("Latitude", viewModel.StatusMessage);
        Assert.Equal(new SurfaceCoordinate(1, 2), store.Load().Snapshot!.Target);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
