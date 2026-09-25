using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed record SurfaceMiningMapPresentation(
    IReadOnlyList<SurfaceRadarMarkerViewModel>? Markers = null,
    MineMapSurvey? ActiveSurvey = null
);

public sealed class SurfaceMiningViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SystemSurfaceStore store;
    private readonly SurfaceMiningSettingsStore? settingsStore;
    private bool autoClearRigsOnShipBoarding;
    private readonly SemaphoreSlim updateLock = new(1, 1);
    private SystemSurfaceContext? context;
    private SystemSurfaceContext? lastMiningContext;
    private SystemSurfaceBodySnapshot? surface;
    private EliteStatus? status;
    private bool isRhino;
    private bool isRhinoParked;
    private bool disposed;
    private IReadOnlyList<SurfaceRadarMarkerViewModel> navigation = [];
    private MineMapSurvey? mineMapSurvey;
    private double cargoUsed;
    private readonly TimeProvider detectionTime;
    private SurfaceCoordinate? detectionPosition;
    private double detectionHeading;
    private long? detectionStillSince;

    public SurfaceMiningViewModel(
        SystemSurfaceStore store,
        SurfaceMiningSettingsStore? settingsStore = null,
        TimeProvider? detectionTimeProvider = null
    )
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.settingsStore = settingsStore;
        detectionTime = detectionTimeProvider ?? TimeProvider.System;
        Detection = new MiningDetectionViewModel(settingsStore, detectionTimeProvider);
        autoClearRigsOnShipBoarding = settingsStore?.LoadAutoClearRigsOnShipBoarding() ?? true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MiningDetectionViewModel Detection { get; }
    internal SystemSurfaceContext? DetectionContext => context;
    internal bool IsDetectionPositionSteady =>
        CanDetectRigs
        && detectionStillSince is { } since
        && detectionTime.GetElapsedTime(since, detectionTime.GetTimestamp()) >= TimeSpan.FromSeconds(1);
    internal const string DetectionMovementMessage =
        "Rhino moving — tracker changes paused until position and heading are steady for one second.";
    public bool ShouldShowRigWarning =>
        ShouldShow
        && isRhino
        && status?.InSrv == true
        && !status.OnFoot
        && Rigs.Any(rig => rig.Marker is { DistanceMeters: > SurfaceMiningGeometry.RigWarningDistanceMeters });

    // NoFocus also includes free head-look. This only permits analysis; the image detector
    // must independently locate the HUD before reporting a present or absent bar.
    public bool CanDetectRigs => ShouldShow && isRhino && status?.InSrv == true && status.GuiFocus == GuiFocus.NoFocus;

    public bool AutoClearRigsOnShipBoarding
    {
        get => autoClearRigsOnShipBoarding;
        set
        {
            if (autoClearRigsOnShipBoarding == value)
            {
                return;
            }

            try
            {
                settingsStore?.SaveAutoClearRigsOnShipBoarding(value);
                autoClearRigsOnShipBoarding = value;
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                StatusText = "Mining settings could not be saved: " + exception.Message;
            }

            Notify();
        }
    }

    public IReadOnlyList<SurfaceRadarMarkerViewModel> RadarMarkers { get; private set; } = [];
    public IReadOnlyList<SurfaceRadarPathViewModel> SplatBoundaries { get; private set; } = [];
    public IReadOnlyList<SurfaceRadarPointViewModel> SuggestedRigLocations { get; private set; } = [];
    public double RadarScale { get; private set; } = 1;
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
    public bool ShouldShow =>
        !disposed
        && context is not null
        && (
            isRhino && status?.InSrv == true
            || isRhinoParked
                && status?.OnFoot == true
                && navigation.Any(marker => marker.Kind == SurfaceRadarMarkerKind.Srv)
        )
        && status is { HasLatitudeLongitude: true, PlanetRadius: > 0 }
        && !status.Docked
        && !status.InTaxi
        && !status.FsdChargingJump
        && TryGetPosition(out _);

    public async Task ApplyUpdateAsync(
        SurfaceSurveySessionContext? session,
        SystemScanSnapshot snapshot,
        EliteStatus? currentStatus,
        string? srvType,
        SurfaceMiningMapPresentation? mapPresentation = null,
        CargoSnapshot? cargo = null,
        string? parkedSrvType = null
    )
    {
        await updateLock.WaitAsync().ConfigureAwait(true);
        try
        {
            status = currentStatus;
            double count =
                cargo is not null && string.Equals(cargo.Vessel, "SRV", StringComparison.OrdinalIgnoreCase)
                    ? cargo.Count
                    : status?.Cargo ?? 0;
            cargoUsed = double.IsFinite(count) ? Math.Max(0, count) : 0;
            navigation = mapPresentation?.Markers ?? [];
            mineMapSurvey = mapPresentation?.ActiveSurvey;
            isRhino = EliteSrvTypes.IsRhino(srvType);
            isRhinoParked = EliteSrvTypes.IsRhino(parkedSrvType);
            SystemScanBodySnapshot? body = snapshot.Bodies.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, status?.BodyName, StringComparison.OrdinalIgnoreCase)
            );
            SystemSurfaceContext? next =
                session is not null && body is not null && status?.PlanetRadius is > 0
                    ? new SystemSurfaceContext(
                        session.FrontierId,
                        session.CommanderName,
                        session.SystemName,
                        session.SystemAddress,
                        session.StarPosition,
                        body.BodyId,
                        body.Name,
                        (double)status.PlanetRadius
                    )
                    : null;
            if (context != next)
            {
                context = next;
                detectionPosition = null;
                detectionStillSince = null;
                surface = null;
                if (context is not null)
                {
                    SystemSurfaceLoadResult result = await store.LoadBodyAsync(context).ConfigureAwait(true);
                    surface = result.Snapshot;
                    StatusText = result.Error ?? "Rig locations are saved for this commander and body.";
                }
            }

            if (context is not null && (isRhino || isRhinoParked))
            {
                lastMiningContext = context;
            }

            UpdateDetectionMotion();
            Recalculate();
        }
        finally
        {
            updateLock.Release();
        }
    }

    public async Task ApplyMineMapSurveyAsync(MineMapSurvey? survey)
    {
        await updateLock.WaitAsync().ConfigureAwait(true);
        try
        {
            mineMapSurvey = survey;
            Recalculate();
        }
        finally
        {
            updateLock.Release();
        }
    }

    private void UpdateDetectionMotion()
    {
        if (!CanDetectRigs || !TryGetPosition(out SurfaceCoordinate position))
        {
            detectionPosition = null;
            detectionStillSince = null;
            return;
        }
        int heading = status!.NormalizedHeading;
        double turn = Math.Abs(heading - detectionHeading);
        turn = Math.Min(turn, 360 - turn);
        // Ignore sub-decimetre coordinate noise, but compare with the stationary origin
        // so a sequence of small steps still counts as driving.
        if (
            detectionPosition is { } origin
            && SurfaceNavigation.GetDistance(origin, position, context!.RadiusMeters) <= .1
            && turn <= .5
        )
        {
            return;
        }

        detectionPosition = position;
        detectionHeading = heading;
        detectionStillSince = detectionTime.GetTimestamp();
        if (Detection.Enabled && !Detection.IsCalibrating)
        {
            Detection.Pause(DetectionMovementMessage);
        }
    }

    public async Task<bool> ToggleRigAsync(int number)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, 6);
        await updateLock.WaitAsync().ConfigureAwait(true);
        try
        {
            if (!ShouldShow || !isRhino || status?.InSrv != true || !TryGetPosition(out SurfaceCoordinate cockpit))
            {
                return false;
            }

            SurfaceCoordinate location = SurfaceMiningGeometry.DeployedRig(
                cockpit,
                status.NormalizedHeading,
                context!.RadiusMeters
            );
            SurfaceBookmarkMutationResult result = await store
                .ToggleBookmarkGroupAsync(context, $"#{number}", location)
                .ConfigureAwait(true);
            surface = (await store.LoadBodyAsync(context).ConfigureAwait(true)).Snapshot;
            StatusText =
                result.Mutation == SurfaceBookmarkMutation.Added
                    ? $"Rig {number} location saved."
                    : $"Rig {number} location cleared.";
            Recalculate();
            return true;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or InvalidOperationException
            )
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

    internal async Task ApplyDetectedRigsAsync(
        IReadOnlyList<MiningBarState> states,
        SystemSurfaceContext expectedContext,
        MiningDetectionSettings expectedSettings
    )
    {
        await updateLock.WaitAsync().ConfigureAwait(true);
        try
        {
            if (
                context != expectedContext
                || !ReferenceEquals(Detection.Settings, expectedSettings)
                || !Detection.Enabled
                || Detection.IsCalibrating
                || !IsDetectionPositionSteady
                || !TryGetPosition(out SurfaceCoordinate cockpit)
            )
            {
                return;
            }

            if (
                !Enumerable
                    .Range(0, 6)
                    .Any(i =>
                        states[i] == MiningBarState.Present && !Rigs[i].IsSet
                        || states[i] == MiningBarState.Absent && Rigs[i].IsSet
                    )
            )
            {
                return;
            }
            // Refresh before mutations, including retries after a partially successful write.
            surface = (await store.LoadBodyAsync(context).ConfigureAwait(true)).Snapshot;
            SurfaceCoordinate location = SurfaceMiningGeometry.DeployedRig(
                cockpit,
                status!.NormalizedHeading,
                context.RadiusMeters
            );
            bool changed = await UpdateDetectedBookmarksAsync(states, location, context).ConfigureAwait(true);
            if (changed)
            {
                surface = (await store.LoadBodyAsync(context).ConfigureAwait(true)).Snapshot;
                StatusText = "Rig trackers updated from the Rhino HUD.";
            }
            Recalculate();
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or InvalidOperationException
            )
        {
            StatusText = "Rig trackers could not be updated: " + exception.Message;
            Notify();
        }
        finally
        {
            updateLock.Release();
        }
    }

    private async Task<bool> UpdateDetectedBookmarksAsync(
        IReadOnlyList<MiningBarState> states,
        SurfaceCoordinate location,
        SystemSurfaceContext miningContext
    )
    {
        bool changed = false;
        for (int i = 0; i < 6; i++)
        {
            string name = $"#{i + 1}";
            bool exists =
                surface?.Bookmarks.TryGetValue(name, out IReadOnlyList<SurfaceCoordinate>? locations) == true
                && locations.Count > 0;
            if (states[i] == MiningBarState.Present && !exists)
            {
                await store.AddBookmarkAsync(miningContext, name, location).ConfigureAwait(true);
                changed = true;
            }
            else if (states[i] == MiningBarState.Absent && exists)
            {
                await store.RemoveBookmarkGroupAsync(miningContext, name).ConfigureAwait(true);
                changed = true;
            }
        }
        return changed;
    }

    public Task<bool> ClearRigsOnShipBoardingAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        string? frontierId
    )
    {
        return AutoClearRigsOnShipBoarding && journalEvents.Any(IsOwnShipBoarding)
            ? ClearRigsAsync(frontierId, shipBoarding: true)
            : Task.FromResult(false);
    }

    public Task<bool> ClearRigsFromChatAsync(IReadOnlyList<JournalEventEnvelope> journalEvents, string? frontierId)
    {
        return journalEvents.Any(entry =>
            entry.EventName == "SendText"
            && entry.Payload.TryGetProperty("Message", out JsonElement message)
            && message.ValueKind == JsonValueKind.String
            && message.GetString()?.Trim() == "---"
        )
            ? ClearRigsAsync(frontierId, shipBoarding: false)
            : Task.FromResult(false);
    }

    private async Task<bool> ClearRigsAsync(string? frontierId, bool shipBoarding)
    {
        await updateLock.WaitAsync().ConfigureAwait(true);
        try
        {
            SystemSurfaceContext? miningContext = shipBoarding ? lastMiningContext ?? context : context;
            if (
                miningContext is null
                || !string.Equals(miningContext.FrontierId, frontierId, StringComparison.OrdinalIgnoreCase)
            )
            {
                return false;
            }

            await store.ClearBookmarksAsync(miningContext).ConfigureAwait(true);
            if (context == miningContext)
            {
                surface = (await store.LoadBodyAsync(miningContext).ConfigureAwait(true)).Snapshot;
            }

            if (lastMiningContext == miningContext)
            {
                lastMiningContext = null;
            }

            StatusText = shipBoarding
                ? "Rig locations cleared after returning to your ship."
                : "Rig locations cleared by the --- chat command.";
            Recalculate();
            return true;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or InvalidOperationException
            )
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
        JsonElement entry = journalEvent.Payload;
        if (
            entry.TryGetProperty("Taxi", out JsonElement taxi) && taxi.ValueKind != JsonValueKind.False
            || entry.TryGetProperty("Multicrew", out JsonElement multicrew)
                && multicrew.ValueKind != JsonValueKind.False
        )
        {
            return false;
        }

        return journalEvent.EventName == "DockSRV"
            || journalEvent.EventName == "Embark"
                && entry.TryGetProperty("SRV", out JsonElement srv)
                && srv.ValueKind == JsonValueKind.False;
    }

    private void Recalculate()
    {
        var markers = new List<SurfaceRadarMarkerViewModel>();
        bool validPosition = ShouldShow && TryGetPosition(out _);
        List<MiningRigViewModel> rigs = CreateRigs(validPosition, markers);

        SurfaceRadarMarkerViewModel[] resources = validPosition ? CreateResourceMarkers() : [];
        if (!SameMarkers(Resources.Select(resource => resource.Marker).ToArray(), resources))
        {
            Resources = resources.Select(marker => new MiningResourceViewModel(marker)).ToArray();
        }
        markers.AddRange(Resources.Select(resource => resource.Marker));

        SurfaceRadarMarkerViewModel[] ships = validPosition
            ? navigation
                .Where(marker => marker.Kind is SurfaceRadarMarkerKind.Ship or SurfaceRadarMarkerKind.FormerShip)
                .ToArray()
            : [];
        if (!SameMarkers(ShipMarkers, ships))
        {
            ShipMarkers = ships;
        }

        markers.AddRange(ShipMarkers);
        RhinoMarker =
            validPosition && status?.OnFoot == true && isRhinoParked
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

        (SplatBoundaries, SuggestedRigLocations) = validPosition ? CreateSplatPresentation() : ([], []);
        RadarScale = GetSplatRadarScale(SplatBoundaries, SuggestedRigLocations);

        Notify();
    }

    private List<MiningRigViewModel> CreateRigs(bool validPosition, List<SurfaceRadarMarkerViewModel> markers)
    {
        var rigs = new List<MiningRigViewModel>();
        for (int number = 1; number <= 6; number++)
        {
            SurfaceRadarMarkerViewModel? marker = validPosition ? CreateRigMarker(number) : null;
            if (marker is not null)
            {
                markers.Add(marker);
            }

            rigs.Add(new MiningRigViewModel(number, marker));
        }

        return rigs;
    }

    private static double GetSplatRadarScale(
        IReadOnlyList<SurfaceRadarPathViewModel> boundaries,
        IReadOnlyList<SurfaceRadarPointViewModel> suggestions
    )
    {
        IEnumerable<double> focusDistances = suggestions.Select(suggestion => suggestion.DistanceMeters);
        foreach (SurfaceRadarPathViewModel boundary in boundaries.Where(boundary => !boundary.IsClosed))
        {
            focusDistances = focusDistances.Concat(boundary.Points.Select(point => point.DistanceMeters));
        }

        double nearest = focusDistances.DefaultIfEmpty(double.PositiveInfinity).Min();
        return nearest switch
        {
            <= 25 => 6,
            <= 50 => 4,
            <= 100 => 2,
            _ => 1,
        };
    }

    private (
        IReadOnlyList<SurfaceRadarPathViewModel> Boundaries,
        IReadOnlyList<SurfaceRadarPointViewModel> Suggestions
    ) CreateSplatPresentation()
    {
        if (
            mineMapSurvey is null
            || context is null
            || status is null
            || mineMapSurvey.SystemAddress != context.SystemAddress
            || mineMapSurvey.BodyId != context.BodyId
            || !TryGetPosition(out SurfaceCoordinate cockpit)
        )
        {
            return ([], []);
        }

        SurfaceCoordinate current = status.InSrv
            ? SurfaceMiningGeometry.VehicleCenter(cockpit, status.NormalizedHeading, context.RadiusMeters)
            : cockpit;
        var boundaries = new List<SurfaceRadarPathViewModel>();
        var suggestions = new List<SurfaceRadarPointViewModel>();
        foreach (MineMapMarker marker in mineMapSurvey.Markers)
        {
            if (marker.SplatBoundary.Count >= 2)
            {
                boundaries.Add(
                    new SurfaceRadarPathViewModel(
                        marker
                            .SplatBoundary.Select(point => CreateSplatPoint(current, point, context.RadiusMeters))
                            .ToArray(),
                        !marker.IsSplatTraceActive
                    )
                );
            }

            suggestions.AddRange(
                marker.SuggestedRigLocations.Select(point => CreateSplatPoint(current, point, context.RadiusMeters))
            );
        }

        return (boundaries, suggestions);
    }

    private SurfaceRadarPointViewModel CreateSplatPoint(
        SurfaceCoordinate current,
        SurfaceCoordinate target,
        double planetRadiusMeters
    )
    {
        double bearing = SurfaceNavigation.GetBearing(current, target);
        return new SurfaceRadarPointViewModel(
            SurfaceNavigation.GetDistance(current, target, planetRadiusMeters),
            SurfaceNavigation.NormalizeDegrees(bearing - status!.NormalizedHeading)
        );
    }

    private SurfaceRadarMarkerViewModel? CreateRigMarker(int number)
    {
        if (
            surface is null
            || !surface.Bookmarks.TryGetValue($"#{number}", out IReadOnlyList<SurfaceCoordinate>? locations)
            || locations.Count == 0
            || !TryGetPosition(out SurfaceCoordinate cockpit)
        )
        {
            return null;
        }

        SurfaceCoordinate current = status!.InSrv
            ? SurfaceMiningGeometry.VehicleCenter(cockpit, status.NormalizedHeading, context!.RadiusMeters)
            : cockpit;
        SurfaceCoordinate location = locations[0];
        double distance = SurfaceNavigation.GetDistance(current, location, context!.RadiusMeters);
        double bearing = SurfaceNavigation.GetBearing(current, location);
        string proximity = distance < SurfaceMiningGeometry.ExclusionDistanceMeters ? "TOO CLOSE" : "TRACKED";
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

    private SurfaceRadarMarkerViewModel[] CreateResourceMarkers()
    {
        SurfaceRadarMarkerViewModel[] savedDeposits = CreateMineMapResourceMarkers();
        return savedDeposits.Length > 0 ? savedDeposits : CreateLegacyResourceMarkers();
    }

    private SurfaceRadarMarkerViewModel[] CreateMineMapResourceMarkers()
    {
        if (
            mineMapSurvey is null
            || context is null
            || status is null
            || mineMapSurvey.SystemAddress != context.SystemAddress
            || mineMapSurvey.BodyId != context.BodyId
            || !TryGetPosition(out SurfaceCoordinate cockpit)
        )
        {
            return [];
        }

        SurfaceCoordinate current = status.InSrv
            ? SurfaceMiningGeometry.VehicleCenter(cockpit, status.NormalizedHeading, context.RadiusMeters)
            : cockpit;
        return mineMapSurvey
            .Markers.Where(marker => !string.IsNullOrWhiteSpace(marker.Material))
            .Select(marker => CreateMineMapResourceMarker(marker, current))
            .OrderBy(marker => marker.DistanceMeters)
            .ThenBy(marker => marker.Name, StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();
    }

    private SurfaceRadarMarkerViewModel CreateMineMapResourceMarker(MineMapMarker marker, SurfaceCoordinate current)
    {
        double distance = SurfaceNavigation.GetDistance(current, marker.Location, context!.RadiusMeters);
        double bearing = SurfaceNavigation.GetBearing(current, marker.Location);
        return new SurfaceRadarMarkerViewModel
        {
            Name = marker.Material,
            RigCount = marker.RigCount,
            Kind = SurfaceRadarMarkerKind.Bookmark,
            Location = marker.Location,
            DistanceMeters = distance,
            BearingDegrees = bearing,
            RelativeBearingDegrees = SurfaceNavigation.NormalizeDegrees(bearing - status!.NormalizedHeading),
            RadiusMeters = SurfaceMiningGeometry.ResourceRadiusMeters,
            IsInsideRadius = distance < SurfaceMiningGeometry.ResourceRadiusMeters,
        };
    }

    private SurfaceRadarMarkerViewModel[] CreateLegacyResourceMarkers() =>
        navigation
            .Where(marker =>
                marker.IsBookmark
                && !marker.Name.StartsWith('#')
                // Legacy treats named bookmarks without a biology sample range as ground resources.
                && ExobiologyReferenceCatalog.GetSampleDistanceMeters(marker.Name) == 50
            )
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
                RadiusMeters = SurfaceMiningGeometry.ResourceRadiusMeters,
                IsInsideRadius = marker.DistanceMeters < SurfaceMiningGeometry.ResourceRadiusMeters,
            })
            .ToArray();

    private static bool SameMarkers(
        IReadOnlyList<SurfaceRadarMarkerViewModel> first,
        IReadOnlyList<SurfaceRadarMarkerViewModel> second
    ) =>
        first.Count == second.Count
        && first
            .Zip(second)
            .All(pair =>
                pair.First.Name == pair.Second.Name
                && pair.First.RigCount == pair.Second.RigCount
                && pair.First.Kind == pair.Second.Kind
                && pair.First.Status == pair.Second.Status
                && pair.First.Location == pair.Second.Location
                && Math.Abs(pair.First.DistanceMeters - pair.Second.DistanceMeters) < 0.000001
                && Math.Abs(pair.First.RelativeBearingDegrees - pair.Second.RelativeBearingDegrees) < 0.000001
            );

    internal void InstallEditorPreview(
        IReadOnlyList<SurfaceRadarMarkerViewModel> markers,
        IReadOnlyList<SurfaceRadarMarkerViewModel>? resources = null
    )
    {
        context = new SystemSurfaceContext(
            "preview",
            null,
            "Synuefe NL-N c23-4",
            42,
            null,
            3,
            "Synuefe NL-N c23-4 B 3",
            1_000_000
        );
        status = new EliteStatus { Heading = 74 };
        cargoUsed = 36;
        ShipMarkers =
        [
            new SurfaceRadarMarkerViewModel
            {
                Name = "Ship",
                Kind = SurfaceRadarMarkerKind.Ship,
                DistanceMeters = 250,
                RelativeBearingDegrees = 110,
                Location = new SurfaceCoordinate(0, 0),
            },
        ];
        Resources = (resources ?? []).Select(marker => new MiningResourceViewModel(marker)).ToArray();
        RadarMarkers = [.. markers, .. ShipMarkers, .. Resources.Select(resource => resource.Marker)];
        Rigs = Enumerable
            .Range(1, 6)
            .Select(number => new MiningRigViewModel(number, markers.ElementAtOrDefault(number - 1)))
            .ToArray();
        StatusText = "Rig locations · cyan: collect · red: too close to deploy";
        Notify();
    }

    private bool TryGetPosition(out SurfaceCoordinate location)
    {
        location = default;
        if (
            status is not { HasLatitudeLongitude: true }
            || !double.IsFinite(status.Latitude)
            || !double.IsFinite(status.Longitude)
            || status.Latitude is < -90 or > 90
            || status.Longitude is < -180 or > 180
        )
        {
            return false;
        }

        location = new SurfaceCoordinate(status.Latitude, status.Longitude);
        return true;
    }

    private static MiningRigViewModel[] EmptyRigs() =>
        Enumerable.Range(1, 6).Select(number => new MiningRigViewModel(number, null)).ToArray();

    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    public void Dispose()
    {
        disposed = true;
        updateLock.Dispose();
    }
}

public sealed record MiningResourceViewModel(SurfaceRadarMarkerViewModel Marker)
{
    public string Name =>
        Marker.RigCount is { } rigCount
            ? $"{Marker.Name} [{rigCount.ToString(CultureInfo.InvariantCulture)}]"
            : Marker.Name;
    public string DistanceText => Marker.DistanceText;
    public double Bearing => Marker.RelativeBearingDegrees;
    public bool IsNear => Marker.DistanceMeters < 150;
}

public sealed record SurfaceRadarPointViewModel(double DistanceMeters, double RelativeBearingDegrees);

public sealed record SurfaceRadarPathViewModel(IReadOnlyList<SurfaceRadarPointViewModel> Points, bool IsClosed);

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
