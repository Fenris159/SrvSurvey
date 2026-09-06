using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class SurfaceMiningViewModelTests : IDisposable
{
    private static readonly string[] ExpectedResourceNames = ["helium", "helium", "thortveitite"];
    private static readonly double[] ExpectedResourceDistances = [150, 2_350, 149];
    private static readonly bool[] ExpectedResourceNearStates = [false, false, true];
    private readonly string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-mining-{Guid.NewGuid():N}");
    private static SurfaceSurveySessionContext Session => new("F123", "Test", "Test", 42, null);

    [Fact]
    public async Task NamedResourcesTrackEveryLocationWithoutBiologyOrRigSlots()
    {
        using var mining = new SurfaceMiningViewModel(new SystemSurfaceStore(root));
        SurfaceRadarMarkerViewModel Resource(string name, double distance, double bearing = 20,
            SurfaceRadarMarkerKind kind = SurfaceRadarMarkerKind.Bookmark) => new()
            {
                Name = name,
                Kind = kind,
                DistanceMeters = distance,
                RelativeBearingDegrees = bearing,
                Location = new(0, distance / 1_000_000 * 180 / Math.PI),
                IsActive = false,
            };
        SurfaceRadarMarkerViewModel[] bookmarks = [
            Resource("thortveitite", 149), Resource("helium", 2_350), Resource("helium", 150),
            Resource("#1", 12), Resource("$Codex_Ent_Bacterial_Genus_Name;", 45),
            Resource("Bacterium", 45), Resource("organic", 50, kind: SurfaceRadarMarkerKind.ActiveSample),
        ];
        await mining.ApplyUpdateAsync(Session, Snapshot(), Status(), "mev_rhino", surfaceMarkers: bookmarks);
        Assert.Equal(ExpectedResourceNames, mining.Resources.Select(resource => resource.Name));
        Assert.Equal(ExpectedResourceDistances, mining.Resources.Select(resource => resource.Marker.DistanceMeters));
        Assert.Equal(ExpectedResourceNearStates, mining.Resources.Select(resource => resource.IsNear));
        Assert.All(mining.Rigs, rig => Assert.False(rig.IsSet));
        Assert.All(mining.Resources, resource =>
        {
            Assert.True(resource.Marker.IsActive);
            Assert.Equal(70, resource.Marker.RadiusMeters);
            Assert.Contains(resource.Marker, mining.RadarMarkers);
        });
        var original = mining.Resources;
        await mining.ApplyUpdateAsync(Session, Snapshot(), Status(), "mev_rhino", surfaceMarkers: bookmarks);
        Assert.Same(original, mining.Resources);

        await mining.ToggleRigAsync(1);
        Assert.Equal(3, mining.Resources.Count);
        Assert.True(mining.Rigs[0].IsSet);
        Assert.True(JournalEventEnvelope.TryParse("""{"event":"DockSRV"}""", out var dock, out _));
        await mining.ClearRigsOnShipBoardingAsync([dock!], Session.FrontierId);
        Assert.Equal(3, mining.Resources.Count);
        Assert.All(mining.Rigs, rig => Assert.False(rig.IsSet));

        await mining.ApplyUpdateAsync(Session, Snapshot(), Status(), "mev_rhino",
            surfaceMarkers: [Resource("helium", 10, 270)]);
        var updated = Assert.Single(mining.Resources);
        Assert.True(updated.IsNear);
        Assert.Equal("10 m", updated.DistanceText);
        Assert.Equal(270, updated.Bearing);

        await mining.ApplyUpdateAsync(null, Snapshot(), Status(), "mev_rhino", surfaceMarkers: bookmarks);
        Assert.Empty(mining.Resources);
        Assert.False(mining.HasResources);
    }

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
    [InlineData("mev_rhino", true, 0, true)]
    [InlineData("mev_rhino", true, 1, false)]
    [InlineData("mev_rhino", true, 2, false)]
    [InlineData("mev_rhino", true, 3, false)]
    [InlineData("mev_rhino", true, 4, false)]
    [InlineData("mev_rhino", true, 9, false)]
    [InlineData("mev_rhino", false, 0, false)]
    [InlineData("testbuggy", true, 0, false)]
    [InlineData(null, true, 0, false)]
    public async Task HudAnalysisRequiresRhinoCockpitWithoutAnOpenPanel(string? srv, bool inSrv,
        int focus, bool expected)
    {
        using var mining = new SurfaceMiningViewModel(new SystemSurfaceStore(root));
        var status = Status() with
        {
            Flags = StatusFlags.HasLatLong | (inSrv ? StatusFlags.InSrv : StatusFlags.None),
            GuiFocus = (GuiFocus)focus,
        };
        await mining.ApplyUpdateAsync(Session, Snapshot(), status, srv);
        Assert.Equal(expected, mining.CanDetectRigs);
        await mining.ApplyUpdateAsync(null, Snapshot(), status, srv);
        Assert.False(mining.CanDetectRigs);
    }

    [Fact]
    public async Task DisembarkedRhinoRemainsTrackableAndRigPlacementRequiresBeingAboard()
    {
        using var mining = new SurfaceMiningViewModel(new SystemSurfaceStore(root));
        await mining.ApplyUpdateAsync(Session, Snapshot(), Status(), "mev_rhino");
        await mining.ToggleRigAsync(1);
        Assert.False(mining.HasRhinoTracker);
        var parked = new SurfaceRadarMarkerViewModel
        {
            Kind = SurfaceRadarMarkerKind.Srv,
            Name = "SRV",
            Location = new(0, 0),
            DistanceMeters = 100,
            RelativeBearingDegrees = 270,
        };
        var onFoot = Status() with
        {
            Flags = StatusFlags.HasLatLong,
            Flags2 = StatusFlags2.OnFoot | StatusFlags2.OnFootOnPlanet,
        };
        await mining.ApplyUpdateAsync(Session, Snapshot(), onFoot, null, [parked], parkedSrvType: "mev_rhino");
        Assert.True(mining.ShouldShow);
        Assert.True(mining.HasRhinoTracker);
        Assert.Equal(270, mining.RhinoBearing);
        Assert.Same(parked, mining.RhinoMarker);
        Assert.Contains(parked, mining.RadarMarkers);
        // On foot the observer is the player, without the 4 m cockpit offset.
        Assert.InRange(mining.Rigs[0].Marker!.DistanceMeters, 6.99, 7.01);
        Assert.False(await mining.ToggleRigAsync(1));
        Assert.True(mining.Rigs[0].IsSet);

        await mining.ApplyUpdateAsync(Session, Snapshot(), Status(), "mev_rhino", [parked]);
        Assert.True(mining.ShouldShow);
        Assert.False(mining.HasRhinoTracker);
        Assert.DoesNotContain(parked, mining.RadarMarkers);
        await mining.ApplyUpdateAsync(Session, Snapshot(), onFoot, null, [parked], parkedSrvType: "testbuggy");
        Assert.False(mining.ShouldShow);
        await mining.ApplyUpdateAsync(Session, Snapshot(), onFoot, null, [], parkedSrvType: "mev_rhino");
        Assert.False(mining.ShouldShow);
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

    [Theory]
    [InlineData("""{"event":"Embark","SRV":false,"Taxi":false,"Multicrew":false}""", true)]
    [InlineData("""{"event":"DockSRV","SRVType":"mev_rhino"}""", true)]
    [InlineData("""{"event":"Embark","SRV":true}""", false)]
    [InlineData("""{"event":"Embark","SRV":false,"Taxi":true}""", false)]
    [InlineData("""{"event":"Embark","SRV":false,"Multicrew":true}""", false)]
    [InlineData("""{"event":"Embark"}""", false)]
    [InlineData("""{"event":"Disembark","SRV":true}""", false)]
    public async Task ReturningToOwnShipClearsEverySavedRig(string json, bool expectedCleared)
    {
        var store = new SystemSurfaceStore(root);
        using var mining = new SurfaceMiningViewModel(store);
        await mining.ApplyUpdateAsync(Session, Snapshot(), Status(), "mev_rhino");
        for (var number = 1; number <= 6; number++)
        {
            Assert.True(await mining.ToggleRigAsync(number));
        }

        Assert.True(JournalEventEnvelope.TryParse(json, out var boarding, out _));
        Assert.Equal(expectedCleared, await mining.ClearRigsOnShipBoardingAsync([boarding!], Session.FrontierId));
        // Re-entering the Rhino and reopening the store must not restore destroyed rigs.
        using var reopened = new SurfaceMiningViewModel(store);
        await reopened.ApplyUpdateAsync(Session, Snapshot(), Status(), "mev_rhino");
        Assert.Equal(expectedCleared ? 0 : 6, reopened.Rigs.Count(rig => rig.IsSet));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BoardingClearPreferencePersistsAndControlsRigCleanup(bool enabled)
    {
        var store = new SystemSurfaceStore(root);
        var settings = new SrvSurvey.Desktop.Configuration.SurfaceMiningSettingsStore(Path.Combine(root, "ui.json"));
        using (var mining = new SurfaceMiningViewModel(store, settings))
        {
            Assert.True(mining.AutoClearRigsOnShipBoarding);
            mining.AutoClearRigsOnShipBoarding = enabled;
        }

        using var reopened = new SurfaceMiningViewModel(store, settings);
        Assert.Equal(enabled, reopened.AutoClearRigsOnShipBoarding);
        await reopened.ApplyUpdateAsync(Session, Snapshot(), Status(), "mev_rhino");
        await reopened.ToggleRigAsync(1);
        Assert.True(JournalEventEnvelope.TryParse("""{"event":"DockSRV"}""", out var boarding, out _));
        Assert.Equal(enabled, await reopened.ClearRigsOnShipBoardingAsync([boarding!], Session.FrontierId));
        using var reloaded = new SurfaceMiningViewModel(store, settings);
        await reloaded.ApplyUpdateAsync(Session, Snapshot(), Status(), "mev_rhino");
        Assert.Equal(!enabled, reloaded.Rigs[0].IsSet);
    }

    [Theory]
    [InlineData("""{"event":"SendText","Message":"---"}""", true)]
    [InlineData("""{"event":"SendText","Message":"  ---  "}""", true)]
    [InlineData("""{"event":"SendText","Message":"--helium"}""", false)]
    [InlineData("""{"event":"SendText","Message":"----"}""", false)]
    [InlineData("""{"event":"ReceiveText","Message":"---"}""", false)]
    [InlineData("""{"event":"SendText","Message":123}""", false)]
    public async Task ChatClearIsExplicitAndIndependentOfBoardingPreference(string json, bool expectedCleared)
    {
        var store = new SystemSurfaceStore(root);
        using var mining = new SurfaceMiningViewModel(store) { AutoClearRigsOnShipBoarding = false };
        await mining.ApplyUpdateAsync(Session, Snapshot(), Status(), "mev_rhino");
        for (var number = 1; number <= 6; number++)
        {
            await mining.ToggleRigAsync(number);
        }

        Assert.True(JournalEventEnvelope.TryParse(json, out var command, out _));
        Assert.Equal(expectedCleared, await mining.ClearRigsFromChatAsync([command!], Session.FrontierId));
        using var reopened = new SurfaceMiningViewModel(store);
        await reopened.ApplyUpdateAsync(Session, Snapshot(), Status(), "mev_rhino");
        Assert.Equal(expectedCleared ? 0 : 6, reopened.Rigs.Count(rig => rig.IsSet));
    }

    [Fact]
    public async Task ChatClearCannotUseAnotherCommanderOrPreviousBodyContext()
    {
        var store = new SystemSurfaceStore(root);
        using var mining = new SurfaceMiningViewModel(store);
        await mining.ApplyUpdateAsync(Session, Snapshot(), Status(), "mev_rhino");
        await mining.ToggleRigAsync(1);
        Assert.True(JournalEventEnvelope.TryParse("""{"event":"SendText","Message":"---"}""", out var command, out _));
        Assert.False(await mining.ClearRigsFromChatAsync([command!], "F456"));
        Assert.True(mining.Rigs[0].IsSet);
        await mining.ApplyUpdateAsync(null, Snapshot(), new EliteStatus(), null);
        Assert.False(await mining.ClearRigsFromChatAsync([command!], Session.FrontierId));
        using var reopened = new SurfaceMiningViewModel(store);
        await reopened.ApplyUpdateAsync(Session, Snapshot(), Status(), "mev_rhino");
        Assert.True(reopened.Rigs[0].IsSet);
    }

    [Fact]
    public async Task ShipBoardingCannotClearAnotherCommandersRigLocations()
    {
        using var mining = new SurfaceMiningViewModel(new SystemSurfaceStore(root));
        await mining.ApplyUpdateAsync(Session, Snapshot(), Status(), "mev_rhino");
        await mining.ToggleRigAsync(1);
        Assert.True(JournalEventEnvelope.TryParse("""{"event":"DockSRV"}""", out var boarding, out _));
        Assert.False(await mining.ClearRigsOnShipBoardingAsync([boarding!], "F456"));
        Assert.True(mining.Rigs[0].IsSet);
    }

    [Fact]
    public async Task ShipBoardingClearsRigsWhenStatusLosesSurfaceContextFirst()
    {
        var store = new SystemSurfaceStore(root);
        using var mining = new SurfaceMiningViewModel(store);
        await mining.ApplyUpdateAsync(Session, Snapshot(), Status(), "mev_rhino");
        await mining.ToggleRigAsync(1);
        await mining.ApplyUpdateAsync(null, Snapshot(), new EliteStatus(), null);
        Assert.True(JournalEventEnvelope.TryParse("""{"event":"Embark","SRV":false}""", out var boarding, out _));
        Assert.True(await mining.ClearRigsOnShipBoardingAsync([boarding!], Session.FrontierId));
        using var reopened = new SurfaceMiningViewModel(store);
        await reopened.ApplyUpdateAsync(Session, Snapshot(), Status(), "mev_rhino");
        Assert.All(reopened.Rigs, rig => Assert.False(rig.IsSet));
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
