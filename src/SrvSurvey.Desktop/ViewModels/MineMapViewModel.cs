using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using Avalonia.Threading;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Controls;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class MineMapViewModel : WorkspaceObservable, IDisposable
{
    private const double DefaultViewportZoom = 1;
    private const string AllMarkerRatings = "ALL";
    private static readonly TimeSpan SurveyGuideFeedbackDuration = TimeSpan.FromSeconds(6);
    private static readonly IReadOnlyList<string> MarkerRatingFilters = [AllMarkerRatings, "HIGH", "MEDIUM", "LOW"];
    private readonly MineMapService service;
    private readonly MiningSearchResultCache miningSearchCache;
    private readonly MineMapSettingsStore settingsStore;
    private readonly Action<string> notify;
    private readonly Action<Guid> editBookmark;
    private readonly Action requestOverviewMapVisibility;
    private readonly WorkspaceTableSorter surveySorter = new();
    private readonly WorkspaceTableSorter hotspotSorter = new();
    private readonly WorkspaceTableSorter surfaceHuntSorter = new();
    private readonly IReadOnlyList<SurfaceMiningCommodityRowViewModel> hotspotRows;
    private readonly CancellationTokenSource commodityPriceCancellation = new();
    private readonly DispatcherTimer commodityPriceTimer = new() { Interval = TimeSpan.FromHours(1) };
    private MiningSearchClient commodityPriceClient = new();
    private IReadOnlyList<SurfaceMiningHuntRowViewModel> surfaceHuntRows;
    private IReadOnlyList<SurfaceMiningCommodityRowViewModel> miningReferenceRows;
    private MineMapCommandContext? context;
    private EliteStatus? status;
    private IReadOnlyList<MineMapSurveyRowViewModel> filteredSurveys = [];
    private IReadOnlyList<MineMapMarkerFilterViewModel> markerFilters = [];
    private string searchText = string.Empty;
    private string selectedContains = "All";
    private string selectedBodyType = "All";
    private bool favoritesOnly;
    private string selectedMineralAmountFilter = AllMarkerRatings;
    private string selectedDensityFilter = AllMarkerRatings;
    private MineMapSurveyGuideState? editorSurveyGuide;
    private int selectedTab;
    private double viewportZoom = DefaultViewportZoom;
    private bool onlyShowWhileOnGround;
    private bool showMarkerLabelsInOverviewMap;
    private MineMapSurveyRowViewModel? selectedSurveyRow;
    private MineMapSurvey? editorSurvey;
    private SurfaceCoordinate? planningCircleCenter;
    private Guid? planningCircleSurveyId;
    private string statusText = string.Empty;
    private string currentSystem = string.Empty;
    private string commodityPriceStatus = "Catalog prices shown while Ardent market quotes load.";
    private bool disposed;
    private bool isAlignmentHelperVisible;
    private string surveyGuideFeedback = string.Empty;
    private DateTimeOffset? surveyGuideFeedbackExpiresAt;

    public MineMapViewModel(
        string dataDirectory,
        MineMapSettingsStore settingsStore,
        Action<string> notify,
        Action? openSurfaceMiningGuide = null,
        Action<Guid>? editBookmark = null,
        BookmarkCatalog? bookmarkCatalog = null,
        Action? requestOverviewMapVisibility = null
    )
    {
        service = new MineMapService(dataDirectory, bookmarkCatalog);
        miningSearchCache = new MiningSearchResultCache(dataDirectory);
        this.settingsStore = settingsStore;
        this.notify = notify;
        this.editBookmark = editBookmark ?? (_ => { });
        this.requestOverviewMapVisibility = requestOverviewMapVisibility ?? (() => { });
        MineMapPreferences preferences = settingsStore.Load();
        onlyShowWhileOnGround = preferences.OnlyShowWhileOnGround;
        showMarkerLabelsInOverviewMap = preferences.ShowMarkerLabelsInOverviewMap;
        var selectedReferenceCommodities = preferences
            .EffectiveMiningReferenceCommodities.Select(name =>
                SurfaceMiningCommodityCatalog.TryResolve(name, out SurfaceMiningCommodity? commodity)
                    ? commodity.Name
                    : name
            )
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        hotspotRows = SurfaceMiningCommodityCatalog
            .All.Select(commodity => new SurfaceMiningCommodityRowViewModel(
                commodity,
                selectedReferenceCommodities.Contains(commodity.Name),
                OnMiningReferenceSelectionChanged
            ))
            .ToArray();
        miningReferenceRows = ReadMiningReferenceRows();
        surfaceHuntRows = SurfaceMiningCommodityCatalog
            .HuntReferences.Select(reference => new SurfaceMiningHuntRowViewModel(reference))
            .ToArray();
        commodityPriceTimer.Tick += OnCommodityPriceTimerTick;
        service.Changed += OnServiceChanged;
        service.NotificationRequested += OnServiceNotificationRequested;
        ZoomInCommand = new WorkspaceCommand(() => ViewportZoom = Math.Min(15, ViewportZoom + 0.5));
        ZoomOutCommand = new WorkspaceCommand(() => ViewportZoom = Math.Max(1, ViewportZoom - 0.5));
        ResetZoomCommand = new WorkspaceCommand(() => ViewportZoom = DefaultViewportZoom);
        SortSurveysCommand = new WorkspaceParameterCommand(parameter =>
        {
            surveySorter.Toggle(parameter);
            RefreshFilteredSurveys();
            Changed(nameof(SurveySortIndicators));
        });
        SortHotspotsCommand = new WorkspaceParameterCommand(parameter =>
        {
            hotspotSorter.Toggle(parameter);
            Changed(nameof(HotspotRows));
            Changed(nameof(HotspotSortIndicators));
        });
        SortSurfaceHuntCommand = new WorkspaceParameterCommand(parameter =>
        {
            surfaceHuntSorter.Toggle(parameter);
            Changed(nameof(SurfaceHuntRows));
            Changed(nameof(SurfaceHuntSortIndicators));
        });
        OpenSurfaceMiningGuideCommand = new WorkspaceCommand(openSurfaceMiningGuide ?? (() => { }));
        EditSelectedBookmarkCommand = new WorkspaceCommand(() =>
        {
            if (SelectedSurveyRow is { } row)
            {
                this.editBookmark(row.Id);
            }
        });
        SurfaceSearch = new SurfaceMiningSearchViewModel(commodityPriceClient);
        ActivateSelectedSurveyCommand = new WorkspaceCommand(() =>
        {
            if (SelectedSurveyRow is { } row)
            {
                SelectSurvey(row);
            }
        });
        RefreshCatalog();
    }

    public SurfaceMiningSearchViewModel SurfaceSearch { get; private set; }

    public void UseSurfaceSearch(MiningSearchClient client, Action<string>? diagnosticLog = null)
    {
        SurfaceSearch.Dispose();
        commodityPriceClient = client;
        client.DiagnosticLog = diagnosticLog;
        SurfaceSearch = new SurfaceMiningSearchViewModel(client);
        SurfaceSearch.ConfigureCache(miningSearchCache);
        SurfaceSearch.UpdateCurrentLocation(currentSystem);
        Changed(nameof(SurfaceSearch));
        if (client.CachedCommodityPriceReport is { Count: > 0 } cached)
        {
            ApplyCommodityPrices(client, cached, stored: true);
        }

        _ = RefreshCommodityPricesAsync();
    }

    public void UpdateCurrentSystem(string? system)
    {
        currentSystem = system ?? string.Empty;
        SurfaceSearch.UpdateCurrentLocation(currentSystem);
    }

    public int SelectedTab
    {
        get => selectedTab;
        set
        {
            if (Set(ref selectedTab, value))
            {
                Changed(nameof(WorkspaceMaxWidth));
                if (value is 2 or 3)
                {
                    commodityPriceTimer.Start();
                    _ = RefreshCommodityPricesAsync();
                }
                else
                {
                    commodityPriceTimer.Stop();
                }
            }
        }
    }

    public double WorkspaceMaxWidth => SelectedTab == 4 ? double.PositiveInfinity : 1050;

    public string SearchText
    {
        get => searchText;
        set
        {
            if (Set(ref searchText, value))
            {
                RefreshFilteredSurveys();
            }
        }
    }

    public string SelectedContains
    {
        get => selectedContains;
        set
        {
            if (Set(ref selectedContains, value))
            {
                RefreshFilteredSurveys();
            }
        }
    }

    public string SelectedBodyType
    {
        get => selectedBodyType;
        set
        {
            if (Set(ref selectedBodyType, value))
            {
                RefreshFilteredSurveys();
            }
        }
    }

    public bool FavoritesOnly
    {
        get => favoritesOnly;
        set
        {
            if (Set(ref favoritesOnly, value))
            {
                RefreshFilteredSurveys();
            }
        }
    }

    public IReadOnlyList<string> ContainsOptions { get; private set; } = ["All"];

    public IReadOnlyList<string> BodyTypeOptions { get; private set; } = ["All"];

    public IReadOnlyList<SurfaceMiningCommodityRowViewModel> HotspotRows => hotspotSorter.Apply(hotspotRows);

    public IReadOnlyList<SurfaceMiningHuntRowViewModel> SurfaceHuntRows => surfaceHuntSorter.Apply(surfaceHuntRows);

    public string CommodityPriceStatus
    {
        get => commodityPriceStatus;
        private set => Set(ref commodityPriceStatus, value);
    }

    public async Task RefreshCommodityPricesAsync()
    {
        MiningSearchClient client = commodityPriceClient;
        try
        {
            IReadOnlyDictionary<string, MiningCommodityPriceSummary> report = await client.CommodityPriceReportAsync(
                commodityPriceCancellation.Token
            );
            if (disposed || client != commodityPriceClient)
            {
                return;
            }

            if (report.Count == 0)
            {
                CommodityPriceStatus = "Ardent daily prices are unavailable. Showing the catalog snapshot.";
                return;
            }

            ApplyCommodityPrices(client, report, stored: false);
        }
        catch (OperationCanceledException) when (commodityPriceCancellation.IsCancellationRequested)
        {
            // The workspace is closing.
        }
    }

    private void ApplyCommodityPrices(
        MiningSearchClient client,
        IReadOnlyDictionary<string, MiningCommodityPriceSummary> report,
        bool stored
    )
    {
        foreach (SurfaceMiningCommodityRowViewModel row in hotspotRows)
        {
            row.ApplyDailyPrice(SurfaceMiningCommodityPrices.Find(row.Name, report));
        }

        surfaceHuntRows = SurfaceMiningCommodityCatalog
            .HuntReferences.Select(reference => new SurfaceMiningHuntRowViewModel(
                reference,
                SurfaceMiningCommodityPrices.Find(reference.Material, report)
            ))
            .ToArray();
        Changed(nameof(HotspotRows));
        Changed(nameof(SurfaceHuntRows));
        string checkedAt =
            client.CommodityReportFetchedAt?.UtcDateTime.ToString("dd MMM yyyy HH:mm", CultureInfo.CurrentCulture)
            ?? "unknown";
        string sourceAt =
            client.CommodityLiveQuoteUpdatedAt?.UtcDateTime.ToString("dd MMM yyyy HH:mm", CultureInfo.CurrentCulture)
            ?? "unknown";
        string coverage = $"{client.LiveSurfaceQuoteCount}/{MiningSearchClient.SurfaceQuoteCount}";
        if (stored)
        {
            CommodityPriceStatus =
                $"Showing stored Ardent market prices for {coverage} materials (latest quote {sourceAt} UTC; checked {checkedAt} UTC). Missing quotes use the older report or catalog.";
        }
        else if (client.PriceMarksUnavailable)
        {
            CommodityPriceStatus =
                $"Ardent market prices for {coverage} materials (latest quote {sourceAt} UTC); some quotes could not be refreshed. Missing quotes use the older report or catalog.";
        }
        else
        {
            CommodityPriceStatus =
                $"Ardent market prices for {coverage} materials (latest quote {sourceAt} UTC; checked {checkedAt} UTC). Missing quotes use the older report or catalog.";
        }
    }

    private void OnCommodityPriceTimerTick(object? sender, EventArgs e) => _ = RefreshCommodityPricesAsync();

    public IReadOnlyList<SurfaceMiningCommodityRowViewModel> MiningReferenceRows => miningReferenceRows;

    public bool ShouldShowMiningReference => MiningReferenceRows.Count > 0;

    public bool ShouldShowAlignmentHelper => isAlignmentHelperVisible;

    public bool ShouldShowSurveyGuideOverlay =>
        HasSurveyGuideFeedback
        || (
            CurrentSurveyGuide is { } guide
            && (
                context is { } current
                    && guide.FrontierId == current.FrontierId
                    && guide.SystemAddress == current.SystemAddress
                    && guide.BodyId == current.BodyId
                || context is null && status is not null && !HasLeftSurfaceLocation(status)
            )
        );

    public bool HasSurveyGuideFeedback => !string.IsNullOrWhiteSpace(surveyGuideFeedback);

    public string SurveyGuideFeedback => surveyGuideFeedback;

    public bool HasSurveyGuideFooter =>
        HasSurveyGuideFeedback || CurrentSurveyGuide is { Phase: MineMapSurveyGuidePhase.Waypoint };

    public string SurveyGuideFooter
    {
        get
        {
            if (HasSurveyGuideFeedback)
            {
                return SurveyGuideFeedback;
            }

            return CurrentSurveyGuide is { Phase: MineMapSurveyGuidePhase.Waypoint }
                ? ".mine <bearing> <commodity> <km> <amount>/<density>  ·  .mine <commodity> <amount>/<density> here"
                : string.Empty;
        }
    }

    public bool IsSurveyGuideComplete => CurrentSurveyGuide is { Phase: MineMapSurveyGuidePhase.Complete };

    public string SurveyGuideTitle =>
        CurrentSurveyGuide switch
        {
            { Phase: MineMapSurveyGuidePhase.Border } => "SURFACE MINING SURVEY · BORDER",
            { Phase: MineMapSurveyGuidePhase.Center } => "SURFACE MINING SURVEY · CENTER",
            { Phase: MineMapSurveyGuidePhase.ConfirmCenter } => "SURFACE MINING SURVEY · CENTER REACHED",
            { Phase: MineMapSurveyGuidePhase.Waypoint } guide =>
                $"SURFACE MINING SURVEY · {guide.WaypointIndex + 1:N0} OF {guide.Waypoints?.Count ?? 0:N0}",
            { Phase: MineMapSurveyGuidePhase.Complete } => "SURFACE MINING SURVEY · COMPLETE",
            _ => HasSurveyGuideFeedback ? "SURFACE MINING SURVEY" : string.Empty,
        };

    public string SurveyGuideInstruction =>
        CurrentSurveyGuide switch
        {
            { Phase: MineMapSurveyGuidePhase.Border } => "Drive to the orange border and face the location center.",
            { Phase: MineMapSurveyGuidePhase.Center } => FormatSurveyGuideTarget("Drive to the saved center"),
            { Phase: MineMapSurveyGuidePhase.ConfirmCenter } =>
                "Stop at the true center and correct the map alignment.",
            { Phase: MineMapSurveyGuidePhase.Waypoint } => FormatSurveyGuideTarget("Drive to the next scan point"),
            { Phase: MineMapSurveyGuidePhase.Complete } => "Surface scan route complete.",
            _ => string.Empty,
        };

    public string SurveyGuideCommandHint =>
        CurrentSurveyGuide?.Phase switch
        {
            MineMapSurveyGuidePhase.Border => ".mining <bearing> <radius km> <signal #>",
            MineMapSurveyGuidePhase.Center or MineMapSurveyGuidePhase.ConfirmCenter => ".mining center here",
            MineMapSurveyGuidePhase.Waypoint =>
                ".mining waypoint <next|prev> to adjust the route · .mining survey complete to finish early",
            MineMapSurveyGuidePhase.Complete =>
                "While mining, use .mine rigs <number> to record each deposit's rig capacity.",
            _ => string.Empty,
        };

    public SurfaceCoordinate? SurveyGuideTarget =>
        CurrentSurveyGuide switch
        {
            {
                Phase: MineMapSurveyGuidePhase.Center or MineMapSurveyGuidePhase.ConfirmCenter,
                SurveyId: { } surveyId
            } => service.Surveys.FirstOrDefault(survey => survey.Id == surveyId)?.Center,
            { Phase: MineMapSurveyGuidePhase.Waypoint } guide => guide.CurrentWaypoint,
            _ => null,
        };

    public IReadOnlyList<MineMapSurveyRowViewModel> FilteredSurveys
    {
        get => filteredSurveys;
        private set => Set(ref filteredSurveys, value);
    }

    public IReadOnlyList<MineMapMarkerFilterViewModel> MarkerFilters
    {
        get => markerFilters;
        private set => Set(ref markerFilters, value);
    }

    public static IReadOnlyList<string> MarkerRatingFilterOptions => MarkerRatingFilters;

    public string SelectedMineralAmountFilter
    {
        get => selectedMineralAmountFilter;
        set
        {
            if (Set(ref selectedMineralAmountFilter, NormalizeMarkerRatingFilter(value)))
            {
                Changed(nameof(VisibleMarkerIds));
            }
        }
    }

    public string SelectedDensityFilter
    {
        get => selectedDensityFilter;
        set
        {
            if (Set(ref selectedDensityFilter, NormalizeMarkerRatingFilter(value)))
            {
                Changed(nameof(VisibleMarkerIds));
            }
        }
    }

    public MineMapSurvey? ActiveSurvey => editorSurvey ?? service.ActiveSurvey;

    public MineMapSurvey? ActiveLiveSurvey => service.ActiveSurvey;

    public bool HasActiveSurvey => ActiveSurvey is not null;

    public string LiveMapTitle => ActiveSurvey?.Name ?? "No live mining map";

    public string LiveMapDescription =>
        ActiveSurvey is { } survey
            ? $"{survey.SystemName} · {survey.BodyName} · {survey.LocationRadiusMeters / 1000:0.##} km border"
            : "Drive to a mining-location border, face its center, and send .mining <bearing> <radius km> <number>, i.e. .mining 120 6.44 4.";

    public static string LiveMapCommandHelp =>
        "Drive to the orange mining-location border and face its center, then use .mining <bearing> <radius km> <number>, i.e. .mining 120 6.44 4. Use .mining center here from the true center to correct it later.";

    public string LiveMapLocation =>
        ActiveSurvey is { } survey ? $"{survey.Center.Latitude:0.000000}, {survey.Center.Longitude:0.000000}" : "—";

    public string SelectedMapSystem => ActiveSurvey?.SystemName ?? "—";

    public string SelectedMapBody =>
        ActiveSurvey is { } survey ? GalacticBookmark.TrimSystemPrefix(survey.SystemName, survey.BodyName) : "—";

    public string SelectedMapSignal => ActiveSurvey?.LocationSignal.ToString(CultureInfo.InvariantCulture) ?? "—";

    public string SelectedMapRadius =>
        ActiveSurvey is { } survey ? $"{survey.LocationRadiusMeters / 1000:0.##} km" : "—";

    public string MarkerCountText
    {
        get
        {
            if (ActiveSurvey is not { } survey)
            {
                return "No survey selected";
            }

            string suffix = survey.Markers.Count == 1 ? string.Empty : "s";
            return $"{survey.Markers.Count:N0} mapped deposit{suffix}";
        }
    }

    public SurfaceCoordinate? PlayerLocation => IsActiveSurveyCurrentContext ? context?.PlayerLocation : null;

    public double PlayerHeading => IsActiveSurveyCurrentContext ? status?.NormalizedHeading ?? 0 : 0;

    public SurfaceCoordinate? PlanningCircleCenter
    {
        get => planningCircleSurveyId == ActiveSurvey?.Id ? planningCircleCenter : null;
        set
        {
            Guid? surveyId = value is null ? null : ActiveSurvey?.Id;
            if (planningCircleSurveyId == surveyId && planningCircleCenter == value)
            {
                return;
            }

            planningCircleSurveyId = surveyId;
            planningCircleCenter = value;
            Changed();
        }
    }

    public double ViewportZoom
    {
        get => viewportZoom;
        set
        {
            double normalized = double.IsFinite(value) ? Math.Clamp(value, 1, 15) : 1;
            if (Set(ref viewportZoom, normalized))
            {
                Changed(nameof(ZoomText));
            }
        }
    }

    public string ZoomText => $"{ViewportZoom:0.00}x";

    public bool OnlyShowWhileOnGround
    {
        get => onlyShowWhileOnGround;
        set
        {
            if (!Set(ref onlyShowWhileOnGround, value))
            {
                return;
            }

            try
            {
                SavePreferences();
                StatusText = string.Empty;
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                StatusText =
                    "Surface Mining map setting changed for this session but could not be saved: " + exception.Message;
            }
            Changed(nameof(ShouldShowOverlay));
        }
    }

    public bool ShowMarkerLabelsInOverviewMap
    {
        get => showMarkerLabelsInOverviewMap;
        set
        {
            if (!Set(ref showMarkerLabelsInOverviewMap, value))
            {
                return;
            }

            try
            {
                SavePreferences();
                StatusText = string.Empty;
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                StatusText =
                    "Overview Map marker-label setting changed for this session but could not be saved: "
                    + exception.Message;
            }
        }
    }

    public bool ShouldShowOverlay =>
        IsActiveSurveyCurrentContext
        && status is { HasLatitudeLongitude: true }
        && (!OnlyShowWhileOnGround || IsOnGround(status));

    private bool IsActiveSurveyCurrentContext =>
        ActiveSurvey is { } survey
        && context is { } current
        && survey.FrontierId == current.FrontierId
        && survey.SystemAddress == current.SystemAddress
        && survey.BodyId == current.BodyId;

    private void OnMiningReferenceSelectionChanged()
    {
        try
        {
            SavePreferences();
            StatusText = string.Empty;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusText =
                "Mining reference selection changed for this session but could not be saved: " + exception.Message;
        }

        miningReferenceRows = ReadMiningReferenceRows();
        Changed(nameof(HotspotRows));
        Changed(nameof(MiningReferenceRows));
        Changed(nameof(ShouldShowMiningReference));
    }

    private SurfaceMiningCommodityRowViewModel[] ReadMiningReferenceRows() =>
        hotspotRows.Where(row => row.IsInOverlay).OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    private void SavePreferences() =>
        settingsStore.Save(
            new MineMapPreferences(
                OnlyShowWhileOnGround,
                hotspotRows.Where(row => row.IsInOverlay).Select(row => row.Name).ToArray(),
                ShowMarkerLabelsInOverviewMap
            )
        );

    public MineMapSurveyRowViewModel? SelectedSurveyRow
    {
        get => selectedSurveyRow;
        set => Set(ref selectedSurveyRow, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => Set(ref statusText, value);
    }

    public ICommand ZoomInCommand { get; }

    public ICommand ZoomOutCommand { get; }

    public ICommand ResetZoomCommand { get; }

    public ICommand SortSurveysCommand { get; }

    public ICommand SortHotspotsCommand { get; }

    public ICommand SortSurfaceHuntCommand { get; }

    public WorkspaceSortIndicators SurveySortIndicators => new(surveySorter.Indicator);

    public WorkspaceSortIndicators HotspotSortIndicators => new(hotspotSorter.Indicator);

    public WorkspaceSortIndicators SurfaceHuntSortIndicators => new(surfaceHuntSorter.Indicator);

    public ICommand OpenSurfaceMiningGuideCommand { get; }

    public ICommand EditSelectedBookmarkCommand { get; }

    public ICommand ActivateSelectedSurveyCommand { get; }

    internal void ReportCsvExported(string fileName)
    {
        string message = $"Exported the selected Surface Mining map to {fileName}.";
        StatusText = message;
        notify(message);
    }

    internal void ReportCsvExportFailed(string message)
    {
        StatusText = "Surface Mining CSV export failed: " + message;
        notify(StatusText);
    }

    public async Task ApplyUpdateAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        MineMapCommandContext? nextContext,
        EliteStatus? latestStatus,
        bool allowCommands
    )
    {
        bool startsSurveyGuide = ContainsSurveyGuideCommand(journalEvents);
        if (allowCommands && ContainsMiningCommand(journalEvents))
        {
            requestOverviewMapVisibility();
        }

        context = nextContext;
        status = latestStatus;
        ApplyAlignmentCommands(journalEvents, allowCommands);
        service.UpdateContext(nextContext);
        if (latestStatus is not null && HasLeftSurfaceLocation(latestStatus))
        {
            service.DismissSurveyGuide();
            ClearSurveyGuideFeedback();
        }
        IReadOnlyList<MineMapCommandResult> results = await service.ApplyJournalEventsAsync(
            journalEvents,
            nextContext,
            allowCommands,
            CancellationToken.None
        );
        foreach (string message in results.Select(result => result.Message))
        {
            StatusText = message;
            if (startsSurveyGuide || service.SurveyGuide is not null)
            {
                ShowSurveyGuideFeedback(message);
            }
            else
            {
                notify(message);
            }
        }
        if (results.Any(result => result.Succeeded && result.Survey is not null))
        {
            editorSurvey = null;
        }
        RefreshCatalog();
        RaiseLiveState();
    }

    private void ApplyAlignmentCommands(IReadOnlyList<JournalEventEnvelope> journalEvents, bool allowCommands)
    {
        if (!allowCommands)
        {
            return;
        }

        foreach (JournalEventEnvelope journalEvent in journalEvents)
        {
            if (
                !string.Equals(journalEvent.EventName, "SendText", StringComparison.Ordinal)
                || !journalEvent.Payload.TryGetProperty("Message", out JsonElement messageProperty)
                || messageProperty.ValueKind != System.Text.Json.JsonValueKind.String
                || !string.Equals(messageProperty.GetString()?.Trim(), ".alignment", StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            isAlignmentHelperVisible = !isAlignmentHelperVisible;
            Changed(nameof(ShouldShowAlignmentHelper));
            string message = isAlignmentHelperVisible
                ? "Alignment helper shown at the center of the Elite window."
                : "Alignment helper hidden.";
            StatusText = message;
            notify(message);
        }
    }

    public void SelectSurvey(MineMapSurveyRowViewModel row)
    {
        SelectSurvey(row.Id);
    }

    public bool SelectSurvey(Guid surveyId)
    {
        if (!service.SelectSurvey(surveyId))
        {
            return false;
        }

        editorSurvey = null;
        selectedSurveyRow = CatalogSurveys
            .Where(survey => survey.Id == surveyId)
            .Select(survey => CreateSurveyRow(survey))
            .SingleOrDefault();
        Changed(nameof(SelectedSurveyRow));
        SelectedTab = 1;
        RaiseLiveState();
        return true;
    }

    public void Dispose()
    {
        disposed = true;
        commodityPriceTimer.Stop();
        commodityPriceTimer.Tick -= OnCommodityPriceTimerTick;
        commodityPriceCancellation.Cancel();
        commodityPriceCancellation.Dispose();
        service.Changed -= OnServiceChanged;
        service.NotificationRequested -= OnServiceNotificationRequested;
        SurfaceSearch.Dispose();
        service.Dispose();
    }

    private void OnServiceNotificationRequested(string message)
    {
        StatusText = message;
        if (service.SurveyGuide is not null)
        {
            ShowSurveyGuideFeedback(message);
        }
        else
        {
            notify(message);
        }
    }

    public void DismissSurveyGuide()
    {
        service.DismissSurveyGuide();
        ClearSurveyGuideFeedback();
    }

    internal void ExpireSurveyGuideFeedback(DateTimeOffset now)
    {
        if (surveyGuideFeedbackExpiresAt is null || now < surveyGuideFeedbackExpiresAt)
        {
            return;
        }

        ClearSurveyGuideFeedback();
    }

    private void ShowSurveyGuideFeedback(string message)
    {
        surveyGuideFeedback = message;
        surveyGuideFeedbackExpiresAt = DateTimeOffset.UtcNow + SurveyGuideFeedbackDuration;
        Changed(nameof(SurveyGuideFeedback));
        Changed(nameof(HasSurveyGuideFeedback));
        Changed(nameof(SurveyGuideFooter));
        Changed(nameof(HasSurveyGuideFooter));
        Changed(nameof(ShouldShowSurveyGuideOverlay));
        Changed(nameof(SurveyGuideTitle));
    }

    private void ClearSurveyGuideFeedback()
    {
        if (!HasSurveyGuideFeedback && surveyGuideFeedbackExpiresAt is null)
        {
            return;
        }

        surveyGuideFeedback = string.Empty;
        surveyGuideFeedbackExpiresAt = null;
        Changed(nameof(SurveyGuideFeedback));
        Changed(nameof(HasSurveyGuideFeedback));
        Changed(nameof(SurveyGuideFooter));
        Changed(nameof(HasSurveyGuideFooter));
        Changed(nameof(ShouldShowSurveyGuideOverlay));
        Changed(nameof(SurveyGuideTitle));
    }

    private static bool ContainsSurveyGuideCommand(IReadOnlyList<JournalEventEnvelope> journalEvents) =>
        journalEvents.Any(journalEvent =>
            string.Equals(journalEvent.EventName, "SendText", StringComparison.Ordinal)
            && journalEvent.Payload.TryGetProperty("Message", out JsonElement message)
            && message.ValueKind == JsonValueKind.String
            && string.Equals(message.GetString()?.Trim(), ".mining survey", StringComparison.OrdinalIgnoreCase)
        );

    private static bool HasLeftSurfaceLocation(EliteStatus currentStatus) =>
        (currentStatus.Flags & (StatusFlags.Supercruise | StatusFlags.FsdJump)) != 0;

    private static bool ContainsMiningCommand(IReadOnlyList<JournalEventEnvelope> journalEvents) =>
        journalEvents.Any(journalEvent =>
            string.Equals(journalEvent.EventName, "SendText", StringComparison.Ordinal)
            && journalEvent.Payload.TryGetProperty("Message", out JsonElement message)
            && message.ValueKind == JsonValueKind.String
            && message.GetString()?.Trim() is { } command
            && (
                command.Equals(".mining", StringComparison.OrdinalIgnoreCase)
                || command.StartsWith(".mining ", StringComparison.OrdinalIgnoreCase)
            )
        );

    internal static MineMapViewModel CreateEditorPreview()
    {
        string root = Path.Combine(Path.GetTempPath(), "SrvSurvey-OverlayEditorPreview", "mine-map-v2");
        var viewModel = new MineMapViewModel(
            root,
            new MineMapSettingsStore(Path.Combine(root, "ui-settings.json")),
            _ => { }
        );
        var center = new SurfaceCoordinate(-18.4216, 74.0921);
        const double radius = 855_573.1875;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        viewModel.editorSurvey = new MineMapSurvey
        {
            FrontierId = "preview",
            CommanderName = "Fenris",
            SystemName = "Wille",
            SystemAddress = 123456789,
            SystemPosition = new SrvSurvey.Core.Search.GalacticCoordinate(1, 2, 3),
            BodyId = 3,
            BodyName = "Wille 2 C",
            BodyType = "Rocky Ice body",
            ArrivalDistanceLs = 129,
            LocationSignal = 4,
            LocationRadiusMeters = 4_200,
            PlanetRadiusMeters = radius,
            Center = center,
            CreatedAt = now,
            UpdatedAt = now,
            Markers =
            [
                new MineMapMarker
                {
                    Material = "Ruby",
                    MineralAmount = MineMapRating.High,
                    Density = MineMapRating.Low,
                    Location = MineMapService.GetDestination(center, 15, 1240, radius),
                    CreatedAt = now,
                },
                new MineMapMarker
                {
                    Material = "Gold",
                    MineralAmount = MineMapRating.Low,
                    Density = MineMapRating.High,
                    Location = MineMapService.GetDestination(center, 90, 2100, radius),
                    CreatedAt = now,
                },
                new MineMapMarker
                {
                    Material = "Thortveitite",
                    MineralAmount = MineMapRating.High,
                    Density = MineMapRating.High,
                    Location = MineMapService.GetDestination(center, 225, 3400, radius),
                    CreatedAt = now,
                },
            ],
        };
        viewModel.context = new MineMapCommandContext(
            "preview",
            "Fenris",
            "Wille",
            123456789,
            viewModel.editorSurvey.SystemPosition,
            3,
            "Wille 2 C",
            "Rocky Ice body",
            129,
            radius,
            MineMapService.GetDestination(center, 190, 450, radius)
        );
        viewModel.status = new EliteStatus
        {
            Flags = StatusFlags.InSrv | StatusFlags.HasLatLong,
            Heading = 25,
            PlanetRadius = (decimal)radius,
        };
        viewModel.RaiseLiveState();
        return viewModel;
    }

    internal void InstallSurveyGuideEditorPreview(MineMapSurveyGuidePhase phase)
    {
        if (context is null || editorSurvey is null)
        {
            return;
        }

        IReadOnlyList<SurfaceCoordinate>? waypoints =
            phase == MineMapSurveyGuidePhase.Waypoint
                ? [MineMapService.GetDestination(editorSurvey.Center, 35, 1_800, editorSurvey.PlanetRadiusMeters)]
                : null;
        editorSurveyGuide = new MineMapSurveyGuideState(
            phase,
            context.FrontierId,
            context.SystemAddress,
            context.BodyId,
            phase == MineMapSurveyGuidePhase.Border ? null : editorSurvey.Id,
            waypoints
        );
        RaiseLiveState();
    }

    private void OnServiceChanged(object? sender, EventArgs eventArgs)
    {
        RefreshCatalog();
        RaiseLiveState();
    }

    private void RefreshCatalog()
    {
        IReadOnlyList<MineMapSurvey> surveys = CatalogSurveys;
        ContainsOptions =
        [
            "All",
            .. surveys
                .SelectMany(survey => survey.Markers)
                .Select(marker => marker.Material)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase),
        ];
        BodyTypeOptions =
        [
            "All",
            .. surveys
                .Select(survey => survey.BodyType)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase),
        ];
        if (!ContainsOptions.Contains(SelectedContains, StringComparer.OrdinalIgnoreCase))
        {
            SelectedContains = "All";
        }

        if (!BodyTypeOptions.Contains(SelectedBodyType, StringComparer.OrdinalIgnoreCase))
        {
            SelectedBodyType = "All";
        }

        Changed(nameof(ContainsOptions));
        Changed(nameof(BodyTypeOptions));
        RefreshMarkerFilters();
        RefreshFilteredSurveys();
    }

    private void RefreshFilteredSurveys()
    {
        string query = SearchText.Trim();
        var expanded = FilteredSurveys.Where(row => row.IsExpanded).Select(row => row.Id).ToHashSet();
        FilteredSurveys = surveySorter.Apply(
            CatalogSurveys
                .Where(survey =>
                    string.Equals(SelectedContains, "All", StringComparison.OrdinalIgnoreCase)
                    || survey.Markers.Any(marker =>
                        string.Equals(marker.Material, SelectedContains, StringComparison.OrdinalIgnoreCase)
                    )
                )
                .Where(survey =>
                    string.Equals(SelectedBodyType, "All", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(survey.BodyType, SelectedBodyType, StringComparison.OrdinalIgnoreCase)
                )
                .Where(survey =>
                    query.Length == 0 || Searchable(survey).Contains(query, StringComparison.OrdinalIgnoreCase)
                )
                .Where(survey => !FavoritesOnly || service.IsFavorite(survey.Id))
                .Select(survey => CreateSurveyRow(survey, expanded.Contains(survey.Id)))
                .ToArray()
        );
    }

    private MineMapSurveyRowViewModel CreateSurveyRow(MineMapSurvey survey, bool isExpanded = false) =>
        new(survey, service.IsFavorite(survey.Id), ToggleFavorite) { IsExpanded = isExpanded };

    private void ToggleFavorite(MineMapSurveyRowViewModel row)
    {
        bool favorite = !row.IsFavorite;
        try
        {
            if (!service.SetFavorite(row.Id, favorite))
            {
                StatusText = "The surface mining bookmark could not be found.";
                return;
            }

            StatusText = favorite
                ? $"Added {row.SystemName} {row.DisplayBodyName} Signal {row.SignalNumber} to favorites."
                : $"Removed {row.SystemName} {row.DisplayBodyName} Signal {row.SignalNumber} from favorites.";
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusText = "The favorite could not be updated: " + exception.Message;
        }
    }

    private void RefreshMarkerFilters()
    {
        var previous = MarkerFilters.ToDictionary(
            filter => filter.Name,
            filter => filter.IsVisible,
            StringComparer.OrdinalIgnoreCase
        );
        MarkerFilters =
            ActiveSurvey
                ?.Markers.Select(marker => marker.Material)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .Select(value => new MineMapMarkerFilterViewModel(
                    value,
                    previous.GetValueOrDefault(value, true),
                    RaiseMapState
                ))
                .ToArray()
            ?? [];
        Changed(nameof(VisibleMaterials));
        Changed(nameof(VisibleMarkerIds));
    }

    private void RaiseMapState()
    {
        Changed(nameof(VisibleMaterials));
        Changed(nameof(VisibleMarkerIds));
    }

    public IReadOnlySet<string> VisibleMaterials =>
        MarkerFilters
            .Where(filter => filter.IsVisible)
            .Select(filter => filter.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<Guid> VisibleMarkerIds
    {
        get
        {
            if (ActiveSurvey is not { } survey)
            {
                return new HashSet<Guid>();
            }

            IReadOnlySet<string> visibleMaterials = VisibleMaterials;
            return survey
                .Markers.Where(marker =>
                    visibleMaterials.Contains(marker.Material)
                    && MatchesMarkerRating(marker.MineralAmount, SelectedMineralAmountFilter)
                    && MatchesMarkerRating(marker.Density, SelectedDensityFilter)
                )
                .Select(marker => marker.Id)
                .ToHashSet();
        }
    }

    private static string NormalizeMarkerRatingFilter(string? value) =>
        MarkerRatingFilters.FirstOrDefault(option => string.Equals(option, value, StringComparison.OrdinalIgnoreCase))
        ?? AllMarkerRatings;

    private static bool MatchesMarkerRating(MineMapRating rating, string filter) =>
        string.Equals(filter, AllMarkerRatings, StringComparison.Ordinal)
        || string.Equals(rating.ToString(), filter, StringComparison.OrdinalIgnoreCase);

    private void RaiseLiveState()
    {
        ClearPlanningCircleForInactiveSurvey();
        Changed(nameof(ActiveSurvey));
        Changed(nameof(ActiveLiveSurvey));
        Changed(nameof(HasActiveSurvey));
        Changed(nameof(LiveMapTitle));
        Changed(nameof(LiveMapDescription));
        Changed(nameof(LiveMapLocation));
        Changed(nameof(SelectedMapSystem));
        Changed(nameof(SelectedMapBody));
        Changed(nameof(SelectedMapSignal));
        Changed(nameof(SelectedMapRadius));
        Changed(nameof(MarkerCountText));
        Changed(nameof(PlayerLocation));
        Changed(nameof(PlayerHeading));
        Changed(nameof(ShouldShowOverlay));
        Changed(nameof(ShouldShowSurveyGuideOverlay));
        Changed(nameof(IsSurveyGuideComplete));
        Changed(nameof(SurveyGuideTitle));
        Changed(nameof(SurveyGuideInstruction));
        Changed(nameof(SurveyGuideCommandHint));
        Changed(nameof(SurveyGuideFooter));
        Changed(nameof(HasSurveyGuideFooter));
        Changed(nameof(SurveyGuideTarget));
        RefreshMarkerFilters();
    }

    private MineMapSurveyGuideState? CurrentSurveyGuide => editorSurveyGuide ?? service.SurveyGuide;

    private string FormatSurveyGuideTarget(string action)
    {
        if (context?.PlayerLocation is not { } player || SurveyGuideTarget is not { } target)
        {
            return action + ".";
        }

        double radius = ActiveSurvey?.PlanetRadiusMeters ?? context.PlanetRadiusMeters;
        double bearing = SurfaceNavigation.GetBearing(player, target);
        double distance = SurfaceNavigation.GetDistance(player, target, radius);
        string distanceText = distance >= 1_000 ? $"{distance / 1_000:0.00} km" : $"{distance:0} m";
        return $"{action}: {bearing:000}° · {distanceText}.";
    }

    private void ClearPlanningCircleForInactiveSurvey()
    {
        if (planningCircleSurveyId is null || planningCircleSurveyId == ActiveSurvey?.Id)
        {
            return;
        }

        planningCircleSurveyId = null;
        planningCircleCenter = null;
        Changed(nameof(PlanningCircleCenter));
    }

    private static bool IsOnGround(EliteStatus current) =>
        current.OnFootOnPlanet || current.InSrv || current.InMainShip && current.Landed;

    private static string Searchable(MineMapSurvey survey) =>
        string.Join(
            ' ',
            survey.SystemName,
            survey.BodyName,
            survey.BodyType,
            survey.Name,
            (survey.LocationRadiusMeters / 1000).ToString("0.##", CultureInfo.InvariantCulture),
            string.Join(
                ' ',
                survey.Markers.Select(marker => $"{marker.Material} {marker.MineralAmount} {marker.Density}")
            ),
            survey.Notes,
            survey.SystemAddress.ToString(CultureInfo.InvariantCulture)
        );

    private IReadOnlyList<MineMapSurvey> CatalogSurveys => service.Surveys;
}

public sealed class MineMapSurveyRowViewModel : WorkspaceObservable
{
    private static readonly SrvSurvey.Core.Search.GalacticCoordinate Sol = new(0, 0, 0);
    private bool isExpanded;

    public MineMapSurveyRowViewModel(
        MineMapSurvey survey,
        bool isFavorite = false,
        Action<MineMapSurveyRowViewModel>? toggleFavorite = null
    )
    {
        Id = survey.Id;
        SignalNumber = survey.LocationSignal.ToString(CultureInfo.InvariantCulture);
        SystemName = survey.SystemName;
        BodyName = survey.BodyName;
        BodyType = survey.BodyType;
        DistanceText = $"{Sol.DistanceTo(survey.SystemPosition):0.0} ly";
        ArrivalText = $"{survey.ArrivalDistanceLs:0} ls";
        RadiusText = $"{survey.LocationRadiusMeters / 1000:0.##} km";
        UpdatedText = survey.UpdatedAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
        Notes = survey.Notes;
        IsFavorite = isFavorite;
        Deposits = survey
            .Markers.OrderBy(marker => marker.Material, StringComparer.OrdinalIgnoreCase)
            .Select(marker => new MineMapDepositRowViewModel(
                marker.Material,
                marker.MineralAmount.ToString(),
                marker.Density.ToString(),
                marker.RigCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            ))
            .ToArray();
        ToggleExpandedCommand = new WorkspaceCommand(() => IsExpanded = !IsExpanded);
        ToggleFavoriteCommand = new WorkspaceCommand(() => toggleFavorite?.Invoke(this));
    }

    public Guid Id { get; }
    public string SignalNumber { get; }
    public string SystemName { get; }
    public string BodyName { get; }
    public string BodyType { get; }
    public string DistanceText { get; }
    public string ArrivalText { get; }
    public string RadiusText { get; }
    public string UpdatedText { get; }
    public string Notes { get; }
    public bool IsFavorite { get; }
    public string FavoriteGlyph => IsFavorite ? "\u2605" : "\u2606";
    public IReadOnlyList<MineMapDepositRowViewModel> Deposits { get; }
    public ICommand ToggleExpandedCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public bool HasDeposits => Deposits.Count > 0;

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (Set(ref isExpanded, value))
            {
                Changed(nameof(ExpandGlyph));
            }
        }
    }

    public string ExpandGlyph => IsExpanded ? "▼" : "▶";

    public int SignalValue => int.Parse(SignalNumber, CultureInfo.InvariantCulture);

    public string DisplayBodyName => GalacticBookmark.TrimSystemPrefix(SystemName, BodyName);

    public double DistanceValue => double.Parse(DistanceText[..DistanceText.IndexOf(' ')], CultureInfo.CurrentCulture);

    public double ArrivalValue => double.Parse(ArrivalText[..ArrivalText.IndexOf(' ')], CultureInfo.CurrentCulture);

    public double RadiusValue => double.Parse(RadiusText[..RadiusText.IndexOf(' ')], CultureInfo.CurrentCulture);

    public DateTime UpdatedValue => DateTime.Parse(UpdatedText, CultureInfo.CurrentCulture);
}

