using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class SurfaceMiningViewModelTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-mining-{Guid.NewGuid():N}");
    private static SurfaceSurveySessionContext Session => new("F123", "Test", "Test", 42, null);

    [Fact]
    public async Task RigShortcutPersistsOffsetLocationAndTogglesOnlyItsSlot()
    {
        var store = new SystemSurfaceStore(root);
        using var mining = new SurfaceMiningViewModel(store);
        await mining.ApplyUpdateAsync(Session, Snapshot(), Status(), "mev_rhino");
        Assert.True(mining.ShouldShow);
        Assert.Equal(6, mining.Rigs.Count);
        Assert.All(mining.Rigs, rig => Assert.False(rig.IsSet));
        Assert.True(await mining.ToggleRigAsync(1));
        Assert.True(await mining.ToggleRigAsync(6));
        var rig = Assert.Single(mining.Rigs, rig => rig.Number == 1);
        Assert.True(rig.CanCollect);
        Assert.InRange(rig.Marker!.DistanceMeters, 2.99, 3.01);
        Assert.InRange(SurfaceNavigation.GetDistance(new(0, 0), rig.Marker.Location, 1_000_000), 6.99, 7.01);
        Assert.Equal(70, rig.Marker.RadiusMeters);
        var rows = mining.Rigs;
        await mining.ApplyUpdateAsync(Session, Snapshot(), Status(), "mev_rhino");
        Assert.Same(rows, mining.Rigs);

        using var reloaded = new SurfaceMiningViewModel(store);
        await reloaded.ApplyUpdateAsync(Session, Snapshot(), Status(), "mev_rhino");
        Assert.Equal(2, reloaded.Rigs.Count(rig => rig.IsSet));
        Assert.True(await reloaded.ToggleRigAsync(1));
        Assert.False(reloaded.Rigs[0].IsSet);
        Assert.True(reloaded.Rigs[5].IsSet);
        await reloaded.ApplyUpdateAsync(Session with { FrontierId = "F456" }, Snapshot(), Status(), "mev_rhino");
        Assert.All(reloaded.Rigs, rig => Assert.False(rig.IsSet));
    }

    [Theory]
    [InlineData("mev_rhino", true, true)]
    [InlineData("MEV_RHINO", true, true)]
    [InlineData("testbuggy", true, false)]
    [InlineData("mev_rhino", false, false)]
    [InlineData(null, true, false)]
    public async Task AutomaticVisibilityAndShortcutsRequireActiveRhino(string? srv, bool inSrv, bool expected)
    {
        using var mining = new SurfaceMiningViewModel(new SystemSurfaceStore(root));
        var status = Status() with { Flags = StatusFlags.HasLatLong | (inSrv ? StatusFlags.InSrv : StatusFlags.None) };
        await mining.ApplyUpdateAsync(Session, Snapshot(), status, srv);
        Assert.Equal(expected, mining.ShouldShow);
        Assert.Equal(expected, await mining.ToggleRigAsync(2));
        await mining.ApplyUpdateAsync(null, Snapshot(), status, srv);
        Assert.False(mining.ShouldShow);
        Assert.Empty(mining.RadarMarkers);
    }

    [Theory]
    [InlineData(4.9, "COLLECT")]
    [InlineData(5.1, "TOO CLOSE")]
    [InlineData(77.9, "TOO CLOSE")]
    [InlineData(78.1, "TRACKED")]
    public async Task RigProximityUsesVehicleCenterAndLegacyThresholds(double distance, string expected)
    {
        using var mining = new SurfaceMiningViewModel(new SystemSurfaceStore(root));
        await mining.ApplyUpdateAsync(Session, Snapshot(), Status(), "mev_rhino");
        await mining.ToggleRigAsync(1);
        // Rig is 7 m south of the cockpit; observer center is another 4 m behind its cockpit.
        var latitude = (distance - 3) / 1_000_000 * 180 / Math.PI;
        await mining.ApplyUpdateAsync(Session, Snapshot(), Status() with { Latitude = latitude }, "mev_rhino");
        Assert.Equal(expected, mining.Rigs[0].Status);
    }

    [Fact]
    public async Task CargoUsesSrvInventoryAndNeverSubstitutesShipCargo()
    {
        using var mining = new SurfaceMiningViewModel(new SystemSurfaceStore(root));
        var cargo = new CargoSnapshot(DateTimeOffset.UtcNow, "Cargo", "SRV", 36, []);
        await mining.ApplyUpdateAsync(Session, Snapshot(), Status() with { Cargo = 12 }, "mev_rhino", cargo: cargo);
        Assert.Equal("Cargo capacity: 36 of 72", mining.CargoText);
        await mining.ApplyUpdateAsync(Session, Snapshot(), Status() with { Cargo = 13 }, "mev_rhino",
            cargo: cargo with { Vessel = "Ship", Count = 400 });
        Assert.Equal(13, mining.CargoUsed);
    }

    private static EliteStatus Status() => new()
    {
        Flags = StatusFlags.InSrv | StatusFlags.HasLatLong,
        BodyName = "Test 1",
        PlanetRadius = 1_000_000,
    };

    private static SystemScanSnapshot Snapshot()
    {
        var state = new SystemScanState();
        foreach (var json in new[]
        {
            """{"event":"Location","StarSystem":"Test","SystemAddress":42}""",
            """{"event":"Scan","StarSystem":"Test","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Radius":1000000,"PlanetClass":"Rocky body"}""",
        })
        {
            Assert.True(JournalEventEnvelope.TryParse(json, out var envelope, out _));
            state.Apply(envelope!);
        }

        return state.CreateSnapshot();
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
