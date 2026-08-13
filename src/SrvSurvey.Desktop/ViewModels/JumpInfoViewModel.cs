using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class JumpInfoViewModel : INotifyPropertyChanged, IDisposable
{
    private const string Unavailable = "\u2014";
    private const string ScoopableStarClasses = "KGBFOAM";

    private readonly ISystemSummaryClient summaryClient;
    private readonly JumpInfoSettingsStore settingsStore;
    private readonly GuardianSiteCatalog guardianSites;
    private readonly TimeProvider timeProvider;
    private CancellationTokenSource? summaryCancellation;
    private string? currentSystemName;
    private long? currentSystemAddress;
    private GalacticCoordinate? currentPosition;
    private NavRouteSnapshot? navRoute;
    private FollowRouteDocument? followedRoute;
    private FollowRouteDocument? displayedFollowedRoute;
    private EliteStatus? status;
    private string? musicTrack;
    private JumpTarget? fsdTarget;
    private JumpTarget? inFlightTarget;
    private JumpInfoRoutePlan? routePlan;
    private SystemSummary? summary;
    private IReadOnlyList<JumpInfoDetailLineViewModel> detailLines = [];
    private HashSet<string> questTags = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase);
    private double? maximumJumpRange;
    private DateTimeOffset? jumpVisibleUntil;
    private DateTimeOffset? jumpContentHeldUntil;
    private DateTimeOffset? followedRouteFinishedUntil;
    private bool isOverlayPresented;
    private bool isWaitingForOverlayHide;
    private bool hasQueuedPlan;
    private bool fsdJumping;
    private bool forceShow;
    private bool manuallyHidden;
    private bool isLoading;
    private bool autoShow;
    private bool minimal;
    private bool showWhenNextHopSelected;
    private bool useSpanshLastUpdated;
    private string settingsStatus = string.Empty;
    private string dataStatus = "Waiting for a jump target.";
    private bool disposed;

    public JumpInfoViewModel(
        ISystemSummaryClient summaryClient,
        JumpInfoSettingsStore settingsStore,
        GuardianSiteCatalog? guardianSites = null,
        TimeProvider? timeProvider = null)
    {
        this.summaryClient = summaryClient
            ?? throw new ArgumentNullException(nameof(summaryClient));
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        this.guardianSites = guardianSites
            ?? GuardianSiteCatalog.LoadEmbedded();
        this.timeProvider = timeProvider ?? TimeProvider.System;
        var preferences = settingsStore.Load();
        autoShow = preferences.AutoShow;
        minimal = preferences.Minimal;
        showWhenNextHopSelected = preferences.ShowWhenNextHopSelected;
        useSpanshLastUpdated = preferences.UseSpanshLastUpdated;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool AutoShow
    {
        get => autoShow;
        set
        {
            if (SetField(ref autoShow, value))
            {
                SavePreferences();
                RaiseVisibilityProperties();
            }
        }
    }

    public bool Minimal
    {
        get => minimal;
        set
        {
            if (SetField(ref minimal, value))
            {
                SavePreferences();
                OnPropertyChanged(nameof(ShowDetails));
            }
        }
    }

    public bool ShowWhenNextHopSelected
    {
        get => showWhenNextHopSelected;
        set
        {
            if (SetField(ref showWhenNextHopSelected, value))
            {
                SavePreferences();
                RaiseVisibilityProperties();
            }
        }
    }

    public bool UseSpanshLastUpdated
    {
        get => useSpanshLastUpdated;
        set
        {
            if (SetField(ref useSpanshLastUpdated, value))
            {
                SavePreferences();
            }
        }
    }

    public bool ShowDetails => !Minimal;

    public string SettingsStatus
    {
        get => settingsStatus;
        private set
        {
            if (SetField(ref settingsStatus, value))
            {
                OnPropertyChanged(nameof(HasSettingsStatus));
            }
        }
    }

    public bool HasSettingsStatus => !string.IsNullOrWhiteSpace(SettingsStatus);

    public bool ShouldShow
    {
        get
        {
            if (!AutoShow
                || routePlan is null
                || manuallyHidden
                || isWaitingForOverlayHide)
            {
                return false;
            }

            var mode = OverlayGameModeResolver.Resolve(
                status,
                fsdJumping,
                musicTrack);
            var chargingForJump = status?.FsdChargingJump == true
                && (mode == OverlayGameMode.Flying
                    || mode == OverlayGameMode.SuperCruising);
            var automatic = chargingForJump
                || fsdJumping
                || jumpVisibleUntil > timeProvider.GetUtcNow()
                    && mode != OverlayGameMode.GalaxyMap
                || ShowWhenNextHopSelected && IsSelectedFollowedRouteHop();
            var forced = forceShow && mode != OverlayGameMode.Fss;
            return automatic || forced;
        }
    }

    public bool IsForced => forceShow;

    public bool IsLoading
    {
        get => isLoading;
        private set => SetField(ref isLoading, value);
    }

    public string TargetName => routePlan?.Target.Name ?? Unavailable;

    public bool IsQuestTagged => routePlan is { } plan
        && questTags.Contains(plan.Target.Name);

    public string StarClass => string.IsNullOrWhiteSpace(
        routePlan?.Target.StarClass ?? summary?.StarClass)
            ? "STAR CLASS UNKNOWN"
            : "STAR CLASS " + (routePlan?.Target.StarClass ?? summary?.StarClass);

    public bool IsScoopableStarClass
    {
        get
        {
            var starClass = (routePlan?.Target.StarClass ?? summary?.StarClass)
                ?.Trim();
            return starClass is { Length: 1 }
                && ScoopableStarClasses.Contains(
                    char.ToUpperInvariant(starClass[0]));
        }
    }

    public string JumpProgress => CreateFollowedRouteProgressText()
        ?? (routePlan is { Legs.Count: > 0 } plan
            ? (plan.JumpNumber > 0) switch
            {
                true => $"JUMP {plan.JumpNumber:N0} OF {plan.Legs.Count:N0}",
                false => $"{plan.Legs.Count:N0} ROUTE JUMPS"
            }
            : "DIRECT TARGET");

    public string TotalDistance => routePlan is { Legs.Count: > 0 } plan
        ? $"{plan.TotalDistanceLy:N1} LY"
        : "DISTANCE UNAVAILABLE";

    public IReadOnlyList<JumpInfoRouteLeg> RouteLegs => routePlan?.Legs ?? [];

    public int TargetLegIndex => routePlan?.TargetLegIndex ?? -1;

    public string DiscoveryText => CreateDiscoveryText(summary);

    public bool HasDiscoveryText => !string.IsNullOrWhiteSpace(DiscoveryText);

    public string TrafficText => summary?.Traffic is { Total: > 0 } traffic
        ? $"Traffic: {traffic.Day:N0} today, {traffic.Week:N0} this week, "
            + $"{traffic.Total:N0} total (EDSM)"
        : string.Empty;

    public bool HasTraffic => !string.IsNullOrWhiteSpace(TrafficText);

    public string PointsOfInterestText => CreatePointsOfInterestText(summary);

    public bool HasPointsOfInterest => !string.IsNullOrWhiteSpace(
        PointsOfInterestText);

    public IReadOnlyList<JumpInfoDetailLineViewModel> DetailLines
    {
        get => detailLines;
        private set => SetField(ref detailLines, value);
    }

    public bool HasDetailLines => DetailLines.Count > 0;

    public bool HasRefuelGuidance => DetailLines.Any(line => line.Refuel);

    public bool HasNeutronGuidance => DetailLines.Any(line => line.Neutron);

    public bool HasRouteGuidanceBadges =>
        HasRefuelGuidance || HasNeutronGuidance;

    public bool HasDiscoveryOrRouteGuidance =>
        HasDiscoveryText || HasRouteGuidanceBadges;

    public string DataStatus
    {
        get => dataStatus;
        private set
        {
            if (SetField(ref dataStatus, value))
            {
                OnPropertyChanged(nameof(HasDataStatus));
            }
        }
    }

    public bool HasDataStatus => !string.IsNullOrWhiteSpace(DataStatus);

    public Task PendingSummaryLoad { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Installs representative next-jump content for the position editor.
    /// </summary>
    internal void InstallEditorPreview(
        JumpInfoRoutePlan plan,
        SystemSummary systemSummary,
        IReadOnlyList<JumpInfoDetailLineViewModel> lines,
        string dataStatusText = "Jump target data ready")
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(systemSummary);
        ArgumentNullException.ThrowIfNull(lines);
        routePlan = plan;
        summary = systemSummary;
        DetailLines = lines.ToArray();
        DataStatus = dataStatusText;
        IsLoading = false;
        OnPropertyChanged(nameof(TargetName));
        OnPropertyChanged(nameof(StarClass));
        OnPropertyChanged(nameof(IsScoopableStarClass));
        OnPropertyChanged(nameof(JumpProgress));
        OnPropertyChanged(nameof(TotalDistance));
        OnPropertyChanged(nameof(RouteLegs));
        OnPropertyChanged(nameof(TargetLegIndex));
        OnPropertyChanged(nameof(DiscoveryText));
        OnPropertyChanged(nameof(HasDiscoveryText));
        OnPropertyChanged(nameof(TrafficText));
        OnPropertyChanged(nameof(HasTraffic));
        OnPropertyChanged(nameof(PointsOfInterestText));
        OnPropertyChanged(nameof(HasPointsOfInterest));
        OnPropertyChanged(nameof(HasDetailLines));
        OnPropertyChanged(nameof(HasRefuelGuidance));
        OnPropertyChanged(nameof(HasNeutronGuidance));
        OnPropertyChanged(nameof(HasRouteGuidanceBadges));
        OnPropertyChanged(nameof(HasDiscoveryOrRouteGuidance));
        OnPropertyChanged(nameof(IsQuestTagged));
        OnPropertyChanged(nameof(ShowDetails));
    }

    public void UpdateQuestTags(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var next = tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (questTags.SetEquals(next))
        {
            return;
        }

        questTags = next;
        OnPropertyChanged(nameof(IsQuestTagged));
    }

    public void ApplyUpdate(JumpInfoApplyUpdateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(request.JournalEvents);
        currentSystemName = request.CurrentSystemName;
        currentSystemAddress = request.CurrentSystemAddress;
        currentPosition = request.CurrentPosition;
        followedRoute = request.FollowedRoute;
        if (request.NavRoute is not null)
        {
            navRoute = request.NavRoute;
        }

        if (request.Status is not null)
        {
            status = request.Status;
        }

        ApplyJournalEvents(request.JournalEvents, request.IsBootstrapRead);
        RefreshPlan();
    }

    public bool ToggleForcedVisibility()
    {
        if (routePlan is null)
        {
            return false;
        }

        if (!ShouldShow)
        {
            manuallyHidden = false;
            forceShow = true;
        }
        else if (forceShow)
        {
            forceShow = false;
            manuallyHidden = true;
        }
        else
        {
            manuallyHidden = !manuallyHidden;
        }

        OnPropertyChanged(nameof(IsForced));
        RaiseVisibilityProperties();
        return true;
    }

    internal void AdvanceTimedTransitions()
    {
        if (jumpContentHeldUntil is not { } heldUntil
            || heldUntil > timeProvider.GetUtcNow())
        {
            return;
        }

        jumpContentHeldUntil = null;
        if (isOverlayPresented)
        {
            isWaitingForOverlayHide = true;
            RaiseVisibilityProperties();
            return;
        }

        QueuePlanForNextPresentation();
    }

    internal void BeginOverlayPresentation()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        isOverlayPresented = true;
        if (!hasQueuedPlan)
        {
            return;
        }

        hasQueuedPlan = false;
        followedRouteFinishedUntil = null;
        RefreshPlan();
    }

    internal void EndOverlayPresentation()
    {
        if (disposed)
        {
            return;
        }

        isOverlayPresented = false;
        if (!isWaitingForOverlayHide)
        {
            return;
        }

        isWaitingForOverlayHide = false;
        QueuePlanForNextPresentation();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        summaryCancellation?.Cancel();
        summaryCancellation?.Dispose();
        summaryCancellation = null;
    }

    private void ApplyJournalEvents(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        bool isBootstrapRead)
    {
        foreach (var journalEvent in journalEvents)
        {
            ApplyMusicAndLoadout(journalEvent);
            if (!isBootstrapRead)
            {
                ApplyLiveJumpEvent(journalEvent);
            }
        }
    }

    private void ApplyMusicAndLoadout(JournalEventEnvelope journalEvent)
    {
        if (journalEvent.EventName == "Music")
        {
            musicTrack = GetString(journalEvent.Payload, "MusicTrack");
        }
        else if (journalEvent.EventName is "Fileheader" or "LoadGame")
        {
            musicTrack = null;
        }

        if (journalEvent.EventName == "Loadout")
        {
            maximumJumpRange = GetDouble(
                journalEvent.Payload,
                "MaxJumpRange") ?? maximumJumpRange;
        }
    }

    private void ApplyLiveJumpEvent(JournalEventEnvelope journalEvent)
    {
        switch (journalEvent.EventName)
        {
            case "FSDTarget":
                fsdTarget = ParseTarget(journalEvent.Payload);
                manuallyHidden = false;
                break;

            case "NavRouteClear":
                fsdTarget = null;
                navRoute = null;
                forceShow = false;
                manuallyHidden = false;
                break;

            case "StartJump" when string.Equals(
                GetString(journalEvent.Payload, "JumpType"),
                "Hyperspace",
                StringComparison.OrdinalIgnoreCase):
                fsdJumping = true;
                CaptureInFlightTarget();
                jumpVisibleUntil = null;
                break;

            case "FSDJump":
            case "CarrierJump":
                fsdJumping = false;
                inFlightTarget = null;
                var isFinalFollowedRouteArrival =
                    journalEvent.EventName == "FSDJump"
                    && IsFinalFollowedRouteArrival(journalEvent.Payload);
                var transitionAt = timeProvider.GetUtcNow().AddSeconds(
                    isFinalFollowedRouteArrival ? 3 : 1);
                jumpVisibleUntil = transitionAt;
                if (routePlan is not null)
                {
                    jumpContentHeldUntil = transitionAt;
                }

                followedRouteFinishedUntil = isFinalFollowedRouteArrival
                    ? transitionAt
                    : null;
                OnPropertyChanged(nameof(JumpProgress));

                break;
        }
    }

    private void RefreshPlan()
    {
        if (routePlan is not null
            && (jumpContentHeldUntil is not null
                || isWaitingForOverlayHide
                || hasQueuedPlan))
        {
            RaiseVisibilityProperties();
            return;
        }

        var previousTarget = routePlan?.Target;
        displayedFollowedRoute = followedRoute;
        routePlan = PreserveInFlightStarClass(JumpInfoRoutePlanner.Create(
            new JumpInfoRoutePlannerRequest
            {
                FsdTarget = fsdTarget,
                Status = status,
                CurrentSystemName = currentSystemName,
                CurrentSystemAddress = currentSystemAddress,
                CurrentPosition = currentPosition,
                NavRoute = navRoute,
                FollowedRoute = followedRoute,
                MaximumJumpRange = maximumJumpRange
            }));
        RaisePlanProperties();

        var nextTarget = routePlan?.Target;
        if (SameTarget(previousTarget, nextTarget))
        {
            RefreshDetailLines();
            return;
        }

        summary = null;
        RefreshSummaryProperties();
        summaryCancellation?.Cancel();
        summaryCancellation?.Dispose();
        summaryCancellation = null;
        if (nextTarget is null)
        {
            IsLoading = false;
            DataStatus = "Waiting for a jump target.";
            PendingSummaryLoad = Task.CompletedTask;
            return;
        }

        summaryCancellation = new CancellationTokenSource();
        PendingSummaryLoad = LoadSummaryAsync(
            nextTarget,
            summaryCancellation.Token);
    }

    private async Task LoadSummaryAsync(
        JumpTarget target,
        CancellationToken cancellationToken)
    {
        try
        {
            IsLoading = true;
            DataStatus = "Loading EDSM and Spansh system data\u2026";
            var result = await summaryClient.GetAsync(
                target.Name,
                target.SystemAddress,
                cancellationToken);
            if (cancellationToken.IsCancellationRequested
                || !SameTarget(routePlan?.Target, target))
            {
                return;
            }

            summary = result.Summary;
            if (string.IsNullOrWhiteSpace(routePlan?.Target.StarClass)
                && !string.IsNullOrWhiteSpace(summary.StarClass)
                && routePlan is not null)
            {
                routePlan = routePlan with
                {
                    Target = routePlan.Target with
                    {
                        StarClass = summary.StarClass,
                    },
                };
                RaisePlanProperties();
            }

            DataStatus = result.Warnings.Count == 0
                ? string.Empty
                : string.Join(" ", result.Warnings);
            RefreshSummaryProperties();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer target replaced this request.
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or TaskCanceledException
                or InvalidDataException
                or JsonException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                DataStatus = "System data is unavailable: " + exception.Message;
            }
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    private void RefreshSummaryProperties()
    {
        OnPropertyChanged(nameof(DiscoveryText));
        OnPropertyChanged(nameof(HasDiscoveryText));
        OnPropertyChanged(nameof(TrafficText));
        OnPropertyChanged(nameof(HasTraffic));
        OnPropertyChanged(nameof(PointsOfInterestText));
        OnPropertyChanged(nameof(HasPointsOfInterest));
        RefreshDetailLines();
    }

    private void RefreshDetailLines()
    {
        var lines = new List<JumpInfoDetailLineViewModel>();
        if (summary is not null)
        {
            lines.AddRange(summary.Specials.Select(special =>
                new JumpInfoDetailLineViewModel(
                    special.Location,
                    string.Join(" \u2022 ", special.Details))));
        }

        AppendGuardianSiteDetails(lines);
        AppendFollowedRouteDetails(lines);
        AppendRegionDetails(lines);

        DetailLines = lines;
        OnPropertyChanged(nameof(HasDetailLines));
        OnPropertyChanged(nameof(HasRefuelGuidance));
        OnPropertyChanged(nameof(HasNeutronGuidance));
        OnPropertyChanged(nameof(HasRouteGuidanceBadges));
        OnPropertyChanged(nameof(HasDiscoveryOrRouteGuidance));
    }

    private void AppendGuardianSiteDetails(
        List<JumpInfoDetailLineViewModel> lines)
    {
        if (routePlan?.Target is not { } target)
        {
            return;
        }

        var sites = target.SystemAddress > 0
            ? guardianSites.FindBySystemAddress(target.SystemAddress)
            : [];
        var ruins = sites.Count(site => site.Kind == GuardianSiteKind.Ruins);
        var structures = sites.Count(
            site => site.Kind == GuardianSiteKind.Structure);
        var beacons = sites.Count(site => site.Kind == GuardianSiteKind.Beacon);
        var guardianDetails = new List<string>();
        if (ruins > 0)
        {
            guardianDetails.Add($"{ruins:N0} ruins");
        }

        if (structures > 0)
        {
            guardianDetails.Add($"{structures:N0} structures");
        }

        if (beacons > 0)
        {
            guardianDetails.Add(
                $"{beacons:N0} beacon{(beacons == 1 ? string.Empty : "s")}");
        }

        if (guardianDetails.Count > 0)
        {
            lines.Add(new JumpInfoDetailLineViewModel(
                "Guardian",
                string.Join(" \u2022 ", guardianDetails)));
        }
    }

    private void AppendFollowedRouteDetails(
        List<JumpInfoDetailLineViewModel> lines)
    {
        if (routePlan?.Target is not { } target)
        {
            return;
        }

        var nextHop = displayedFollowedRoute?.NextHop;
        if (nextHop is null || !MatchesTarget(nextHop, target))
        {
            return;
        }

        var routeDetails = new List<string>
        {
            CreateFollowedRouteProgressText() ?? "ROUTE TARGET",
        };
        if (!string.IsNullOrWhiteSpace(nextHop.Notes))
        {
            routeDetails.Add(nextHop.Notes);
        }

        lines.Add(new JumpInfoDetailLineViewModel(
            "Followed route",
            string.Join(" \u2022 ", routeDetails),
            nextHop.Refuel,
            nextHop.Neutron));
    }

    private void AppendRegionDetails(List<JumpInfoDetailLineViewModel> lines)
    {
        if (routePlan is null)
        {
            return;
        }

        var targetPosition = routePlan.TargetPosition ?? summary?.Position;
        if (currentPosition is not { } origin
            || targetPosition is not { } destination
            || GalacticRegionMap.Find(origin) is not { } currentRegion
            || GalacticRegionMap.Find(destination) is not { } nextRegion
            || currentRegion.Id == nextRegion.Id)
        {
            return;
        }

        lines.Add(new JumpInfoDetailLineViewModel(
            "Now entering",
            nextRegion.Name));
    }

    private bool IsSelectedFollowedRouteHop()
    {
        if (status is null
            || followedRoute?.NextHop is not { } nextHop
            || status.Destination is not { } destination)
        {
            return false;
        }

        var mode = OverlayGameModeResolver.Resolve(
            status,
            fsdJumping,
            musicTrack);
        var isVisibleMode = mode == OverlayGameMode.SuperCruising;
        var destinationMatches = destination.System > 0
                && destination.System == nextHop.SystemAddress
            || !string.IsNullOrWhiteSpace(destination.Name)
                && string.Equals(
                    destination.Name,
                    nextHop.Name,
                    StringComparison.OrdinalIgnoreCase);
        return isVisibleMode && destinationMatches;
    }

    private void SavePreferences()
    {
        try
        {
            settingsStore.Save(new JumpInfoPreferences(
                AutoShow,
                Minimal,
                ShowWhenNextHopSelected,
                UseSpanshLastUpdated));
            SettingsStatus = string.Empty;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            SettingsStatus = "Jump overlay preferences changed for this session "
                + "but could not be saved: " + exception.Message;
        }
    }

    private void RaisePlanProperties()
    {
        OnPropertyChanged(nameof(TargetName));
        OnPropertyChanged(nameof(IsQuestTagged));
        OnPropertyChanged(nameof(StarClass));
        OnPropertyChanged(nameof(IsScoopableStarClass));
        OnPropertyChanged(nameof(JumpProgress));
        OnPropertyChanged(nameof(TotalDistance));
        OnPropertyChanged(nameof(RouteLegs));
        OnPropertyChanged(nameof(TargetLegIndex));
        RaiseVisibilityProperties();
    }

    private string? CreateFollowedRouteProgressText()
    {
        if (followedRouteFinishedUntil is { } finishedUntil
            && (finishedUntil > timeProvider.GetUtcNow()
                || isWaitingForOverlayHide
                || hasQueuedPlan))
        {
            return "FINISHED";
        }

        if (displayedFollowedRoute?.NextHop is not { } nextHop
            || routePlan?.Target is not { } target
            || !MatchesTarget(nextHop, target))
        {
            return null;
        }

        var totalHops = Math.Max(0, displayedFollowedRoute.Hops.Count - 1);
        var nextIndex = displayedFollowedRoute.LastReachedIndex + 1;
        return nextIndex <= 0
            ? "START"
            : $"HOP {Math.Min(nextIndex, totalHops):N0} / {totalHops:N0}";
    }

    private bool IsFinalFollowedRouteArrival(JsonElement payload)
    {
        if (followedRoute is not
            {
                IsActive: true,
                NextHop: { } nextHop,
                Hops.Count: > 0,
            }
            || followedRoute.LastReachedIndex + 2 != followedRoute.Hops.Count)
        {
            return false;
        }

        var systemName = GetString(payload, "StarSystem");
        var systemAddress = GetInt64(payload, "SystemAddress");
        return systemAddress is > 0
                && nextHop.SystemAddress == systemAddress
            || !string.IsNullOrWhiteSpace(systemName)
                && string.Equals(
                    nextHop.Name,
                    systemName,
                    StringComparison.OrdinalIgnoreCase);
    }

    private void RaiseVisibilityProperties()
    {
        OnPropertyChanged(nameof(ShouldShow));
    }

    private void QueuePlanForNextPresentation()
    {
        hasQueuedPlan = routePlan is not null;
        if (!hasQueuedPlan)
        {
            followedRouteFinishedUntil = null;
            RefreshPlan();
        }
    }

    private void CaptureInFlightTarget()
    {
        var target = JumpInfoRoutePlanner.SelectTarget(fsdTarget, status)
            ?? routePlan?.Target;
        if (target is null)
        {
            inFlightTarget = null;
            return;
        }

        var starClass = target.StarClass;
        if (string.IsNullOrWhiteSpace(starClass)
            && routePlan is { } displayedPlan
            && SameTarget(displayedPlan.Target, target))
        {
            starClass = string.IsNullOrWhiteSpace(
                displayedPlan.Target.StarClass)
                    ? summary?.StarClass
                    : displayedPlan.Target.StarClass;
        }

        inFlightTarget = target with { StarClass = starClass };
    }

    private JumpInfoRoutePlan? PreserveInFlightStarClass(
        JumpInfoRoutePlan? plan)
    {
        if (!fsdJumping || plan is null)
        {
            return plan;
        }

        if (inFlightTarget is null)
        {
            inFlightTarget = plan.Target;
        }

        if (!SameTarget(inFlightTarget, plan.Target))
        {
            return plan;
        }

        if (string.IsNullOrWhiteSpace(inFlightTarget.StarClass))
        {
            inFlightTarget = inFlightTarget with
            {
                StarClass = plan.Target.StarClass,
            };
        }

        return string.IsNullOrWhiteSpace(inFlightTarget.StarClass)
            ? plan
            : plan with
            {
                Target = plan.Target with
                {
                    StarClass = inFlightTarget.StarClass,
                },
            };
    }

    private static string CreateDiscoveryText(SystemSummary? value)
    {
        if (value is null || value.IsKnown is null)
        {
            return string.Empty;
        }

        if (value.IsKnown == false
            || value.TotalBodyCount == 0 && value.LastUpdatedAt is null)
        {
            return "Undiscovered system";
        }

        var discovered = value.DiscoveredAt is { } discoveredAt
            ? FormatDiscoveredText(value, discoveredAt)
            : FormatScanStatusText(value);
        return AppendUpdatedSuffix(value, discovered);
    }

    private static string FormatScanStatusText(SystemSummary value)
    {
        if (value.TotalBodyCount == 0)
        {
            return "Unscanned system";
        }

        return value.ScannedBodyCount >= value.TotalBodyCount
            ? $"All {value.TotalBodyCount:N0} bodies reported"
            : $"{value.ScannedBodyCount:N0} of {value.TotalBodyCount:N0} bodies reported";
    }

    private static string FormatDiscoveredText(
        SystemSummary value,
        DateTimeOffset discoveredAt)
    {
        var byline = string.IsNullOrWhiteSpace(value.DiscoveredBy)
            ? string.Empty
            : " by " + value.DiscoveredBy;
        return "Discovered" + byline + $" on {discoveredAt.ToLocalTime():g}";
    }

    private static string AppendUpdatedSuffix(
        SystemSummary value,
        string discovered)
    {
        if (value.LastUpdatedAt is not { } updated)
        {
            return discovered;
        }

        if (value.DiscoveredAt is not null && updated <= value.DiscoveredAt)
        {
            return discovered;
        }

        return discovered + $" \u2022 updated {updated.ToLocalTime():g}";
    }

    private static string CreatePointsOfInterestText(SystemSummary? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var points = value.PointsOfInterest;
        var values = new List<string>();
        AddCount(values, "Bodies", points.Bodies);
        AddCount(values, "Genus", points.Genus);
        AddCount(values, "Starports", points.Starports);
        AddCount(values, "Outposts", points.Outposts);
        AddCount(values, "Settlements", points.Settlements);
        AddCount(values, "Fleet carriers", points.FleetCarriers);
        AddCount(values, "Wars", points.Wars);
        return string.Join(" \u2022 ", values);
    }

    private static void AddCount(List<string> values, string label, int count)
    {
        if (count > 0)
        {
            values.Add($"{label}: {count:N0}");
        }
    }

    private static JumpTarget? ParseTarget(JsonElement root)
    {
        var name = GetString(root, "Name");
        var address = GetInt64(root, "SystemAddress") ?? 0;
        return string.IsNullOrWhiteSpace(name)
            ? null
            : new JumpTarget(
                name,
                address,
                GetString(root, nameof(StarClass)));
    }

    private static bool MatchesTarget(FollowRouteHop hop, JumpTarget target)
    {
        return target.SystemAddress > 0 && hop.SystemAddress == target.SystemAddress
            || string.Equals(hop.Name, target.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameTarget(JumpTarget? left, JumpTarget? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return left is not null
            && right is not null
            && MatchesTarget(
                new FollowRouteHop(
                    left.Name,
                    left.SystemAddress,
                    null,
                    null,
                    false,
                    false),
                right);
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var result)
                ? result
                : null;
    }

    private static double? GetDouble(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var result)
            && double.IsFinite(result)
                ? result
                : null;
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

public sealed record JumpInfoApplyUpdateRequest(
    string? CurrentSystemName,
    long? CurrentSystemAddress,
    GalacticCoordinate? CurrentPosition,
    NavRouteSnapshot? NavRoute,
    IReadOnlyList<JournalEventEnvelope> JournalEvents,
    EliteStatus? Status,
    FollowRouteDocument? FollowedRoute,
    bool IsBootstrapRead = false);

public sealed record JumpInfoDetailLineViewModel(
    string Label,
    string Value,
    bool Refuel = false,
    bool Neutron = false)
{
    public bool HasRouteBadges => Refuel || Neutron;
}