public sealed record MineMapDepositRowViewModel(string Material, string MineralAmount, string Density, string Rigs);

public sealed class SurfaceMiningCommodityRowViewModel : WorkspaceObservable
{
    private readonly Action selectionChanged;
    private int averageSellPriceValue;
    private int maximumSellPriceValue;
    private bool isInOverlay;

    public SurfaceMiningCommodityRowViewModel(
        SurfaceMiningCommodity commodity,
        bool isInOverlay = false,
        Action? selectionChanged = null
    )
    {
        Category = commodity.Category;
        Name = commodity.Name;
        ColorHex = commodity.ColorHex;
        HighMetalContent = Available(commodity.HighMetalContent);
        MetalRich = Available(commodity.MetalRich);
        Rocky = Available(commodity.Rocky);
        RockyIce = Available(commodity.RockyIce);
        Icy = Available(commodity.Icy);
        averageSellPriceValue = commodity.AverageSellPrice;
        maximumSellPriceValue = commodity.MaximumSellPrice;
        BodyTypes = string.Join(
            ", ",
            new[]
            {
                commodity.HighMetalContent ? "HMC" : null,
                commodity.MetalRich ? "MR" : null,
                commodity.Rocky ? "Rocky" : null,
                commodity.RockyIce ? "Rocky Ice" : null,
                commodity.Icy ? "Icy" : null,
            }.Where(value => value is not null)
        );
        this.isInOverlay = isInOverlay;
        this.selectionChanged = selectionChanged ?? (() => { });
    }

