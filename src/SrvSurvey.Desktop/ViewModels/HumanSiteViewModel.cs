using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Settlements;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class HumanSiteViewModel : INotifyPropertyChanged
{
    public const double ShipCallLimitMeters = 500;
    public const double ShipDismissalWarningMeters = 1_800;
    public const double ShipDismissalLimitMeters = 2_000;
    public const double OriginWarningDistanceMeters = 300;
    private const string ProfileAnalyser = "$humanoid_companalyser_name;";
    private readonly HumanSiteLiveState state;
    private readonly HumanSiteMapProjector mapProjector;
    private readonly HumanSiteNavigation navigation;
    private readonly HumanSiteVehicleTracker vehicleTracker = new();
    private readonly HumanSiteActivityTracker activityTracker = new();
    private readonly HumanSiteSettingsStore? settingsStore;
    private readonly HumanSiteKnowledgeStore? knowledgeStore;
    private readonly HumanSiteMaterialStore? materialStore;
    private EliteStatus? status;
    private string? frontierId;
    private string? commanderName;
    private string? systemName;
    private long systemAddress;
    private GalacticCoordinate? starPosition;
    private string? vehicle;
    private string? loadedSiteKey;
    private string? loadedMaterialSiteKey;
    private bool activeBuildProjects;
    private bool stationInfoVisible;
    private bool autoZoom = true;
    private bool isHuge;
    private double zoom;
    private bool autoShow;
    private int preferredWidth;
    private int preferredHeight;
    private double shipZoom;
    private double srvZoom;
    private double footZoom;
    private bool autoZoomInside;
    private double insideZoom;
    private bool autoZoomTool;
    private double toolZoom;
    private bool showMedkits;
    private bool showBatteries;
    private bool showDataTerminals;
    private bool showCollectedMaterials;
    private bool trackMaterialCollection;
    private bool suppressForActiveBuildProjects;
    private string statusMessage = "Waiting to approach a human settlement.";
    private string settingsStatus = string.Empty;

    public HumanSiteViewModel(
        HumanSiteSettingsStore? settingsStore = null,
        HumanSiteKnowledgeStore? knowledgeStore = null,
        HumanSiteMaterialStore? materialStore = null,
        HumanSiteTemplateCatalog? templateCatalog = null)
    {
        this.settingsStore = settingsStore;
        this.knowledgeStore = knowledgeStore;
        this.materialStore = materialStore;
        var templates = templateCatalog
            ?? HumanSiteTemplateCatalog.LoadEmbedded();
        state = new HumanSiteLiveState(templates);
        mapProjector = new HumanSiteMapProjector();
        navigation = new HumanSiteNavigation(templates);
        var preferences = settingsStore?.Load()
            ?? HumanSitePreferences.Default;
        autoShow = preferences.AutoShow;
        preferredWidth = preferences.Width;
        preferredHeight = preferences.Height;
        shipZoom = preferences.ShipZoom;
        srvZoom = preferences.SrvZoom;
        footZoom = preferences.FootZoom;
        autoZoomInside = preferences.AutoZoomInside;
        insideZoom = preferences.InsideZoom;
        autoZoomTool = preferences.AutoZoomTool;
        toolZoom = preferences.ToolZoom;
        showMedkits = preferences.ShowMedkits;
        showBatteries = preferences.ShowBatteries;
        showDataTerminals = preferences.ShowDataTerminals;
        showCollectedMaterials = preferences.ShowCollectedMaterials;
        trackMaterialCollection = preferences.TrackMaterialCollection;
        suppressForActiveBuildProjects =
            preferences.SuppressForActiveBuildProjects;
        zoom = shipZoom;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public HumanSiteLiveSnapshot? ActiveSite => state.CurrentSite;

    public HumanSiteMapProjection? MapProjection => ActiveSite?.Template is { } template
        ? mapProjector.Project(
            template,
            new HumanSiteMapDisplayOptions(
                ShowMedkits,
                ShowBatteries,
                ShowDataTerminals,
                ActiveSite.FactionState is "War" or "CivilWar"))
        : null;

    public bool HasKnownType => ActiveSite?.Template is not null;

    public bool HasKnownGeometry => ActiveSite is
    { Template: not null, Heading: not null };

    public string SiteName => ActiveSite?.LocalizedName
        ?? ActiveSite?.Name
        ?? "Human settlement";

    public string TemplateText => ActiveSite is { } site
        ? site.Template is { } template
            ? $"{site.Economy} #{site.SubType} · {template.Name}"
            : $"{site.Economy} · type not identified"
        : "Settlement type unavailable";

    public string GeometryStatus => ActiveSite switch
    {
        null => "No active settlement",
        { SubType: 0, Heading: null } => "Settlement type and heading unknown",
        { Heading: null } => "Settlement heading unknown",
        { Template: null } => "Settlement template unavailable",
        _ => "Settlement map aligned",
    };

    public string FactionText => ActiveSite is { } site
        && !string.IsNullOrWhiteSpace(site.FactionName)
            ? string.IsNullOrWhiteSpace(site.FactionState)
                ? site.FactionName
                : $"{site.FactionName} · {site.FactionState}"
            : "Controlling faction unavailable";

    public string GovernmentText => ActiveSite is { } site
        && !string.IsNullOrWhiteSpace(site.GovernmentLocalized)
            ? site.GovernmentLocalized
            : string.Empty;

    public bool IsAnarchy => ActiveSite?.Government == "$government_Anarchy;";

    public bool HasInterstellarFactors => ActiveSite?.Services.Contains(
        "facilitator",
        StringComparer.OrdinalIgnoreCase) == true;

    public HumanSiteDockingStatus DockingStatus =>
        ActiveSite?.Docking ?? HumanSiteDockingStatus.None;

    public string DockingStatusText => ActiveSite?.Docking switch
    {
        HumanSiteDockingStatus.Requested => "Docking requested",
        HumanSiteDockingStatus.Granted =>
            $"Docking granted · pad {ActiveSite.GrantedPad}",
        HumanSiteDockingStatus.Denied => string.IsNullOrWhiteSpace(
            ActiveSite.DockingDeniedReason)
                ? "Docking denied"
                : $"Docking denied · {ActiveSite.DockingDeniedReason}",
        HumanSiteDockingStatus.Docked => "Docked",
        _ => string.Empty,
    };

    public bool HasDockingStatus => !string.IsNullOrWhiteSpace(DockingStatusText);

    public HumanSiteMapPoint? CommanderOffset { get; private set; }

    public IReadOnlySet<int> ProcessedTerminalIndexes =>
        activityTracker.ProcessedTerminalIndexes;

    public IReadOnlyList<HumanSiteMapPoint> ProcessedTerminalOffsets =>
        ActiveSite?.Template is { } template
            ? activityTracker.ProcessedTerminalIndexes
                .Where(index => index >= 0
                    && index < template.DataTerminals.Count)
                .Select(index => template.DataTerminals[index].Offset)
                .ToArray()
            : [];

    public IReadOnlyList<HumanSiteCollectedMaterial> CollectedMaterials =>
        ShowCollectedMaterials
            ? activityTracker.CollectedMaterials
            : [];

    public int CollectedMaterialLocationCount =>
        activityTracker.CollectedMaterials.Count;

    public HumanSiteMapPoint? ShipOffset { get; private set; }

    public HumanSiteMapPoint? SrvOffset { get; private set; }

    public bool HasShipDeparted => vehicleTracker.HasShipDeparted;

    public double DistanceToShipMeters { get; private set; }

    public bool ShowShipDismissalBoundary => ShipOffset is not null
        && !HasShipDeparted
        && status is { } currentStatus
        && (currentStatus.OnFoot || currentStatus.InSrv);

    public bool ShowShipDismissalWarning => ShowShipDismissalBoundary
        && DistanceToShipMeters > ShipDismissalWarningMeters;

    public string ShipDismissalWarningText =>
        $"Ship dismissal range nearby · {DistanceToShipMeters:N0} m / "
        + $"{ShipDismissalLimitMeters:N0} m";

    public double DistanceToOriginMeters { get; private set; }

    public double ApproachDistanceMeters { get; private set; }

    public double RelativeHeading { get; private set; }

    public string DistanceText => ActiveSite is null
        ? string.Empty
        : $"{DistanceToOriginMeters:N0} m from origin";

    public string ApproachDistanceText => ActiveSite is null
        ? string.Empty
        : $"{ApproachDistanceMeters:N0} m approach distance";

    public string CommanderPositionText => CommanderOffset is { } offset
        ? $"x {offset.X:N1} m · y {offset.Y:N1} m · {RelativeHeading:N0}°"
        : "Settlement-relative position unavailable";

    public bool ShowOriginWarning => HasKnownGeometry
        && DistanceToOriginMeters > OriginWarningDistanceMeters;

    public bool ShouldShow => AutoShow
        && ActiveSite is not null
        && status?.HasLatitudeLongitude == true
        && IsStatusEligible(status)
        && !stationInfoVisible
        && !(SuppressForActiveBuildProjects && activeBuildProjects);

    public bool AutoShow
    {
        get => autoShow;
        set => SetPreference(ref autoShow, value);
    }

    public int PreferredWidth
    {
        get => preferredWidth;
        set => SetPreference(ref preferredWidth, Math.Clamp(value, 320, 1600));
    }

    public int PreferredHeight
    {
        get => preferredHeight;
        set => SetPreference(ref preferredHeight, Math.Clamp(value, 320, 1400));
    }

    public double ShipZoom
    {
        get => shipZoom;
        set => SetZoomPreference(ref shipZoom, value);
    }

    public double SrvZoom
    {
        get => srvZoom;
        set => SetZoomPreference(ref srvZoom, value);
    }

    public double FootZoom
    {
        get => footZoom;
        set => SetZoomPreference(ref footZoom, value);
    }

    public bool AutoZoomInside
    {
        get => autoZoomInside;
        set => SetPreference(ref autoZoomInside, value);
    }

    public double InsideZoom
    {
        get => insideZoom;
        set => SetZoomPreference(ref insideZoom, value);
    }

    public bool AutoZoomTool
    {
        get => autoZoomTool;
        set => SetPreference(ref autoZoomTool, value);
    }

    public double ToolZoom
    {
        get => toolZoom;
        set => SetZoomPreference(ref toolZoom, value);
    }

    public bool ShowMedkits
    {
        get => showMedkits;
        set => SetMapPreference(ref showMedkits, value);
    }

    public bool ShowBatteries
    {
        get => showBatteries;
        set => SetMapPreference(ref showBatteries, value);
    }

    public bool ShowDataTerminals
    {
        get => showDataTerminals;
        set => SetMapPreference(ref showDataTerminals, value);
    }

    public bool ShowCollectedMaterials
    {
        get => showCollectedMaterials;
        set
        {
            if (SetField(ref showCollectedMaterials, value))
            {
                SavePreferences();
                OnPropertyChanged(nameof(CollectedMaterials));
            }
        }
    }

    public bool TrackMaterialCollection
    {
        get => trackMaterialCollection;
        set => SetPreference(ref trackMaterialCollection, value);
    }

    public bool SuppressForActiveBuildProjects
    {
        get => suppressForActiveBuildProjects;
        set => SetPreference(ref suppressForActiveBuildProjects, value);
    }

    public bool AutoZoom
    {
        get => autoZoom;
        private set
        {
            if (SetField(ref autoZoom, value))
            {
                OnPropertyChanged(nameof(ZoomText));
            }
        }
    }

    public double Zoom
    {
        get => zoom;
        private set
        {
            if (SetField(ref zoom, value))
            {
                OnPropertyChanged(nameof(ZoomText));
            }
        }
    }

    public string ZoomText => AutoZoom
        ? $"Auto · {Zoom:F1}×"
        : $"{Zoom:F1}×";

    public bool IsHuge
    {
        get => isHuge;
        private set => SetField(ref isHuge, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public string SettingsStatus
    {
        get => settingsStatus;
        private set => SetField(ref settingsStatus, value);
    }

    public void UpdateContext(
        string? currentFrontierId,
        string? currentCommanderName,
        string? currentSystemName,
        long currentSystemAddress,
        GalacticCoordinate? currentStarPosition)
    {
        if (systemAddress != currentSystemAddress
            || !string.Equals(
                frontierId,
                currentFrontierId,
                StringComparison.Ordinal)
            || !string.Equals(
                systemName,
                currentSystemName,
                StringComparison.OrdinalIgnoreCase))
        {
            loadedSiteKey = null;
            loadedMaterialSiteKey = null;
        }

        frontierId = currentFrontierId;
        commanderName = currentCommanderName;
        systemName = currentSystemName;
        systemAddress = currentSystemAddress;
        starPosition = currentStarPosition;
    }

    public async Task ApplyUpdateAsync(
        IEnumerable<JournalEventEnvelope> journalEvents,
        EliteStatus? currentStatus,
        string? currentVehicle = null)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        if (currentStatus is not null)
        {
            status = currentStatus;
        }

        vehicle = currentVehicle ?? vehicle;
        var versionBefore = state.Version;
        var source = HumanSiteGeometrySource.Unknown;
        var addedMaterials = new List<HumanSiteCollectedMaterial>();
        var completeMaterialSurvey = false;
        foreach (var journalEvent in journalEvents)
        {
            state.Apply(journalEvent);
            vehicleTracker.Apply(journalEvent, status);
            if (journalEvent.EventName == "ApproachSettlement"
                && state.CurrentSite is not null)
            {
                await LoadKnowledgeAsync();
            }

            var inferredSource = TryInferGeometry();
            if (inferredSource != HumanSiteGeometrySource.Unknown)
            {
                source = inferredSource;
            }

            var activity = activityTracker.Apply(
                journalEvent,
                state.CurrentSite,
                status,
                TrackMaterialCollection);
            addedMaterials.AddRange(activity.AddedMaterials);
            if (journalEvent.EventName == "ApproachSettlement"
                && state.CurrentSite is not null)
            {
                await LoadMaterialSurveyAsync();
            }

            completeMaterialSurvey |= IsStopMaterialSurveyCommand(journalEvent);
        }

        if (state.CurrentSite is null)
        {
            loadedSiteKey = null;
            loadedMaterialSiteKey = null;
        }

        var finalSource = TryInferGeometry();
        if (finalSource != HumanSiteGeometrySource.Unknown)
        {
            source = finalSource;
        }

        if (addedMaterials.Count > 0)
        {
            await SaveMaterialActivityAsync(addedMaterials);
        }

        if (completeMaterialSurvey)
        {
            await CompleteMaterialSurveyAsync();
        }

        UpdateNavigation();
        if (AutoZoom)
        {
            ApplyAutomaticZoom();
        }

        if (state.Version != versionBefore && state.CurrentSite is { } site)
        {
            await SaveKnowledgeAsync(site, source);
        }

        NotifySiteState();
    }

    public void UpdateStatus(EliteStatus? currentStatus, string? currentVehicle = null)
    {
        status = currentStatus;
        vehicle = currentVehicle ?? vehicle;
        if (AutoZoom)
        {
            ApplyAutomaticZoom();
        }

        UpdateNavigation();
        NotifySiteState();
    }

    public void SetActiveBuildProjects(bool value)
    {
        if (activeBuildProjects != value)
        {
            activeBuildProjects = value;
            OnPropertyChanged(nameof(ShouldShow));
        }
    }

    public void SetStationInfoVisible(bool value)
    {
        if (stationInfoVisible != value)
        {
            stationInfoVisible = value;
            OnPropertyChanged(nameof(ShouldShow));
        }
    }

    public void AdjustZoom(bool zoomIn)
    {
        var next = Math.Round(Zoom + (zoomIn ? 0.2 : -0.2), 1);
        if (next is < 0.2 or > 15)
        {
            return;
        }

        var automatic = GetAutomaticZoom();
        AutoZoom = automatic is not null
            && Math.Abs(next - automatic.Value) < 0.001;
        Zoom = next;
    }

    public void EnableAutomaticZoom()
    {
        AutoZoom = true;
        ApplyAutomaticZoom();
    }

    public void ToggleHuge()
    {
        IsHuge = !IsHuge;
    }

    private HumanSiteGeometrySource TryInferGeometry()
    {
        if (state.CurrentSite is not { } site
            || status is not { HasLatitudeLongitude: true } currentStatus
            || currentStatus.PlanetRadius <= 0
            || !(currentStatus.Docked
                || currentStatus.Landed
                || currentStatus.OnFoot
                || currentStatus.InSrv
                || currentStatus.InTaxi))
        {
            return HumanSiteGeometrySource.Unknown;
        }

        var activeVehicle = currentStatus.InTaxi
            ? "taxi"
            : currentStatus.OnFoot
                ? "foot"
                : vehicle;
        var source = currentStatus.InTaxi
            ? HumanSiteGeometrySource.TaxiDock
            : currentStatus.OnFoot || currentStatus.InSrv
                ? HumanSiteGeometrySource.ManualFoot
                : HumanSiteGeometrySource.AutoDock;
        var geometry = navigation.InferGeometry(
            site,
            new SurfaceCoordinate(
                currentStatus.Latitude,
                currentStatus.Longitude),
            currentStatus.NormalizedHeading,
            (double)currentStatus.PlanetRadius,
            activeVehicle,
            site.GrantedPad);
        return geometry is not null && state.ApplyGeometry(geometry)
            ? source
            : HumanSiteGeometrySource.Unknown;
    }

    private void UpdateNavigation()
    {
        CommanderOffset = null;
        ShipOffset = null;
        SrvOffset = null;
        DistanceToShipMeters = 0;
        DistanceToOriginMeters = 0;
        ApproachDistanceMeters = 0;
        RelativeHeading = 0;
        if (ActiveSite is not { } site
            || status is not { HasLatitudeLongitude: true } currentStatus
            || currentStatus.PlanetRadius <= 0)
        {
            return;
        }

        var origin = new SurfaceCoordinate(
            site.Location.Latitude,
            site.Location.Longitude);
        var current = new SurfaceCoordinate(
            currentStatus.Latitude,
            currentStatus.Longitude);
        DistanceToOriginMeters = SurfaceNavigation.GetDistance(
            origin,
            current,
            (double)currentStatus.PlanetRadius);
        ApproachDistanceMeters = Math.Sqrt(
            (DistanceToOriginMeters * DistanceToOriginMeters)
            + (currentStatus.Altitude * currentStatus.Altitude));
        if (site.Heading is { } heading)
        {
            CommanderOffset = HumanSiteNavigation.GetSiteOffset(
                origin,
                current,
                (double)currentStatus.PlanetRadius,
                heading);
            RelativeHeading = SurfaceNavigation.NormalizeDegrees(
                currentStatus.NormalizedHeading - heading);
            UpdateVehicleNavigation(currentStatus, origin, current, heading);
        }
    }

    private void UpdateVehicleNavigation(
        EliteStatus currentStatus,
        SurfaceCoordinate origin,
        SurfaceCoordinate current,
        double heading)
    {
        var bodyRadius = (double)currentStatus.PlanetRadius;
        if (vehicleTracker.ShipLocation is { } observedShip)
        {
            var shipLocation = vehicleTracker.ShipHeading is { } shipHeading
                ? HumanSiteNavigation.AdjustForVehicle(
                    observedShip,
                    shipHeading,
                    bodyRadius,
                    vehicle)
                : observedShip;
            ShipOffset = HumanSiteNavigation.GetSiteOffset(
                origin,
                shipLocation,
                bodyRadius,
                heading);
            DistanceToShipMeters = SurfaceNavigation.GetDistance(
                current,
                observedShip,
                bodyRadius);
        }

        if (vehicleTracker.SrvLocation is { } srvLocation)
        {
            SrvOffset = HumanSiteNavigation.GetSiteOffset(
                origin,
                srvLocation,
                bodyRadius,
                heading);
        }
    }

    private async Task LoadKnowledgeAsync()
    {
        if (knowledgeStore is null
            || ActiveSite is not { } site
            || CreateKnowledgeContext() is not { } context)
        {
            return;
        }

        var key = $"{site.SystemAddress}/{site.MarketId}";
        if (string.Equals(key, loadedSiteKey, StringComparison.Ordinal))
        {
            return;
        }

        loadedSiteKey = key;
        try
        {
            var result = await knowledgeStore.LoadAsync(context, site.MarketId);
            if (result.Knowledge is not null)
            {
                state.ApplyKnowledge(result.Knowledge);
                StatusMessage = "Loaded saved settlement type and alignment.";
            }
            else if (result.Error is not null)
            {
                StatusMessage = result.Error;
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            StatusMessage =
                $"Settlement geometry could not be loaded: {exception.Message}";
        }
    }

    private async Task SaveKnowledgeAsync(
        HumanSiteLiveSnapshot site,
        HumanSiteGeometrySource source)
    {
        if (knowledgeStore is null || CreateKnowledgeContext() is not { } context)
        {
            return;
        }

        try
        {
            await knowledgeStore.SaveAsync(context, site, source);
            StatusMessage = site.Heading is not null
                ? "Settlement type and alignment saved."
                : "Settlement visit recorded; alignment is still unknown.";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            StatusMessage =
                $"Settlement geometry could not be saved: {exception.Message}";
        }
    }

    private async Task LoadMaterialSurveyAsync()
    {
        if (materialStore is null
            || ActiveSite is not { } site
            || CreateMaterialContext(site) is not { } context)
        {
            return;
        }

        var key = $"{site.SystemAddress}/{site.MarketId}";
        if (string.Equals(key, loadedMaterialSiteKey, StringComparison.Ordinal))
        {
            return;
        }

        loadedMaterialSiteKey = key;
        try
        {
            var result = await materialStore.LoadActiveAsync(context);
            if (result.Survey is { } survey)
            {
                activityTracker.ReplaceCollectedMaterials(survey.Materials);
            }

            if (result.Error is not null)
            {
                StatusMessage = result.Error;
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            StatusMessage =
                $"Settlement material survey could not be loaded: {exception.Message}";
        }
    }

    private async Task SaveMaterialActivityAsync(
        IReadOnlyList<HumanSiteCollectedMaterial> materials)
    {
        if (materialStore is null
            || !TrackMaterialCollection
            || ActiveSite is not { } site
            || CreateMaterialContext(site) is not { } context)
        {
            return;
        }

        try
        {
            await materialStore.AppendAsync(context, materials);
            StatusMessage =
                $"Recorded {materials.Count:N0} settlement material location(s).";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            StatusMessage =
                $"Settlement material survey could not be saved: {exception.Message}";
        }
    }

    private async Task CompleteMaterialSurveyAsync()
    {
        if (materialStore is null
            || !TrackMaterialCollection
            || ActiveSite is not { } site
            || CreateMaterialContext(site) is not { } context)
        {
            return;
        }

        try
        {
            await materialStore.CompleteAsync(context);
            loadedMaterialSiteKey = null;
            StatusMessage = "Settlement material survey completed.";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            StatusMessage =
                $"Settlement material survey could not be completed: {exception.Message}";
        }
    }

    private HumanSiteMaterialContext? CreateMaterialContext(
        HumanSiteLiveSnapshot site)
    {
        return !string.IsNullOrWhiteSpace(frontierId)
            ? new HumanSiteMaterialContext(frontierId, site)
            : null;
    }

    private static bool IsStopMaterialSurveyCommand(
        JournalEventEnvelope journalEvent)
    {
        return journalEvent.EventName == "SendText"
            && journalEvent.Payload.TryGetProperty("Message", out var message)
            && message.ValueKind == System.Text.Json.JsonValueKind.String
            && string.Equals(
                message.GetString()?.Trim(),
                ".stop",
                StringComparison.OrdinalIgnoreCase);
    }

    private HumanSiteKnowledgeContext? CreateKnowledgeContext()
    {
        return !string.IsNullOrWhiteSpace(frontierId)
            && !string.IsNullOrWhiteSpace(systemName)
            && systemAddress > 0
                ? new HumanSiteKnowledgeContext(
                    frontierId,
                    commanderName,
                    systemName,
                    systemAddress,
                    starPosition,
                    status?.PlanetRadius is > 0
                        ? (double)status.PlanetRadius
                        : 0)
                : null;
    }

    private double? GetAutomaticZoom()
    {
        if (status is not { } currentStatus)
        {
            return null;
        }

        if (AutoZoomTool
            && string.Equals(
                currentStatus.SelectedWeapon,
                ProfileAnalyser,
                StringComparison.Ordinal))
        {
            return ToolZoom;
        }

        if (AutoZoomInside
            && currentStatus.OnFootOnPlanet
            && !currentStatus.OnFootExterior)
        {
            return InsideZoom;
        }

        if (currentStatus.OnFoot || currentStatus.OnFootExterior)
        {
            return FootZoom;
        }

        if (currentStatus.InSrv)
        {
            return SrvZoom;
        }

        if (currentStatus.Landed
            || currentStatus.Docked
            || currentStatus.InMainShip
                && !currentStatus.Flags.HasFlag(StatusFlags.Supercruise))
        {
            return DistanceToOriginMeters < 2_500
                ? ShipZoom
                : DistanceToOriginMeters < 4_000
                    ? 0.2
                    : 0.1;
        }

        return null;
    }

    private void ApplyAutomaticZoom()
    {
        UpdateNavigation();
        if (GetAutomaticZoom() is { } automatic)
        {
            Zoom = automatic;
        }
    }

    private static bool IsStatusEligible(EliteStatus currentStatus)
    {
        if (currentStatus.GuiFocus is GuiFocus.ExternalPanel or GuiFocus.RolePanel)
        {
            return true;
        }

        if (currentStatus.GuiFocus != GuiFocus.NoFocus)
        {
            return false;
        }

        var flying = currentStatus.InMainShip
            && !currentStatus.Docked
            && !currentStatus.Landed
            && !currentStatus.Flags.HasFlag(StatusFlags.Supercruise)
            && !currentStatus.GlideMode;
        return currentStatus.OnFoot
            || currentStatus.InSrv
            || currentStatus.InTaxi
            || currentStatus.Docked
            || currentStatus.Landed
            || currentStatus.GlideMode
            || flying;
    }

    private void SetZoomPreference(
        ref double field,
        double value,
        [CallerMemberName] string? propertyName = null)
    {
        if (!double.IsFinite(value))
        {
            return;
        }

        SetPreference(
            ref field,
            Math.Clamp(value, 0.2, 15),
            propertyName);
        if (AutoZoom)
        {
            ApplyAutomaticZoom();
        }
    }

    private void SetMapPreference(
        ref bool field,
        bool value,
        [CallerMemberName] string? propertyName = null)
    {
        if (SetField(ref field, value, propertyName))
        {
            SavePreferences();
            OnPropertyChanged(nameof(MapProjection));
        }
    }

    private void SetPreference<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (SetField(ref field, value, propertyName))
        {
            SavePreferences();
            OnPropertyChanged(nameof(ShouldShow));
        }
    }

    private void SavePreferences()
    {
        if (settingsStore is null)
        {
            return;
        }

        try
        {
            settingsStore.Save(new HumanSitePreferences(
                AutoShow,
                PreferredWidth,
                PreferredHeight,
                ShipZoom,
                SrvZoom,
                FootZoom,
                AutoZoomInside,
                InsideZoom,
                AutoZoomTool,
                ToolZoom,
                ShowMedkits,
                ShowBatteries,
                ShowDataTerminals,
                ShowCollectedMaterials,
                TrackMaterialCollection,
                SuppressForActiveBuildProjects));
            SettingsStatus = string.Empty;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            SettingsStatus =
                $"Settlement-map settings could not be saved: {exception.Message}";
        }
    }

    private void NotifySiteState()
    {
        OnPropertyChanged(nameof(ActiveSite));
        OnPropertyChanged(nameof(MapProjection));
        OnPropertyChanged(nameof(HasKnownType));
        OnPropertyChanged(nameof(HasKnownGeometry));
        OnPropertyChanged(nameof(SiteName));
        OnPropertyChanged(nameof(TemplateText));
        OnPropertyChanged(nameof(GeometryStatus));
        OnPropertyChanged(nameof(FactionText));
        OnPropertyChanged(nameof(GovernmentText));
        OnPropertyChanged(nameof(IsAnarchy));
        OnPropertyChanged(nameof(HasInterstellarFactors));
        OnPropertyChanged(nameof(DockingStatus));
        OnPropertyChanged(nameof(DockingStatusText));
        OnPropertyChanged(nameof(HasDockingStatus));
        OnPropertyChanged(nameof(CommanderOffset));
        OnPropertyChanged(nameof(ProcessedTerminalIndexes));
        OnPropertyChanged(nameof(ProcessedTerminalOffsets));
        OnPropertyChanged(nameof(CollectedMaterials));
        OnPropertyChanged(nameof(CollectedMaterialLocationCount));
        OnPropertyChanged(nameof(ShipOffset));
        OnPropertyChanged(nameof(SrvOffset));
        OnPropertyChanged(nameof(HasShipDeparted));
        OnPropertyChanged(nameof(DistanceToShipMeters));
        OnPropertyChanged(nameof(ShowShipDismissalBoundary));
        OnPropertyChanged(nameof(ShowShipDismissalWarning));
        OnPropertyChanged(nameof(ShipDismissalWarningText));
        OnPropertyChanged(nameof(DistanceToOriginMeters));
        OnPropertyChanged(nameof(ApproachDistanceMeters));
        OnPropertyChanged(nameof(RelativeHeading));
        OnPropertyChanged(nameof(DistanceText));
        OnPropertyChanged(nameof(ApproachDistanceText));
        OnPropertyChanged(nameof(CommanderPositionText));
        OnPropertyChanged(nameof(ShowOriginWarning));
        OnPropertyChanged(nameof(ShouldShow));
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

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
