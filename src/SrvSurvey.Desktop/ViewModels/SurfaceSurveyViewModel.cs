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
    private PriorScanSurfaceMarkerViewModel[] priorScanSurfaceMarkers = [];
    private string statusText = "Waiting for surface survey context.";
    private double? customRadarScale;
    private string? editorBodyName;
    private string? editorHeadingText;
    private string? editorHistoryText;
    private bool editorForceVisible;
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

    public string BodyName => editorBodyName
        ?? surface?.BodyName
        ?? survey.CurrentStatus?.BodyName
        ?? "Current body";

    public string HeadingText => editorHeadingText
        ?? (survey.CurrentStatus is { } status
            ? $"HEADING {status.NormalizedHeading:000}°"
            : "HEADING —");

    public string HistoryText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(editorHistoryText))
            {
                return editorHistoryText;
            }

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

    public bool ShouldShowRadar => editorForceVisible
        || (IsEligibleStatus() && HasRadarContent());

    public bool ShouldShow => editorForceVisible
        || (IsEligibleStatus()
            && (HasRadarContent() || HasTrackerTargets()));

    public bool IsTrackerOnly => ShouldShow && !ShouldShowRadar;

    public bool ShouldShowMiniTrack => editorForceVisible
        || (survey.AutoShowMiniTrack
            && HasQuickTrackers
            && IsMiniTrackStatusEligible());

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

    /// <summary>
    /// Applies Canonn prior-scan coordinates for PlotGrounded radar rings
    /// (legacy <c>drawPriorScans</c> when showCanonnSignalsOnRadar is enabled).
    /// </summary>
    public void SetPriorScanSurfaceMarkers(
        IReadOnlyList<PriorScanSurfaceMarkerViewModel>? markers)
    {
        var next = markers is { Count: > 0 }
            ? markers.ToArray()
            : [];
        if (priorScanSurfaceMarkers.SequenceEqual(next))
        {
            return;
        }

        priorScanSurfaceMarkers = next;
        Recalculate();
    }

    /// <summary>
    /// Installs representative surface-radar content for the position editor.
    /// </summary>
    internal void InstallEditorPreview(
        string bodyName,
        string headingText,
        string historyText,
        IReadOnlyList<SurfaceRadarMarkerViewModel> radarMarkers,
        IReadOnlyList<SurfaceTrackerGroupViewModel> trackerGroups)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyName);
        ArgumentNullException.ThrowIfNull(radarMarkers);
        ArgumentNullException.ThrowIfNull(trackerGroups);
        editorForceVisible = true;
        editorBodyName = bodyName;
        editorHeadingText = headingText;
        editorHistoryText = historyText;
        StatusText = "Surface survey active";
        customRadarScale = 1;
        RadarMarkers = radarMarkers.ToArray();
        TrackerGroups = trackerGroups.ToArray();
        OnPropertyChanged(nameof(BodyName));
        OnPropertyChanged(nameof(HeadingText));
        OnPropertyChanged(nameof(HistoryText));
        OnPropertyChanged(nameof(RadarScale));
        OnPropertyChanged(nameof(RadarScaleText));
        OnPropertyChanged(nameof(ShouldShowRadar));
        OnPropertyChanged(nameof(ShouldShow));
        OnPropertyChanged(nameof(ShouldShowMiniTrack));
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

            await ApplyUpdateCoreAsync(
                session,
                journalEvents,
                status,
                currentExobiology,
                processJournalMutations,
                scansLostToDeath,
                cancellationToken)
                .ConfigureAwait(true);
        }
        finally
        {
            updateLock.Release();
        }
    }

    private async Task ApplyUpdateCoreAsync(
        SurfaceSurveySessionContext? session,
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        EliteStatus? status,
        ExobiologySnapshot currentExobiology,
        bool processJournalMutations,
        IReadOnlyList<string>? scansLostToDeath,
        CancellationToken cancellationToken)
    {
        var nextContext = session is null
            ? null
            : CreateBodyContext(session);
        if (journalEvents.Count == 0
            && status is null
            && (scansLostToDeath is null || scansLostToDeath.Count == 0)
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
        var (journalResult, deathResult) = await ApplySurfaceJournalAndDeathAsync(
            session,
            events,
            status,
            scansLostToDeath,
            cancellationToken)
            .ConfigureAwait(true);

        var contextChanged = !Equals(context, nextContext);
        context = nextContext;
        if (context is null)
        {
            ClearSurfacePresentation(journalResult, deathResult);
            return;
        }

        if (contextChanged
            || surface is null
            || journalResult?.MutationCount > 0
            || deathResult?.MarkedScanCount > 0)
        {
            await ReloadSurfaceBodyAsync(
                journalResult,
                deathResult,
                cancellationToken)
                .ConfigureAwait(true);
        }

        Recalculate();
    }

    private async Task<(
            SurfaceSurveyJournalUpdateResult? JournalResult,
            SurfaceDeathMarkResult? DeathResult)>
        ApplySurfaceJournalAndDeathAsync(
            SurfaceSurveySessionContext? session,
            IReadOnlyList<JournalEventEnvelope> events,
            EliteStatus? status,
            IReadOnlyList<string>? scansLostToDeath,
            CancellationToken cancellationToken)
    {
        if (session is null)
        {
            return (null, null);
        }

        var journalResult = await journalTracker.ApplyAsync(
                session,
                events,
                status,
                new SurfaceSurveyTrackingOptions(
                    survey.AutoRemoveTrackerOnSampling,
                    survey.AutoRemoveTrackerOnFinalSample,
                    survey.AutoTrackCompositionScans,
                    survey.SkipAnalyzedCompositionScans,
                    GetAnalyzedSpeciesByBodyId()),
                cancellationToken)
            .ConfigureAwait(true);
        var deathResult = await MarkLostSurfaceScansAsync(
            session,
            scansLostToDeath,
            cancellationToken)
            .ConfigureAwait(true);
        return (journalResult, deathResult);
    }

    private async Task<SurfaceDeathMarkResult?> MarkLostSurfaceScansAsync(
        SurfaceSurveySessionContext session,
        IReadOnlyList<string>? scansLostToDeath,
        CancellationToken cancellationToken)
    {
        if (scansLostToDeath is not { Count: > 0 })
        {
            return null;
        }

        try
        {
            return await store.MarkBioScansDiedAsync(
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
            return new SurfaceDeathMarkResult(
                0,
                0,
                ["Lost surface scans were not marked: " + exception.Message]);
        }
    }

    private void ClearSurfacePresentation(
        SurfaceSurveyJournalUpdateResult? journalResult,
        SurfaceDeathMarkResult? deathResult)
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
    }

    private async Task ReloadSurfaceBodyAsync(
        SurfaceSurveyJournalUpdateResult? journalResult,
        SurfaceDeathMarkResult? deathResult,
        CancellationToken cancellationToken)
    {
        var loadResult = await store.LoadBodyAsync(
                context!,
                cancellationToken)
            .ConfigureAwait(true);
        surface = loadResult.Snapshot;
        StatusText = BuildSurfaceLoadStatusText(
            loadResult,
            journalResult,
            deathResult);
    }

    private static string BuildSurfaceLoadStatusText(
        SystemSurfaceLoadResult loadResult,
        SurfaceSurveyJournalUpdateResult? journalResult,
        SurfaceDeathMarkResult? deathResult)
    {
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
        if (messages.Length > 0)
        {
            return string.Join(Environment.NewLine, messages);
        }

        return loadResult.BodyExists
            ? $"Loaded surface history from {Path.GetFileName(loadResult.Path)}."
            : "No saved surface history exists for this body yet.";
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

        var markers = CreateHistoricalScanMarkers(status, current);
        var (trackerRows, trackerMarkers) = CreateTrackerMarkers(status, current);
        markers.AddRange(trackerMarkers);
        markers.AddRange(CreateActiveSampleMarkers(status, current));
        markers.AddRange(CreateVehicleMarkers(status, current));

        RadarMarkers = markers
            .OrderBy(marker => marker.Kind)
            .ThenBy(marker => marker.DistanceMeters)
            .ToArray();
        TrackerGroups = trackerRows;
        RaisePresentationProperties();
    }

    private List<SurfaceRadarMarkerViewModel> CreateHistoricalScanMarkers(
        EliteStatus status,
        SurfaceCoordinate current)
    {
        var markers = new List<SurfaceRadarMarkerViewModel>();
        foreach (var scan in surface!.BioScans.Where(scan =>
                     string.IsNullOrWhiteSpace(scan.BodyName)
                     || BodyNamesMatch(scan.BodyName, surface.BodyName)))
        {
            markers.Add(CreateMarker(
                new SurfaceRadarMarkerOptions
                {
                    Name = scan.Species,
                    Location = scan.Location,
                    RadiusMeters = string.Equals(
                        scan.Status,
                        "Died",
                        StringComparison.OrdinalIgnoreCase)
                        ? 40
                        : scan.RadiusMeters,
                    Kind = SurfaceRadarMarkerKind.HistoricalScan,
                    StatusText = scan.Status,
                    Current = current,
                    Status = status,
                }));
        }

        return markers;
    }

    private (List<SurfaceTrackerGroupViewModel> TrackerRows,
        List<SurfaceRadarMarkerViewModel> Markers)
        CreateTrackerMarkers(EliteStatus status, SurfaceCoordinate current)
    {
        var activeGenus = exobiology.ScanOne is { } active
            && BodyNamesMatch(active.Body, surface!.BodyName)
                ? active.Genus
                : null;
        var trackerRows = new List<SurfaceTrackerGroupViewModel>();
        var markers = new List<SurfaceRadarMarkerViewModel>();
        foreach (var group in surface!.Bookmarks.OrderBy(
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
                    new SurfaceRadarMarkerOptions
                    {
                        Name = group.Key,
                        Location = location,
                        RadiusMeters = ExobiologyReferenceCatalog
                            .GetSampleDistanceMeters(group.Key),
                        Kind = SurfaceRadarMarkerKind.Bookmark,
                        StatusText = "Tracker",
                        Current = current,
                        Status = status,
                        IsActive = isActive,
                    }))
                .OrderBy(marker => marker.DistanceMeters)
                .ToArray();
            markers.AddRange(targets);
            trackerRows.Add(new SurfaceTrackerGroupViewModel(
                GetTrackerDisplayName(group.Key),
                isActive,
                targets));
        }

        return (trackerRows, markers);
    }

    private List<SurfaceRadarMarkerViewModel> CreateActiveSampleMarkers(
        EliteStatus status,
        SurfaceCoordinate current)
    {
        var markers = new List<SurfaceRadarMarkerViewModel>();
        var activeSamples = new[] { exobiology.ScanOne, exobiology.ScanTwo };
        for (var index = 0; index < activeSamples.Length; index++)
        {
            var sample = activeSamples[index];
            if (sample is null
                || !BodyNamesMatch(sample.Body, surface!.BodyName))
            {
                continue;
            }

            markers.Add(CreateMarker(
                new SurfaceRadarMarkerOptions
                {
                    Name = $"Sample {index + 1}",
                    Location = new SurfaceCoordinate(
                        sample.Location.Latitude,
                        sample.Location.Longitude),
                    RadiusMeters = sample.Radius,
                    Kind = SurfaceRadarMarkerKind.ActiveSample,
                    StatusText = "Active",
                    Current = current,
                    Status = status,
                }));
        }

        return markers;
    }

    private List<SurfaceRadarMarkerViewModel> CreateVehicleMarkers(
        EliteStatus status,
        SurfaceCoordinate current)
    {
        var markers = new List<SurfaceRadarMarkerViewModel>();
        var shipLocation = journalTracker.ShipLocation
            ?? surface!.LastTouchdown;
        if (shipLocation is { } ship)
        {
            var shipDeparted = journalTracker.HasShipDeparted;
            markers.Add(CreateMarker(
                new SurfaceRadarMarkerOptions
                {
                    Name = shipDeparted ? "Former ship location" : "Ship",
                    Location = ship,
                    RadiusMeters = 0,
                    Kind = shipDeparted
                        ? SurfaceRadarMarkerKind.FormerShip
                        : SurfaceRadarMarkerKind.Ship,
                    StatusText = shipDeparted ? "Departed" : "Ship",
                    Current = current,
                    Status = status,
                }));
        }

        if (journalTracker.SrvLocation is { } srv)
        {
            markers.Add(CreateMarker(
                new SurfaceRadarMarkerOptions
                {
                    Name = "SRV",
                    Location = srv,
                    RadiusMeters = 0,
                    Kind = SurfaceRadarMarkerKind.Srv,
                    StatusText = "SRV",
                    Current = current,
                    Status = status,
                }));
        }

        if (survey.ShowCanonnSignalsOnRadar
            && survey.UseExternalData
            && survey.AutoShowPriorScans
            && priorScanSurfaceMarkers.Length > 0)
        {
            foreach (var prior in priorScanSurfaceMarkers)
            {
                markers.Add(CreateMarker(
                    new SurfaceRadarMarkerOptions
                    {
                        Name = prior.DisplayName,
                        Location = prior.Location,
                        RadiusMeters = prior.SampleRadiusMeters,
                        Kind = SurfaceRadarMarkerKind.CanonnPrior,
                        StatusText = prior.IsClose ? "Close" : "Prior",
                        Current = current,
                        Status = status,
                        IsActive = prior.IsActive,
                    }));
            }
        }

        return markers;
    }

    private SurfaceRadarMarkerViewModel CreateMarker(
        SurfaceRadarMarkerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var distance = SurfaceNavigation.GetDistance(
            options.Current,
            options.Location,
            surface!.RadiusMeters);
        var bearing = SurfaceNavigation.GetBearing(
            options.Current,
            options.Location);
        var isCompletedHistoricalScan =
            options.Kind == SurfaceRadarMarkerKind.HistoricalScan
            && string.Equals(
                options.StatusText,
                "Complete",
                StringComparison.OrdinalIgnoreCase);
        return new SurfaceRadarMarkerViewModel
        {
            Name = options.Name,
            Kind = options.Kind,
            Status = options.StatusText,
            DistanceMeters = distance,
            BearingDegrees = bearing,
            RelativeBearingDegrees = SurfaceNavigation.NormalizeDegrees(
                bearing - options.Status.NormalizedHeading),
            RadiusMeters = Math.Max(0, options.RadiusMeters),
            IsInsideRadius = !isCompletedHistoricalScan
                && distance < options.RadiusMeters,
            Location = options.Location,
            IsActive = options.IsActive,
        };
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
            || survey.ShouldSuppressSurfaceNavigationForLandingGear
            || (survey.ShowSurfaceRadarOnlyWhenGeneticSamplerDrawn
                && status.OnFoot
                && !status.IsGeneticSamplerDrawn)
            || survey.IsFsdJumping)
        {
            return false;
        }

        var mode = survey.CurrentOverlayGameMode;
        var allowedMode = mode is OverlayGameMode.Flying
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

        return true;
    }

    private bool HasRadarContent()
    {
        return surface is not null
            && (surface.BioScans.Count > 0
                || surface.Bookmarks.Any(group =>
                    !group.Key.StartsWith('#') && group.Value.Count > 0)
                || exobiology.ScanOne is { } sample
                    && BodyNamesMatch(sample.Body, surface.BodyName)
                || (survey.ShowCanonnSignalsOnRadar
                    && survey.UseExternalData
                    && survey.AutoShowPriorScans
                    && priorScanSurfaceMarkers.Length > 0));
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
            || !status.HasLatitudeLongitude
            || survey.ShouldSuppressSurfaceNavigationForLandingGear)
        {
            return false;
        }

        var mode = survey.CurrentOverlayGameMode;
        return mode is OverlayGameMode.Flying
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

    private Dictionary<int, IReadOnlySet<string>>
        GetAnalyzedSpeciesByBodyId()
    {
        return survey.Snapshot.Bodies
            .Select(body => new
            {
                body.BodyId,
                Species = (IReadOnlySet<string>)body.Organisms
                    .Where(organism => organism.IsAnalyzed
                        && !string.IsNullOrWhiteSpace(organism.Species))
                    .Select(organism => organism.Species!)
                    .ToHashSet(StringComparer.Ordinal),
            })
            .Where(body => body.Species.Count > 0)
            .ToDictionary(body => body.BodyId, body => body.Species);
    }

    private void OnSurveyPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(SystemSurveyViewModel.AutoShowSurfaceRadar)
            or nameof(SystemSurveyViewModel.AutoShowMiniTrack)
            or nameof(SystemSurveyViewModel.ShowSurfaceRadarOnlyWhenGeneticSamplerDrawn)
            or nameof(SystemSurveyViewModel.SurfaceRadarSize)
            or nameof(SystemSurveyViewModel.AutoHideSurfaceRadarWithoutLandingGear)
            or nameof(SystemSurveyViewModel.ShouldSuppressForActiveBuildProjects)
            or nameof(SystemSurveyViewModel.ShowCanonnSignalsOnRadar)
            or nameof(SystemSurveyViewModel.UseExternalData)
            or nameof(SystemSurveyViewModel.AutoShowPriorScans)
            or nameof(SystemSurveyViewModel.UseSmallCanonnRadarCircles)
            or nameof(SystemSurveyViewModel.Snapshot)
            or nameof(SystemSurveyViewModel.CurrentStatus)
            or nameof(SystemSurveyViewModel.CurrentExobiology))
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