    public string Category { get; }
    public string Name { get; }
    public string ColorHex { get; }
    public string HighMetalContent { get; }
    public string MetalRich { get; }
    public string Rocky { get; }
    public string RockyIce { get; }
    public string Icy { get; }
    public string AverageSellPrice => $"{averageSellPriceValue:N0} CR/t";
    public string MaximumSellPrice => $"{maximumSellPriceValue:N0} CR/t";
    public string BodyTypes { get; }

    public bool IsInOverlay
    {
        get => isInOverlay;
        set
        {
            if (!Set(ref isInOverlay, value))
            {
                return;
            }

            selectionChanged();
        }
    }

    public int AverageSellPriceValue => averageSellPriceValue;

    public int MaximumSellPriceValue => maximumSellPriceValue;

    public void ApplyDailyPrice(MiningCommodityPriceSummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        if (
            summary.AverageSellPrice is > 0 and <= int.MaxValue
            && Set(ref averageSellPriceValue, (int)summary.AverageSellPrice)
        )
        {
            Changed(nameof(AverageSellPrice));
            Changed(nameof(AverageSellPriceValue));
        }

        if (
            summary.MaximumSellPrice is > 0 and <= int.MaxValue
            && Set(ref maximumSellPriceValue, (int)summary.MaximumSellPrice)
        )
        {
            Changed(nameof(MaximumSellPrice));
            Changed(nameof(MaximumSellPriceValue));
        }
    }

