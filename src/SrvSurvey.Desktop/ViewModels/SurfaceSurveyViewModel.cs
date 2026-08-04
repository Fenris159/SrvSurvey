using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class SurfaceSurveyViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SystemSurveyViewModel survey;
    private readonly SystemSurfaceStore store;
    private readonly SurfaceSurveyJournalTracker journalTracker;
    private readonly SemaphoreSlim updateLock = new(1, 1);
    private SystemSurfaceContext? context;
    private SystemSurfaceBodySnapshot? surface;
    private ExobiologySnapshot exobiology = ExobiologySnapshot.Empty;
    private IReadOnlyList<SurfaceRadarMarkerViewModel> radarMarkers = [];
    private IReadOnlyList<SurfaceRadarMarkerViewModel> navigationMarkers = [];
    private IReadOnlyList<SurfaceTrackerGroupViewModel> trackerGroups = [];
    private IReadOnlyList<SurfaceTrackerGroupViewModel> quickTrackerGroups = [];
    private string statusText = "Waiting for surface survey context.";
    private double? customRadarScale;
    private bool disposed;

    public SurfaceSurveyViewModel(
        SystemSurveyViewModel survey,
        SystemSurfaceStore store,
        SurfaceSurveyJournalTracker journalTracker)
    {
        this.survey = survey ?? throw new ArgumentNullException(nameof(survey));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.journalTracker = journalTracker
            ?? throw new ArgumentNullException(nameof(journalTracker));
        survey.PropertyChanged += OnSurveyPropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<SurfaceRadarMarkerViewModel> RadarMarkers
    {
        get => radarMarkers;
        private set
        {
            if (radarMarkers.SequenceEqual(value))
            {
                return;
            }

            if (SetField(ref radarMarkers, value))
            {
                navigationMarkers = value
                    .Where(marker => marker.IsActiveSample || marker.IsVehicle)
                    .ToArray();
                OnPropertyChanged(nameof(NavigationMarkers));
                OnPropertyChanged(nameof(HasNavigationMarkers));
            }
        }
    }

    public IReadOnlyList<SurfaceRadarMarkerViewModel> NavigationMarkers =>
        navigationMarkers;

    public IReadOnlyList<SurfaceTrackerGroupViewModel> TrackerGroups
    {
        get => trackerGroups;
        private set
        {
            if (TrackerGroupsEqual(trackerGroups, value))
            {
                return;
            }

            if (SetField(ref trackerGroups, value))
            {
                quickTrackerGroups = value
                    .Where(group => group.Name.StartsWith('#'))
                    .ToArray();
                OnPropertyChanged(nameof(HasTrackers));
                OnPropertyChanged(nameof(QuickTrackerGroups));
                OnPropertyChanged(nameof(HasQuickTrackers));
                OnPropertyChanged(nameof(ShouldShowMiniTrack));
            }
        }
    }

    public string StatusText
    {
        get => statusText;
        private set => SetField(ref statusText, value);
    }

    public string BodyName => surface?.BodyName
        ?? survey.CurrentStatus?.BodyName
        ?? "Current body";

    public string HeadingText => survey.CurrentStatus is { } status
        ? $"HEADING {status.NormalizedHeading:000}°"
        : "HEADING —";

    public string HistoryText
    {
        get
        {
            var scans = surface?.BioScans.Count ?? 0;
            var trackers = surface?.Bookmarks.Values.Sum(group => group.Count) ?? 0;
            return $"{scans:N0} scan circles · {trackers:N0} trackers";
        }
    }

    public bool HasTrackers => TrackerGroups.Count > 0;

    public IReadOnlyList<SurfaceTrackerGroupViewModel> QuickTrackerGroups =>
        quickTrackerGroups;

    public bool HasQuickTrackers => QuickTrackerGroups.Count > 0;

    public bool HasNavigationMarkers => NavigationMarkers.Count > 0;

    public int RadarSize => survey.SurfaceRadarSize;

    public double RadarScale => customRadarScale ?? 1;

    public string RadarScaleText => customRadarScale is { } scale
        ? $"ZOOM {scale:N2}×"
        : "ZOOM AUTO";

    public bool ShouldShowRadar => IsEligibleStatus()
        && HasRadarContent();

    public bool ShouldShow => IsEligibleStatus()
        && (HasRadarContent() || HasTrackerTargets());

    public bool IsTrackerOnly => ShouldShow && !ShouldShowRadar;

    public bool ShouldShowMiniTrack => survey.AutoShowMiniTrack
        && HasQuickTrackers
        && IsMiniTrackStatusEligible();

    public SystemSurfaceBodySnapshot? CurrentSurface => surface;

    public bool AdjustRadarScale(bool zoomIn)
    {
        const double delta = 1.25;
        var next = RadarScale;
        next = zoomIn ? next * delta : next / delta;
        if (next is < 0.25 or > 10)
        {
            return false;
        }

        customRadarScale = next;
        OnPropertyChanged(nameof(RadarScale));
        OnPropertyChanged(nameof(RadarScaleText));
        return true;
    }

    public bool ResetRadarScale()
    {
        if (customRadarScale is null)
        {
            return false;
        }

        customRadarScale = null;
        OnPropertyChanged(nameof(RadarScale));
        OnPropertyChanged(nameof(RadarScaleText));
        return true;
    }

    public async Task<bool> ClearAllTrackersAsync(
        CancellationToken cancellationToken = default)
    {
        await updateLock.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (disposed || context is null)
            {
                StatusText = "A scanned body is required before trackers can be cleared.";
                return false;
            }

            try
            {
                await store.ClearBookmarksAsync(context, cancellationToken)
                    .ConfigureAwait(true);
                var loadResult = await store.LoadBodyAsync(
                        context,
                        cancellationToken)
                    .ConfigureAwait(true);
                surface = loadResult.Snapshot;
                StatusText = "All surface trackers for the current body were cleared.";
                Recalculate();
                return true;
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException
                    or InvalidOperationException)
            {
                StatusText = "Surface trackers could not be cleared: "
                    + exception.Message;
                return false;
            }
        }
        finally
        {
            updateLock.Release();
        }
    }

    public async Task<bool> ToggleQuickTrackerAsync(
        int number,
        CancellationToken cancellationToken = default)
    {
        if (number is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(number));
        }

        await updateLock.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (disposed
                || context is null
                || survey.CurrentStatus is not { } status
                || !TryGetCurrentCoordinate(status, out var location))
            {
                StatusText = "A scanned body and live surface coordinates are "
                    + "required to toggle a quick tracker.";
                return false;
            }

            var name = $"#{number}";
            try
            {
                var mutation = await store.ToggleBookmarkGroupAsync(
                        context,
                        name,
                        location,
                        cancellationToken)
                    .ConfigureAwait(true);
                var loadResult = await store.LoadBodyAsync(
                        context,
                        cancellationToken)
                    .ConfigureAwait(true);
                surface = loadResult.Snapshot;
                StatusText = mutation.Mutation == SurfaceBookmarkMutation.Added
                    ? $"Quick tracker {name} added at the current location."
                    : $"Quick tracker {name} removed.";
                Recalculate();
                return true;
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException
                    or InvalidOperationException)
            {
                StatusText = $"Quick tracker {name} was not changed: "
                    + exception.Message;
                return false;
            }
        }
        finally
        {
            updateLock.Release();
        }
    }

    public void Reset(ExobiologySnapshot? seed = null)
    {
        journalTracker.Reset(seed);
        exobiology = seed ?? ExobiologySnapshot.Empty;
        context = null;
        surface = null;
        customRadarScale = null;
        RadarMarkers = [];
        TrackerGroups = [];
        StatusText = "Waiting for surface survey context.";
        RaisePresentationProperties();
    }

    public async Task ApplyUpdateAsync(
        SurfaceSurveySessionContext? session,
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        EliteStatus? status,
        ExobiologySnapshot currentExobiology,
        bool processJournalMutations = true,
        IReadOnlyList<string>? scansLostToDeath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        ArgumentNullException.ThrowIfNull(currentExobiology);
        await updateLock.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (disposed)
            {
                return;
            }

            var nextContext = session is null
                ? null
                : CreateBodyContext(session);
            if (journalEvents.Count == 0
                && status is null
                && (scansLostToDeath is null
                    || scansLostToDeath.Count == 0)
                && Equals(context, nextContext)
                && HasSameExobiology(exobiology, currentExobiology))
            {
                return;
            }

            exobiology = currentExobiology;
            var events = processJournalMutations
                ? journalEvents
                : journalEvents
                    .Where(item => item.EventName is not "ScanOrganic"
                        and not "CodexEntry"
                        and not "SendText")
                    .ToArray();
            SurfaceSurveyJournalUpdateResult? journalResult = null;
            SurfaceDeathMarkResult? deathResult = null;
            if (session is not null)
            {
                journalResult = await journalTracker.ApplyAsync(
                        session,
                        events,
                        status,
                        new SurfaceSurveyTrackingOptions(
                            survey.AutoRemoveTrackerOnSampling,
                            survey.AutoRemoveTrackerOnFinalSample,
                            survey.AutoTrackCompositionScans,
                            survey.SkipAnalyzedCompositionScans,
                            GetAnalyzedSpecies()),
                        cancellationToken)
                    .ConfigureAwait(true);
                if (scansLostToDeath is { Count: > 0 })
                {
                    try
                    {
                        deathResult = await store.MarkBioScansDiedAsync(
                                session.FrontierId,
                                scansLostToDeath,
                                cancellationToken)
                            .ConfigureAwait(true);
                    }
                    catch (Exception exception) when (
                        exception is IOException
                            or UnauthorizedAccessException
                            or InvalidDataException
                            or InvalidOperationException)
                    {
                        deathResult = new SurfaceDeathMarkResult(
                            0,
                            0,
                            ["Lost surface scans were not marked: "
                                + exception.Message]);
                    }
                }
            }

            var contextChanged = !Equals(context, nextContext);
            context = nextContext;
            if (context is null)
            {
                surface = null;
                RadarMarkers = [];
                TrackerGroups = [];
                StatusText = journalResult?.Warnings.Count > 0
                    || deathResult?.Warnings.Count > 0
                        ? string.Join(
                            Environment.NewLine,
                            (journalResult?.Warnings ?? [])
                                .Concat(deathResult?.Warnings ?? []))
                        : "Waiting for a scanned body and surface coordinates.";
                RaisePresentationProperties();
                return;
            }

            if (contextChanged
                || surface is null
                || journalResult?.MutationCount > 0
                || deathResult?.MarkedScanCount > 0)
            {
                var loadResult = await store.LoadBodyAsync(
                        context,
                        cancellationToken)
                    .ConfigureAwait(true);
                surface = loadResult.Snapshot;
                var messages = new[]
                    {
                        loadResult.Error,
                        loadResult.Warnings.Count > 0
                            ? string.Join(Environment.NewLine, loadResult.Warnings)
                            : null,
                        journalResult?.Warnings.Count > 0
                            ? string.Join(Environment.NewLine, journalResult.Warnings)
                            : null,
                        deathResult?.Warnings.Count > 0
                            ? string.Join(Environment.NewLine, deathResult.Warnings)
                            : null,
                    }
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .ToArray();
                StatusText = messages.Length > 0
                    ? string.Join(Environment.NewLine, messages)
                    : loadResult.BodyExists
                        ? $"Loaded surface history from {Path.GetFileName(loadResult.Path)}."
                        : "No saved surface history exists for this body yet.";
            }

            Recalculate();
        }
        finally
        {
            updateLock.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        survey.PropertyChanged -= OnSurveyPropertyChanged;
        updateLock.Dispose();
    }

    private void Recalculate()
    {
        if (surface is null
            || survey.CurrentStatus is not { } status
            || !TryGetCurrentCoordinate(status, out var current)
            || surface.RadiusMeters <= 0)
        {
            RadarMarkers = [];
            TrackerGroups = [];
            RaisePresentationProperties();
            return;
        }

        var markers = new List<SurfaceRadarMarkerViewModel>();
        foreach (var scan in surface.BioScans.Where(scan =>
                     string.IsNullOrWhiteSpace(scan.BodyName)
                     || BodyNamesMatch(scan.BodyName, surface.BodyName)))
        {
            markers.Add(CreateMarker(
                scan.Species,
                scan.Location,
                string.Equals(scan.Status, "Died", StringComparison.OrdinalIgnoreCase)
                    ? 40
                    : scan.RadiusMeters,
                SurfaceRadarMarkerKind.HistoricalScan,
                scan.Status,
                current,
                status));
        }

        var activeGenus = exobiology.ScanOne is { } active
            && BodyNamesMatch(active.Body, surface.BodyName)
                ? active.Genus
                : null;
        var trackerRows = new List<SurfaceTrackerGroupViewModel>();
        foreach (var group in surface.Bookmarks.OrderBy(
                     pair => pair.Key,
                     StringComparer.Ordinal))
        {
            var isActive = string.IsNullOrWhiteSpace(activeGenus)
                || string.Equals(
                    activeGenus,
                    group.Key,
                    StringComparison.Ordinal);
            var targets = group.Value
                .Select(location => CreateMarker(
                    group.Key,
                    location,
                    ExobiologyReferenceCatalog.GetSampleDistanceMeters(group.Key),
                    SurfaceRadarMarkerKind.Bookmark,
                    "Tracker",
                    current,
                    status,
                    isActive))
                .OrderBy(marker => marker.DistanceMeters)
                .ToArray();
            markers.AddRange(targets);
            trackerRows.Add(new SurfaceTrackerGroupViewModel(
                GetTrackerDisplayName(group.Key),
                isActive,
                targets));
        }

        var activeSamples = new[] { exobiology.ScanOne, exobiology.ScanTwo };
        for (var index = 0; index < activeSamples.Length; index++)
        {
            var sample = activeSamples[index];
            if (sample is null
                || !BodyNamesMatch(sample.Body, surface.BodyName))
            {
                continue;
            }

            markers.Add(CreateMarker(
                $"Sample {index + 1}",
                new SurfaceCoordinate(
                    sample.Location.Latitude,
                    sample.Location.Longitude),
                sample.Radius,
                SurfaceRadarMarkerKind.ActiveSample,
                "Active",
                current,
                status));
        }

        var shipLocation = journalTracker.ShipLocation
            ?? surface.LastTouchdown;
        if (shipLocation is { } ship)
        {
            var shipDeparted = journalTracker.HasShipDeparted;
            markers.Add(CreateMarker(
                shipDeparted ? "Former ship location" : "Ship",
                ship,
                0,
                shipDeparted
                    ? SurfaceRadarMarkerKind.FormerShip
                    : SurfaceRadarMarkerKind.Ship,
                shipDeparted ? "Departed" : "Ship",
                current,
                status));
        }

        if (journalTracker.SrvLocation is { } srv)
        {
            markers.Add(CreateMarker(
                "SRV",
                srv,
                0,
                SurfaceRadarMarkerKind.Srv,
                "SRV",
                current,
                status));
        }

        RadarMarkers = markers
            .OrderBy(marker => marker.Kind)
            .ThenBy(marker => marker.DistanceMeters)
            .ToArray();
        TrackerGroups = trackerRows;
        RaisePresentationProperties();
    }

    private SurfaceRadarMarkerViewModel CreateMarker(
        string name,
        SurfaceCoordinate location,
        double radiusMeters,
        SurfaceRadarMarkerKind kind,
        string statusText,
        SurfaceCoordinate current,
        EliteStatus status,
        bool isActive = true)
    {
        var distance = SurfaceNavigation.GetDistance(
            current,
            location,
            surface!.RadiusMeters);
        var bearing = SurfaceNavigation.GetBearing(current, location);
        return new SurfaceRadarMarkerViewModel(
            name,
            kind,
            statusText,
            distance,
            bearing,
            SurfaceNavigation.NormalizeDegrees(
                bearing - status.NormalizedHeading),
            Math.Max(0, radiusMeters),
            distance < radiusMeters,
            location,
            isActive);
    }

    private static bool HasSameExobiology(
        ExobiologySnapshot current,
        ExobiologySnapshot candidate)
    {
        return current.LastOrganicScan == candidate.LastOrganicScan
            && Equals(current.ScanOne, candidate.ScanOne)
            && Equals(current.ScanTwo, candidate.ScanTwo)
            && current.OrganicRewards == candidate.OrganicRewards
            && current.CountRadicoidaUnica == candidate.CountRadicoidaUnica
            && current.ScannedBioEntryIds.SequenceEqual(
                candidate.ScannedBioEntryIds,
                StringComparer.Ordinal);
    }

    private SystemSurfaceContext? CreateBodyContext(
        SurfaceSurveySessionContext session)
    {
        var status = survey.CurrentStatus;
        var body = status?.BodyName is { Length: > 0 } bodyName
            ? survey.Snapshot.Bodies.FirstOrDefault(candidate =>
                BodyNamesMatch(candidate.Name, bodyName))
            : null;
        body ??= survey.Snapshot.CurrentBodyId is { } bodyId
            ? survey.Snapshot.Bodies.FirstOrDefault(candidate =>
                candidate.BodyId == bodyId)
            : null;
        if (body is null)
        {
            return null;
        }

        var radius = status?.PlanetRadius is > 0
            ? (double)status.PlanetRadius
            : body.RadiusMeters;
        return new SystemSurfaceContext(
            session.FrontierId,
            session.CommanderName,
            session.SystemName,
            session.SystemAddress,
            session.StarPosition,
            body.BodyId,
            body.Name,
            radius);
    }

    private bool IsEligibleStatus()
    {
        if (!survey.AutoShowSurfaceRadar
            || surface is null
            || survey.CurrentStatus is not { } status
            || survey.ShouldSuppressForActiveBuildProjects
            || !status.HasLatitudeLongitude
            || status.PlanetRadius <= 0
            || status.Docked
            || status.Altitude >= 10_000
            || status.InTaxi
            || status.FsdChargingJump
            || survey.IsFsdJumping)
        {
            return false;
        }

        var mode = survey.CurrentOverlayGameMode;
        var allowedMode = mode is OverlayGameMode.SuperCruising
            or OverlayGameMode.Flying
            or OverlayGameMode.Landed
            or OverlayGameMode.InSrv
            or OverlayGameMode.OnFoot
            or OverlayGameMode.GlideMode
            or OverlayGameMode.InFighter
            or OverlayGameMode.CommsPanel
            or OverlayGameMode.RolePanel;
        if (!allowedMode)
        {
            return false;
        }

        return !survey.AutoHideSurfaceRadarWithoutLandingGear
            || mode != OverlayGameMode.Flying
            || status.LandingGearDown;
    }

    private bool HasRadarContent()
    {
        return surface is not null
            && (surface.BioScans.Count > 0
                || surface.Bookmarks.Any(group =>
                    !group.Key.StartsWith('#') && group.Value.Count > 0)
                || exobiology.ScanOne is { } sample
                    && BodyNamesMatch(sample.Body, surface.BodyName));
    }

    private bool HasTrackerTargets()
    {
        return surface?.Bookmarks.Any(group =>
            !group.Key.StartsWith('#') && group.Value.Count > 0) == true;
    }

    private bool IsMiniTrackStatusEligible()
    {
        if (surface is null
            || survey.CurrentStatus is not { } status
            || !status.HasLatitudeLongitude)
        {
            return false;
        }

        var mode = survey.CurrentOverlayGameMode;
        return mode is OverlayGameMode.SuperCruising
            or OverlayGameMode.Flying
            or OverlayGameMode.Landed
            or OverlayGameMode.InSrv
            or OverlayGameMode.OnFoot
            or OverlayGameMode.GlideMode
            or OverlayGameMode.InFighter
            or OverlayGameMode.CommsPanel
            or OverlayGameMode.RolePanel;
    }

    private string GetTrackerDisplayName(string name)
    {
        var body = survey.Snapshot.Bodies.FirstOrDefault(candidate =>
            surface is not null
            && candidate.BodyId == surface.BodyId);
        var organism = body?.Organisms.FirstOrDefault(candidate =>
            string.Equals(candidate.Genus, name, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(organism?.GenusLocalized))
        {
            return organism.GenusLocalized;
        }

        if (!name.StartsWith("$Codex_Ent_", StringComparison.Ordinal))
        {
            return name;
        }

        return ExobiologyReferenceCatalog.GetGenusDisplayName(name);
    }

    private IReadOnlySet<string> GetAnalyzedSpecies()
    {
        return survey.Snapshot.Bodies
            .SelectMany(body => body.Organisms)
            .Where(organism => organism.IsAnalyzed
                && !string.IsNullOrWhiteSpace(organism.Species))
            .Select(organism => organism.Species!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private void OnSurveyPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(SystemSurveyViewModel.AutoShowSurfaceRadar)
            or nameof(SystemSurveyViewModel.AutoShowMiniTrack)
            or nameof(SystemSurveyViewModel.SurfaceRadarSize)
            or nameof(SystemSurveyViewModel.AutoHideSurfaceRadarWithoutLandingGear)
            or nameof(SystemSurveyViewModel.ShouldSuppressForActiveBuildProjects)
            or nameof(SystemSurveyViewModel.Snapshot)
            or nameof(SystemSurveyViewModel.CurrentStatus))
        {
            Recalculate();
        }
    }

    private void RaisePresentationProperties()
    {
        OnPropertyChanged(nameof(BodyName));
        OnPropertyChanged(nameof(HeadingText));
        OnPropertyChanged(nameof(HistoryText));
        OnPropertyChanged(nameof(RadarSize));
        OnPropertyChanged(nameof(RadarScale));
        OnPropertyChanged(nameof(RadarScaleText));
        OnPropertyChanged(nameof(ShouldShowRadar));
        OnPropertyChanged(nameof(ShouldShow));
        OnPropertyChanged(nameof(IsTrackerOnly));
        OnPropertyChanged(nameof(ShouldShowMiniTrack));
    }

    private static bool TrackerGroupsEqual(
        IReadOnlyList<SurfaceTrackerGroupViewModel> first,
        IReadOnlyList<SurfaceTrackerGroupViewModel> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        for (var index = 0; index < first.Count; index++)
        {
            var firstGroup = first[index];
            var secondGroup = second[index];
            if (!string.Equals(firstGroup.Name, secondGroup.Name, StringComparison.Ordinal)
                || firstGroup.IsActive != secondGroup.IsActive
                || !firstGroup.Targets.SequenceEqual(secondGroup.Targets))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetCurrentCoordinate(
        EliteStatus status,
        out SurfaceCoordinate coordinate)
    {
        coordinate = default;
        if (!status.HasLatitudeLongitude)
        {
            return false;
        }

        try
        {
            coordinate = new SurfaceCoordinate(status.Latitude, status.Longitude);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool BodyNamesMatch(string? first, string? second)
    {
        return !string.IsNullOrWhiteSpace(first)
            && !string.IsNullOrWhiteSpace(second)
            && string.Equals(
                first.Replace(" ", string.Empty, StringComparison.Ordinal),
                second.Replace(" ", string.Empty, StringComparison.Ordinal),
                StringComparison.OrdinalIgnoreCase);
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record SurfaceRadarMarkerViewModel(
    string Name,
    SurfaceRadarMarkerKind Kind,
    string Status,
    double DistanceMeters,
    double BearingDegrees,
    double RelativeBearingDegrees,
    double RadiusMeters,
    bool IsInsideRadius,
    SurfaceCoordinate Location,
    bool IsActive = true)
{
    public string DistanceText => DistanceMeters >= 1_000
        ? $"{DistanceMeters / 1_000:N2} km"
        : $"{DistanceMeters:N0} m";

    public string BearingText => $"{BearingDegrees:N0}°";

    public bool IsHistoricalScan => Kind == SurfaceRadarMarkerKind.HistoricalScan;

    public bool IsBookmark => Kind == SurfaceRadarMarkerKind.Bookmark;

    public bool IsActiveSample => Kind == SurfaceRadarMarkerKind.ActiveSample;

    public bool IsVehicle => Kind is SurfaceRadarMarkerKind.Ship
        or SurfaceRadarMarkerKind.FormerShip
        or SurfaceRadarMarkerKind.Srv;
}

public sealed record SurfaceTrackerGroupViewModel(
    string Name,
    bool IsActive,
    IReadOnlyList<SurfaceRadarMarkerViewModel> Targets)
{
    public double RowOpacity => IsActive ? 1 : 0.58;
}

public enum SurfaceRadarMarkerKind
{
    HistoricalScan,
    Bookmark,
    ActiveSample,
    Ship,
    FormerShip,
    Srv,
}
