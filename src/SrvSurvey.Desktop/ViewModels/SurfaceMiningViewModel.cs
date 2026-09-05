using System.ComponentModel;
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
    private SystemSurfaceBodySnapshot? surface;
    private EliteStatus? status;
    private bool isRhino;
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
    public IReadOnlyList<SurfaceRadarMarkerViewModel> ShipMarkers { get; private set; } = [];
    public string BodyName => context?.BodyName ?? "Current body";
    public string HeadingText => $"HEADING {status?.NormalizedHeading ?? 0:000}°";
    public string HistoryText => $"{Rigs.Count(rig => rig.IsSet)} of 6 rigs tracked";
    public double CargoUsed => cargoUsed;
    public string CargoText => $"Cargo capacity: {CargoUsed:N0} of 72";
    public string StatusText { get; private set; } = "Waiting for a Rhino on a planetary surface.";
    public bool ShouldShow => !disposed && isRhino && context is not null
        && status is { InSrv: true, HasLatitudeLongitude: true, PlanetRadius: > 0 }
        && !status.Docked && !status.InTaxi && !status.FsdChargingJump && TryGetCockpit(out _);

    public async Task ApplyUpdateAsync(SurfaceSurveySessionContext? session,
        SystemScanSnapshot snapshot, EliteStatus? currentStatus, string? srvType,
        IReadOnlyList<SurfaceRadarMarkerViewModel>? navigationMarkers = null,
        CargoSnapshot? cargo = null)
    {
        await updateLock.WaitAsync().ConfigureAwait(true);
        try
        {
            status = currentStatus;
            var count = cargo is not null && string.Equals(cargo.Vessel, "SRV", StringComparison.OrdinalIgnoreCase)
                ? cargo.Count : status?.Cargo ?? 0;
            cargoUsed = double.IsFinite(count) ? Math.Max(0, count) : 0;
            navigation = navigationMarkers ?? [];
            isRhino = string.Equals(srvType, "mev_rhino", StringComparison.OrdinalIgnoreCase);
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
            if (!ShouldShow || !TryGetCockpit(out var cockpit))
            {
                return false;
            }

            var location = SurfaceMiningGeometry.DeployedRig(cockpit,
                status!.NormalizedHeading, context!.RadiusMeters);
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

    private void Recalculate()
    {
        var markers = new List<SurfaceRadarMarkerViewModel>();
        var rigs = new List<MiningRigViewModel>();
        var validPosition = ShouldShow && TryGetCockpit(out _);
        for (var number = 1; number <= 6; number++)
        {
            SurfaceRadarMarkerViewModel? marker = null;
            if (validPosition && surface?.Bookmarks.TryGetValue($"#{number}", out var locations) == true
                && locations.Count > 0 && TryGetCockpit(out var cockpit))
            {
                var current = SurfaceMiningGeometry.VehicleCenter(cockpit,
                    status!.NormalizedHeading, context!.RadiusMeters);
                var location = locations[0];
                var distance = SurfaceNavigation.GetDistance(current, location, context.RadiusMeters);
                var bearing = SurfaceNavigation.GetBearing(current, location);
                marker = new SurfaceRadarMarkerViewModel
                {
                    Name = $"Rig {number}",
                    Kind = SurfaceRadarMarkerKind.MiningRig,
                    Location = location,
                    DistanceMeters = distance,
                    BearingDegrees = bearing,
                    RelativeBearingDegrees = SurfaceNavigation.NormalizeDegrees(bearing - status.NormalizedHeading),
                    RadiusMeters = SurfaceMiningGeometry.RigRadiusMeters,
                    IsInsideRadius = distance < SurfaceMiningGeometry.ExclusionDistanceMeters,
                    Status = distance < SurfaceMiningGeometry.PickupDistanceMeters ? "COLLECT"
                        : distance < SurfaceMiningGeometry.ExclusionDistanceMeters ? "TOO CLOSE" : "TRACKED",
                };
                markers.Add(marker);
            }

            rigs.Add(new MiningRigViewModel(number, marker));
        }

        var ships = validPosition ? navigation.Where(marker => marker.Kind is
            SurfaceRadarMarkerKind.Ship or SurfaceRadarMarkerKind.FormerShip).ToArray() : [];
        if (!SameMarkers(ShipMarkers, ships))
        {
            ShipMarkers = ships;
        }

        markers.AddRange(ShipMarkers);
        if (!SameMarkers(RadarMarkers, markers))
        {
            RadarMarkers = markers;
            Rigs = rigs;
        }

        Notify();
    }

    private static bool SameMarkers(IReadOnlyList<SurfaceRadarMarkerViewModel> first,
        IReadOnlyList<SurfaceRadarMarkerViewModel> second) => first.Count == second.Count
        && first.Zip(second).All(pair => pair.First.Name == pair.Second.Name
            && pair.First.Kind == pair.Second.Kind && pair.First.Status == pair.Second.Status
            && pair.First.Location == pair.Second.Location
            && pair.First.DistanceMeters == pair.Second.DistanceMeters
            && pair.First.RelativeBearingDegrees == pair.Second.RelativeBearingDegrees);

    internal void InstallEditorPreview(IReadOnlyList<SurfaceRadarMarkerViewModel> markers)
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
        RadarMarkers = [.. markers, .. ShipMarkers];
        Rigs = Enumerable.Range(1, 6).Select(number => new MiningRigViewModel(number,
            markers.ElementAtOrDefault(number - 1))).ToArray();
        StatusText = "Rig locations · cyan: collect · red: too close to deploy";
        Notify();
    }

    private bool TryGetCockpit(out SurfaceCoordinate location)
    {
        location = default;
        if (status is not { HasLatitudeLongitude: true } || !double.IsFinite((double)status.Latitude)
            || !double.IsFinite((double)status.Longitude)
            || status.Latitude is < -90 or > 90 || status.Longitude is < -180 or > 180)
        {
            return false;
        }

        location = new SurfaceCoordinate((double)status.Latitude, (double)status.Longitude);
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