    private static string Available(bool available) => available ? "✓" : "—";
}

public sealed record SurfaceMiningHuntRowViewModel(
    string SearchGroup,
    string Material,
    string StartWith,
    string AlsoPossibleOn,
    string Geology,
    string SpecialClue,
    string AverageGalacticPrice,
    string PeakSellPrice,
    int AverageGalacticPriceValue,
    int PeakSellPriceValue
)
{
    public SurfaceMiningHuntRowViewModel(
        SurfaceMiningHuntReference reference,
        MiningCommodityPriceSummary? summary = null
    )
        : this(
            reference.SearchGroup,
            reference.Material,
            reference.StartWith,
            reference.AlsoPossibleOn,
            reference.Geology,
            reference.SpecialClue,
            $"{UseDailyPrice(summary?.AverageSellPrice, reference.AverageGalacticPrice):N0} CR/t",
            $"{UseDailyPrice(summary?.MaximumSellPrice, reference.PeakSellPrice):N0} CR/t",
            UseDailyPrice(summary?.AverageSellPrice, reference.AverageGalacticPrice),
            UseDailyPrice(summary?.MaximumSellPrice, reference.PeakSellPrice)
        ) { }

    private static int UseDailyPrice(long? daily, int snapshot) =>
        daily is > 0 and <= int.MaxValue ? (int)daily : snapshot;
}

public sealed class MineMapMarkerFilterViewModel : WorkspaceObservable
{
    private readonly Action changed;
    private bool isVisible;

    public MineMapMarkerFilterViewModel(string name, bool isVisible, Action changed)
    {
        Name = name;
        this.isVisible = isVisible;
        this.changed = changed;
    }

    public string Name { get; }

    public string ColorHex => MineMapControl.ColorFor(Name).ToString();

    public bool IsVisible
    {
        get => isVisible;
        set
        {
            if (Set(ref isVisible, value))
            {
                changed();
            }
        }
    }
}
