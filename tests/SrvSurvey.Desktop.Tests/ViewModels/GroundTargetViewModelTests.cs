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
        Assert.Equal("-45°", viewModel.DescentAngle);
        Assert.Equal("0.000000, 1.000000", viewModel.TargetCoordinates);
        Assert.Equal(45, viewModel.RelativeBearingDegrees, 6);
        Assert.Equal(45, viewModel.AttackAngleDegrees, 3);
        Assert.Equal("Ideal approach", viewModel.ApproachStatus);
        Assert.True(viewModel.HasIdealApproach);
        Assert.False(viewModel.ShouldShow);

        viewModel.UpdateStatus(new EliteStatus
        {
            Flags = StatusFlags.HasLatLong | StatusFlags.InMainShip,
            Latitude = 0,
            Longitude = 0,
            PlanetRadius = 1_000,
            Heading = 45,
            Altitude = 17.4532925,
        });

        Assert.True(viewModel.ShouldShow);

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
        Assert.False(viewModel.ShouldShow);
    }

    [Theory]
    [InlineData(StatusFlags.InMainShip, StatusFlags2.None, GuiFocus.NoFocus, true)]
    [InlineData(StatusFlags.Supercruise, StatusFlags2.None, GuiFocus.NoFocus, true)]
    [InlineData(StatusFlags.InFighter, StatusFlags2.None, GuiFocus.NoFocus, true)]
    [InlineData(StatusFlags.InSrv, StatusFlags2.None, GuiFocus.NoFocus, true)]
    [InlineData(StatusFlags.None, StatusFlags2.OnFoot | StatusFlags2.OnFootOnPlanet, GuiFocus.NoFocus, true)]
    [InlineData(StatusFlags.None, StatusFlags2.OnFoot | StatusFlags2.OnFootInStation, GuiFocus.NoFocus, false)]
    [InlineData(StatusFlags.InMainShip, StatusFlags2.GlideMode, GuiFocus.NoFocus, true)]
    [InlineData(StatusFlags.InMainShip, StatusFlags2.None, GuiFocus.CommsPanel, true)]
    [InlineData(StatusFlags.InMainShip, StatusFlags2.None, GuiFocus.RolePanel, false)]
    [InlineData(StatusFlags.InMainShip, StatusFlags2.InTaxi, GuiFocus.NoFocus, false)]
    public async Task OverlayVisibilityMatchesLegacyModes(
        StatusFlags flags,
        StatusFlags2 flags2,
        GuiFocus focus,
        bool expected)
    {
        var store = new GroundTargetSettingsStore(temporaryDirectory);
        await store.SaveAsync(
            new GroundTargetSnapshot(true, new SurfaceCoordinate(0, 1)));
        var viewModel = new GroundTargetViewModel(store);

        viewModel.UpdateStatus(new EliteStatus
        {
            Flags = flags | StatusFlags.HasLatLong,
            Flags2 = flags2,
            GuiFocus = focus,
            PlanetRadius = 1_000,
        });

        Assert.Equal(expected, viewModel.ShouldShow);
    }

    [Fact]
    public async Task OverlayRequiresCoordinatesAndPositiveBodyRadius()
    {
        var store = new GroundTargetSettingsStore(temporaryDirectory);
        await store.SaveAsync(
            new GroundTargetSnapshot(true, new SurfaceCoordinate(0, 1)));
        var viewModel = new GroundTargetViewModel(store);

        viewModel.UpdateStatus(new EliteStatus
        {
            Flags = StatusFlags.InMainShip,
            PlanetRadius = 1_000,
        });
        Assert.False(viewModel.ShouldShow);

        viewModel.UpdateStatus(new EliteStatus
        {
            Flags = StatusFlags.InMainShip | StatusFlags.HasLatLong,
            PlanetRadius = 0,
        });
        Assert.False(viewModel.ShouldShow);
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
