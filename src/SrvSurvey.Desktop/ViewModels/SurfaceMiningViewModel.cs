using System.ComponentModel;
using System.Text.Json;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class SurfaceMiningViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SystemSurfaceStore store;
    private readonly SemaphoreSlim updateLock = new(1, 1);
    private SystemSurfaceContext? context;
    private SystemSurfaceContext? lastMiningContext;
    private SystemSurfaceBodySnapshot? surface;
    private EliteStatus? status;
    private bool isRhino;
    private bool isRhinoParked;
    private bool disposed;
    private IReadOnlyList<SurfaceRadarMarkerViewModel> navigation = [];
    private double cargoUsed;

    public SurfaceMiningViewModel(SystemSurfaceStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<SurfaceRadarMarkerViewModel> RadarMarkers { get; private set; } = [];
    public IReadOnlyList<MiningRigViewModel> Rigs { get; private set; } = EmptyRigs();
    public IReadOnlyList<MiningResourceViewModel> Resources { get; private set; } = [];
    public bool HasResources => Resources.Count > 0;
    public IReadOnlyList<SurfaceRadarMarkerViewModel> ShipMarkers { get; private set; } = [];
    public SurfaceRadarMarkerViewModel? RhinoMarker { get; private set; }
    public bool HasRhinoTracker => RhinoMarker is not null;
    public double RhinoBearing => RhinoMarker?.RelativeBearingDegrees ?? 0;
    public string RhinoDistanceText => RhinoMarker?.DistanceText ?? "—";
    public bool HasShipTracker => ShipMarkers.Count > 0;
    public double ShipBearing => HasShipTracker ? ShipMarkers[0].RelativeBearingDegrees : 0;
    public string ShipDistanceText => HasShipTracker ? ShipMarkers[0].DistanceText : "—";
    public string BodyName => context?.BodyName ?? "Current body";
    public string HeadingText => $"HEADING {status?.NormalizedHeading ?? 0:000}°";
    public string HistoryText => $"{Rigs.Count(rig => rig.IsSet)} of 6 rigs tracked";
    public double CargoUsed => cargoUsed;
    public string CargoText => $"Cargo capacity: {CargoUsed:N0} of 72";
    public string StatusText { get; private set; } = "Waiting for a Rhino on a planetary surface.";
    public bool ShouldShow => !disposed && context is not null
        && (isRhino && status?.InSrv == true
            || isRhinoParked && status?.OnFoot == true
                && navigation.Any(marker => marker.Kind == SurfaceRadarMarkerKind.Srv))
        && status is { HasLatitudeLongitude: true, PlanetRadius: > 0 }
        && !status.Docked && !status.InTaxi && !status.FsdChargingJump && TryGetPosition(out _);

    public async Task ApplyUpdateAsync(SurfaceSurveySessionContext? session,
        SystemScanSnapshot snapshot, EliteStatus? currentStatus, string? srvType,
        IReadOnlyList<SurfaceRadarMarkerViewModel>? surfaceMarkers = null,
        CargoSnapshot? cargo = null,
        string? parkedSrvType = null)
    {
        await updateLock.WaitAsync().ConfigureAwait(true);
        try
        {
            status = currentStatus;
            var count = cargo is not null && string.Equals(cargo.Vessel, "SRV", StringComparison.OrdinalIgnoreCase)
                ? cargo.Count : status?.Cargo ?? 0;
            cargoUsed = double.IsFinite(count) ? Math.Max(0, count) : 0;
            navigation = surfaceMarkers ?? [];
            isRhino = string.Equals(srvType, "mev_rhino", StringComparison.OrdinalIgnoreCase);
            isRhinoParked = string.Equals(parkedSrvType, "mev_rhino", StringComparison.OrdinalIgnoreCase);
            var body = snapshot.Bodies.FirstOrDefault(candidate => string.Equals(
                candidate.Name, status?.BodyName, StringComparison.OrdinalIgnoreCase));
            var next = session is not null && body is not null && status?.PlanetRadius is > 0
                ? new SystemSurfaceContext(session.FrontierId, session.CommanderName,
                    session.SystemName, session.SystemAddress, session.StarPosition,
                    body.BodyId, body.Name, (double)status.PlanetRadius)
                : null;
            if (context != next)
            {
                context = next;
                surface = null;
                if (context is not null)
                {
                    var result = await store.LoadBodyAsync(context).ConfigureAwait(true);
                    surface = result.Snapshot;
                    StatusText = result.Error ?? "Rig locations are saved for this commander and body.";
                }
            }

            if (context is not null && (isRhino || isRhinoParked))
            {
                lastMiningContext = context;
            }

            Recalculate();
        }
        finally
        {
            updateLock.Release();
        }
    }

    public async Task<bool> ToggleRigAsync(int number)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, 6);
        await updateLock.WaitAsync().ConfigureAwait(true);
        try
        {
            if (!ShouldShow || !isRhino || status?.InSrv != true || !TryGetPosition(out var cockpit))
            {
                return false;
            }

            var location = SurfaceMiningGeometry.DeployedRig(cockpit,
                status.NormalizedHeading, context!.RadiusMeters);
            var result = await store.ToggleBookmarkGroupAsync(context, $"#{number}", location)
                .ConfigureAwait(true);
            surface = (await store.LoadBodyAsync(context).ConfigureAwait(true)).Snapshot;
            StatusText = result.Mutation == SurfaceBookmarkMutation.Added
                ? $"Rig {number} location saved." : $"Rig {number} location cleared.";
            Recalculate();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidDataException or InvalidOperationException)
        {
            StatusText = "Rig location could not be saved: " + exception.Message;
            Notify();
            return false;
        }
        finally
        {
            updateLock.Release();
        }
    }

    public async Task<bool> ClearRigsOnShipBoardingAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        string? frontierId)
    {
        if (!journalEvents.Any(IsOwnShipBoarding))
        {
            return false;
        }

        await updateLock.WaitAsync().ConfigureAwait(true);
        try
        {
            var miningContext = lastMiningContext ?? context;
            if (miningContext is null || !string.Equals(miningContext.FrontierId, frontierId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            await store.ClearBookmarksAsync(miningContext).ConfigureAwait(true);
            if (context == miningContext)
            {
                surface = (await store.LoadBodyAsync(miningContext).ConfigureAwait(true)).Snapshot;
            }

            lastMiningContext = null;
            StatusText = "Rig locations cleared after returning to your ship.";
            Recalculate();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidDataException or InvalidOperationException)
        {
            StatusText = "Rig locations could not be cleared: " + exception.Message;
            Notify();
            return false;
        }
        finally
        {
            updateLock.Release();
        }
    }

    private static bool IsOwnShipBoarding(JournalEventEnvelope journalEvent)
    {
        var entry = journalEvent.Payload;
        if (entry.TryGetProperty("Taxi", out var taxi) && taxi.ValueKind != JsonValueKind.False
            || entry.TryGetProperty("Multicrew", out var multicrew) && multicrew.ValueKind != JsonValueKind.False)
        {
            return false;
        }

        return journalEvent.EventName == "DockSRV"
            || journalEvent.EventName == "Embark"
                && entry.TryGetProperty("SRV", out var srv) && srv.ValueKind == JsonValueKind.False;
    }

    private void Recalculate()
    {
        var markers = new List<SurfaceRadarMarkerViewModel>();
        var rigs = new List<MiningRigViewModel>();
        var validPosition = ShouldShow && TryGetPosition(out _);
        for (var number = 1; number <= 6; number++)
        {
            var marker = validPosition ? CreateRigMarker(number) : null;
            if (marker is not null)
            {
                markers.Add(marker);
            }

            rigs.Add(new MiningRigViewModel(number, marker));
        }

        var resources = validPosition ? CreateResourceMarkers() : [];
        if (!SameMarkers(Resources.Select(resource => resource.Marker).ToArray(), resources))
        {
            Resources = resources.Select(marker => new MiningResourceViewModel(marker)).ToArray();
        }
        markers.AddRange(Resources.Select(resource => resource.Marker));

        var ships = validPosition ? navigation.Where(marker => marker.Kind is
            SurfaceRadarMarkerKind.Ship or SurfaceRadarMarkerKind.FormerShip).ToArray() : [];
        if (!SameMarkers(ShipMarkers, ships))
        {
            ShipMarkers = ships;
        }

        markers.AddRange(ShipMarkers);
        RhinoMarker = validPosition && status?.OnFoot == true && isRhinoParked
            ? navigation.FirstOrDefault(marker => marker.Kind == SurfaceRadarMarkerKind.Srv)
            : null;
        if (RhinoMarker is not null)
        {
            markers.Add(RhinoMarker);
        }
        if (!SameMarkers(RadarMarkers, markers))
        {
            RadarMarkers = markers;
            Rigs = rigs;
        }

        Notify();
    }

    private SurfaceRadarMarkerViewModel? CreateRigMarker(int number)
    {
        if (surface is null || !surface.Bookmarks.TryGetValue($"#{number}", out var locations)
            || locations.Count == 0 || !TryGetPosition(out var cockpit))
        {
            return null;
        }

        var current = status!.InSrv
            ? SurfaceMiningGeometry.VehicleCenter(cockpit, status.NormalizedHeading, context!.RadiusMeters)
            : cockpit;
        var location = locations[0];
        var distance = SurfaceNavigation.GetDistance(current, location, context!.RadiusMeters);
        var bearing = SurfaceNavigation.GetBearing(current, location);
        var proximity = distance < SurfaceMiningGeometry.ExclusionDistanceMeters ? "TOO CLOSE" : "TRACKED";
        if (distance < SurfaceMiningGeometry.PickupDistanceMeters)
        {
            proximity = "COLLECT";
        }

        return new SurfaceRadarMarkerViewModel
        {
            Name = $"Rig {number}",
            Kind = SurfaceRadarMarkerKind.MiningRig,
            Location = location,
            DistanceMeters = distance,
            BearingDegrees = bearing,
            RelativeBearingDegrees = SurfaceNavigation.NormalizeDegrees(bearing - status.NormalizedHeading),
            RadiusMeters = SurfaceMiningGeometry.RigRadiusMeters,
            IsInsideRadius = distance < SurfaceMiningGeometry.ExclusionDistanceMeters,
            Status = proximity,
        };
    }

    private SurfaceRadarMarkerViewModel[] CreateResourceMarkers() => navigation
            .Where(marker => marker.IsBookmark && !marker.Name.StartsWith('#')
                // Legacy treats named bookmarks without a biology sample range as ground resources.
                && ExobiologyReferenceCatalog.GetSampleDistanceMeters(marker.Name) == 50)
            .OrderBy(marker => marker.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(marker => marker.DistanceMeters)
            .Select(marker => new SurfaceRadarMarkerViewModel
            {
                Name = marker.Name,
                Kind = SurfaceRadarMarkerKind.Bookmark,
                Location = marker.Location,
                DistanceMeters = marker.DistanceMeters,
                BearingDegrees = marker.BearingDegrees,
                RelativeBearingDegrees = marker.RelativeBearingDegrees,
                RadiusMeters = SurfaceMiningGeometry.RigRadiusMeters,
                IsInsideRadius = marker.DistanceMeters < SurfaceMiningGeometry.RigRadiusMeters,
            }).ToArray();

    private static bool SameMarkers(IReadOnlyList<SurfaceRadarMarkerViewModel> first,
        IReadOnlyList<SurfaceRadarMarkerViewModel> second) => first.Count == second.Count
        && first.Zip(second).All(pair => pair.First.Name == pair.Second.Name
            && pair.First.Kind == pair.Second.Kind && pair.First.Status == pair.Second.Status
            && pair.First.Location == pair.Second.Location
            && Math.Abs(pair.First.DistanceMeters - pair.Second.DistanceMeters) < 0.000001
            && Math.Abs(pair.First.RelativeBearingDegrees - pair.Second.RelativeBearingDegrees) < 0.000001);

    internal void InstallEditorPreview(IReadOnlyList<SurfaceRadarMarkerViewModel> markers,
        IReadOnlyList<SurfaceRadarMarkerViewModel>? resources = null)
    {
        context = new SystemSurfaceContext("preview", null, "Synuefe NL-N c23-4", 42, null,
            3, "Synuefe NL-N c23-4 B 3", 1_000_000);
        status = new EliteStatus { Heading = 74 };
        cargoUsed = 36;
        ShipMarkers = [new SurfaceRadarMarkerViewModel
        {
            Name = "Ship", Kind = SurfaceRadarMarkerKind.Ship, DistanceMeters = 250,
            RelativeBearingDegrees = 110, Location = new SurfaceCoordinate(0, 0),
        }];
        Resources = (resources ?? []).Select(marker => new MiningResourceViewModel(marker)).ToArray();
        RadarMarkers = [.. markers, .. ShipMarkers, .. Resources.Select(resource => resource.Marker)];
        Rigs = Enumerable.Range(1, 6).Select(number => new MiningRigViewModel(number,
            markers.ElementAtOrDefault(number - 1))).ToArray();
        StatusText = "Rig locations · cyan: collect · red: too close to deploy";
        Notify();
    }

    private bool TryGetPosition(out SurfaceCoordinate location)
    {
        location = default;
        if (status is not { HasLatitudeLongitude: true } || !double.IsFinite(status.Latitude)
            || !double.IsFinite(status.Longitude)
            || status.Latitude is < -90 or > 90 || status.Longitude is < -180 or > 180)
        {
            return false;
        }

        location = new SurfaceCoordinate(status.Latitude, status.Longitude);
        return true;
    }

    private static MiningRigViewModel[] EmptyRigs() => Enumerable.Range(1, 6)
        .Select(number => new MiningRigViewModel(number, null)).ToArray();

    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    public void Dispose()
    {
        disposed = true;
        updateLock.Dispose();
    }
}

public sealed record MiningResourceViewModel(SurfaceRadarMarkerViewModel Marker)
{
    public string Name => Marker.Name;
    public string DistanceText => Marker.DistanceText;
    public double Bearing => Marker.RelativeBearingDegrees;
    public bool IsNear => Marker.DistanceMeters < 150;
}

public sealed record MiningRigViewModel(int Number, SurfaceRadarMarkerViewModel? Marker)
{
    public string Name => $"Rig {Number}";
    public bool IsSet => Marker is not null;
    public bool CanCollect => Marker?.Status == "COLLECT";
    public bool IsTooClose => Marker?.Status == "TOO CLOSE";
    public string Status => Marker?.Status ?? "NOT SET";
    public string DistanceText => Marker?.DistanceText ?? "—";
    public double Bearing => Marker?.RelativeBearingDegrees ?? 0;
}