public sealed class SurfaceRadarMarkerViewModel
{
    private const double FallbackFarDistanceMeters = 1_000;

    public string Name { get; init; } = string.Empty;

    public SurfaceRadarMarkerKind Kind { get; init; }

    public string Status { get; init; } = string.Empty;

    public double DistanceMeters { get; init; }

    public double BearingDegrees { get; init; }

    public double RelativeBearingDegrees { get; init; }

    public double RadiusMeters { get; init; }

    public bool IsInsideRadius { get; init; }

    public double FarDistanceMeters => double.IsFinite(RadiusMeters)
        && RadiusMeters > 0
        ? RadiusMeters
        : FallbackFarDistanceMeters;

    public bool IsFarTarget => DistanceMeters >= FarDistanceMeters;

    public required SurfaceCoordinate Location { get; init; }

    public bool IsActive { get; init; } = true;

    public string DistanceText => DistanceMeters >= 1_000
        ? $"{DistanceMeters / 1_000:N2} km"
        : $"{DistanceMeters:N0} m";

    public string BearingText => $"{BearingDegrees:N0}°";

    public bool IsHistoricalScan => Kind == SurfaceRadarMarkerKind.HistoricalScan;

    public bool IsBookmark => Kind == SurfaceRadarMarkerKind.Bookmark;

    public bool IsActiveSample => Kind == SurfaceRadarMarkerKind.ActiveSample;

    public bool IsCanonnPrior => Kind == SurfaceRadarMarkerKind.CanonnPrior;

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
    CanonnPrior,
    Ship,
    FormerShip,
    Srv,
}
