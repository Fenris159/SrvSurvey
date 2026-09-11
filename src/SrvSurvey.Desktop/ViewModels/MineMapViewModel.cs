using System.Globalization;
using System.Windows.Input;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Controls;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class MineMapViewModel : WorkspaceObservable, IDisposable
{
    private const double DefaultViewportZoom = 1;
    private readonly MineMapService service;
    private readonly MineMapSettingsStore settingsStore;
    private readonly Action<string> notify;
    private readonly Action<Guid> editBookmark;
    private readonly WorkspaceTableSorter surveySorter = new();
    private readonly WorkspaceTableSorter hotspotSorter = new();
    private readonly WorkspaceTableSorter surfaceHuntSorter = new();
    private readonly IReadOnlyList<SurfaceMiningCommodityRowViewModel> hotspotRows;
    private readonly IReadOnlyList<SurfaceMiningHuntRowViewModel> surfaceHuntRows;
    private IReadOnlyList<SurfaceMiningCommodityRowViewModel> miningReferenceRows;
    private MineMapCommandContext? context;
    private EliteStatus? status;
    private IReadOnlyList<MineMapSurveyRowViewModel> filteredSurveys = [];
    private IReadOnlyList<MineMapMarkerFilterViewModel> markerFilters = [];
    private string searchText = string.Empty;
    private string selectedContains = "All";
    private string selectedBodyType = "All";
    private int selectedTab;
    private double viewportZoom = DefaultViewportZoom;
    private bool onlyShowWhileOnGround;
    private MineMapSurveyRowViewModel? selectedSurveyRow;
    private MineMapSurvey? editorSurvey;
    private string statusText = string.Empty;

    public MineMapViewModel(
        string dataDirectory,
        MineMapSettingsStore settingsStore,
        Action<string> notify,
        Action? openSurfaceMiningGuide = null,
        Action<Guid>? editBookmark = null,
        BookmarkCatalog? bookmarkCatalog = null)
    {
        service = new MineMapService(dataDirectory, bookmarkCatalog);
        this.settingsStore = settingsStore;
        this.notify = notify;
        this.editBookmark = editBookmark ?? (_ => { });
        var preferences = settingsStore.Load();
        onlyShowWhileOnGround = preferences.OnlyShowWhileOnGround;
        var selectedReferenceCommodities = preferences
            .EffectiveMiningReferenceCommodities
            .Select(name => SurfaceMiningCommodityCatalog.TryResolve(name, out var commodity)
                ? commodity.Name
                : name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        hotspotRows = SurfaceMiningCommodityCatalog.All
            .Select(commodity => new SurfaceMiningCommodityRowViewModel(
                commodity,
                selectedReferenceCommodities.Contains(commodity.Name),
                OnMiningReferenceSelectionChanged))
            .ToArray();
        miningReferenceRows = ReadMiningReferenceRows();
        surfaceHuntRows = SurfaceMiningCommodityCatalog.HuntReferences
            .Select(reference => new SurfaceMiningHuntRowViewModel(reference))
            .ToArray();
        service.Changed += OnServiceChanged;
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
        OpenSurfaceMiningGuideCommand = new WorkspaceCommand(
            openSurfaceMiningGuide ?? (() => { }));
        EditSelectedBookmarkCommand = new WorkspaceCommand(() =>
        {
            if (SelectedSurveyRow is { } row)
            {
                this.editBookmark(row.Id);
            }
        });
        RefreshCatalog();
    }

    public int SelectedTab
    {
        get => selectedTab;
        set => Set(ref selectedTab, value);
    }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (Set(ref searchText, value)) RefreshFilteredSurveys();
        }
    }

    public string SelectedContains
    {
        get => selectedContains;
        set
        {
            if (Set(ref selectedContains, value)) RefreshFilteredSurveys();
        }
    }

    public string SelectedBodyType
    {
        get => selectedBodyType;
        set
        {
            if (Set(ref selectedBodyType, value)) RefreshFilteredSurveys();
        }
    }

    public IReadOnlyList<string> ContainsOptions { get; private set; } = ["All"];

    public IReadOnlyList<string> BodyTypeOptions { get; private set; } = ["All"];

    public IReadOnlyList<SurfaceMiningCommodityRowViewModel> HotspotRows =>
        hotspotSorter.Apply(hotspotRows);

    public IReadOnlyList<SurfaceMiningHuntRowViewModel> SurfaceHuntRows =>
        surfaceHuntSorter.Apply(surfaceHuntRows);

    public IReadOnlyList<SurfaceMiningCommodityRowViewModel> MiningReferenceRows =>
        miningReferenceRows;

    public bool ShouldShowMiningReference => MiningReferenceRows.Count > 0;

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

    public MineMapSurvey? ActiveSurvey => editorSurvey ?? service.ActiveSurvey;

    public bool HasActiveSurvey => ActiveSurvey is not null;

    public string LiveMapTitle => ActiveSurvey?.Name ?? "No live mining map";

    public string LiveMapDescription => ActiveSurvey is { } survey
        ? $"{survey.SystemName} · {survey.BodyName} · {survey.MineralAmount} mineral amount · {survey.Density} density"
        : "Stand on a mining-location border, face its center, and send .mining <heading> <number> <amount>/<density>, i.e. .mining 120 4 high/low.";

    public static string LiveMapCommandHelp =>
        "Stand on the mining-location border and face its center, then use .mining <heading> <number> <amount>/<density>, i.e. .mining 120 4 high/low.";

    public string LiveMapLocation => ActiveSurvey is { } survey
        ? $"{survey.Center.Latitude:0.000000}, {survey.Center.Longitude:0.000000}"
        : "—";

    public string SelectedMapSystem => ActiveSurvey?.SystemName ?? "—";

    public string SelectedMapBody => ActiveSurvey is { } survey
        ? GalacticBookmark.TrimSystemPrefix(survey.SystemName, survey.BodyName)
        : "—";

    public string SelectedMapSignal => ActiveSurvey?.LocationSignal.ToString(
        CultureInfo.InvariantCulture) ?? "—";

    public string SelectedMapAmount => ActiveSurvey?.MineralAmount.ToString() ?? "—";

    public string SelectedMapDensity => ActiveSurvey?.Density.ToString() ?? "—";

    public string MarkerCountText
    {
        get
        {
            if (ActiveSurvey is not { } survey)
            {
                return "No survey selected";
            }

            var suffix = survey.Markers.Count == 1 ? string.Empty : "s";
            return $"{survey.Markers.Count:N0} mapped deposit{suffix}";
        }
    }

    public SurfaceCoordinate? PlayerLocation => IsActiveSurveyCurrentContext
        ? context?.PlayerLocation
        : null;

    public double PlayerHeading => IsActiveSurveyCurrentContext
        ? status?.NormalizedHeading ?? 0
        : 0;

    public double ViewportZoom
    {
        get => viewportZoom;
        set
        {
            var normalized = double.IsFinite(value) ? Math.Clamp(value, 1, 15) : 1;
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
            if (!Set(ref onlyShowWhileOnGround, value)) return;
            try
            {
                SavePreferences();
                StatusText = string.Empty;
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
            {
                StatusText = "Surface Mining map setting changed for this session but could not be saved: " + exception.Message;
            }
            Changed(nameof(ShouldShowOverlay));
        }
    }

    public bool ShouldShowOverlay => IsActiveSurveyCurrentContext
        && status is { HasLatitudeLongitude: true }
        && (!OnlyShowWhileOnGround || IsOnGround(status));

    private bool IsActiveSurveyCurrentContext => ActiveSurvey is { } survey
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
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            StatusText = "Mining reference selection changed for this session but could not be saved: "
                + exception.Message;
        }

        miningReferenceRows = ReadMiningReferenceRows();
        Changed(nameof(HotspotRows));
        Changed(nameof(MiningReferenceRows));
        Changed(nameof(ShouldShowMiningReference));
    }

    private SurfaceMiningCommodityRowViewModel[] ReadMiningReferenceRows() =>
        hotspotRows.Where(row => row.IsInOverlay)
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private void SavePreferences() => settingsStore.Save(new MineMapPreferences(
        OnlyShowWhileOnGround,
        hotspotRows.Where(row => row.IsInOverlay)
            .Select(row => row.Name)
            .ToArray()));

    public MineMapSurveyRowViewModel? SelectedSurveyRow
    {
        get => selectedSurveyRow;
        set
        {
            if (Set(ref selectedSurveyRow, value) && value is { } row)
            {
                SelectSurvey(row);
            }
        }
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

    public WorkspaceSortIndicators SurveySortIndicators =>
        new(surveySorter.Indicator);

    public WorkspaceSortIndicators HotspotSortIndicators =>
        new(hotspotSorter.Indicator);

    public WorkspaceSortIndicators SurfaceHuntSortIndicators =>
        new(surfaceHuntSorter.Indicator);

    public ICommand OpenSurfaceMiningGuideCommand { get; }

    public ICommand EditSelectedBookmarkCommand { get; }

    public async Task ApplyUpdateAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        MineMapCommandContext? nextContext,
        EliteStatus? latestStatus,
        bool allowCommands)
    {
        context = nextContext;
        status = latestStatus;
        service.UpdateContext(nextContext);
        var results = await service.ApplyJournalEventsAsync(
            journalEvents,
            nextContext,
            allowCommands,
            CancellationToken.None);
        foreach (var message in results.Select(result => result.Message))
        {
            StatusText = message;
            notify(message);
        }
        if (results.Any(result => result.Succeeded && result.Survey is not null))
        {
            editorSurvey = null;
        }
        RefreshCatalog();
        RaiseLiveState();
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
            .Select(survey => new MineMapSurveyRowViewModel(survey))
            .SingleOrDefault();
        Changed(nameof(SelectedSurveyRow));
        SelectedTab = 1;
        RaiseLiveState();
        return true;
    }

    public void Dispose()
    {
        service.Changed -= OnServiceChanged;
        service.Dispose();
    }

    internal static MineMapViewModel CreateEditorPreview()
    {
        var root = Path.Combine(Path.GetTempPath(), "SrvSurvey-OverlayEditorPreview", "mine-map");
        var viewModel = new MineMapViewModel(
            root,
            new MineMapSettingsStore(Path.Combine(root, "ui-settings.json")),
            _ => { });
        var center = new SurfaceCoordinate(-18.4216, 74.0921);
        const double radius = 855_573.1875;
        var now = DateTimeOffset.UtcNow;
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
            MineralAmount = MineMapRating.High,
            Density = MineMapRating.Low,
            PlanetRadiusMeters = radius,
            Center = center,
            CreatedAt = now,
            UpdatedAt = now,
            Markers =
            [
                new MineMapMarker { Material = "Ruby", Location = MineMapService.GetDestination(center, 15, 1240, radius), CreatedAt = now },
                new MineMapMarker { Material = "Gold", Location = MineMapService.GetDestination(center, 90, 2100, radius), CreatedAt = now },
                new MineMapMarker { Material = "Thortveitite", Location = MineMapService.GetDestination(center, 225, 3400, radius), CreatedAt = now },
            ],
        };
        viewModel.context = new MineMapCommandContext(
            "preview", "Fenris", "Wille", 123456789,
            viewModel.editorSurvey.SystemPosition, 3, "Wille 2 C", "Rocky Ice body",
            129, radius, MineMapService.GetDestination(center, 190, 450, radius));
        viewModel.status = new EliteStatus
        {
            Flags = StatusFlags.InSrv | StatusFlags.HasLatLong,
            Heading = 25,
            PlanetRadius = (decimal)radius,
        };
        viewModel.RaiseLiveState();
        return viewModel;
    }

    private void OnServiceChanged(object? sender, EventArgs eventArgs)
    {
        RefreshCatalog();
        RaiseLiveState();
    }

    private void RefreshCatalog()
    {
        var surveys = CatalogSurveys;
        ContainsOptions = ["All", .. surveys.SelectMany(survey => survey.Markers)
            .Select(marker => marker.Material)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)];
        BodyTypeOptions = ["All", .. surveys.Select(survey => survey.BodyType)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)];
        if (!ContainsOptions.Contains(SelectedContains, StringComparer.OrdinalIgnoreCase)) SelectedContains = "All";
        if (!BodyTypeOptions.Contains(SelectedBodyType, StringComparer.OrdinalIgnoreCase)) SelectedBodyType = "All";
        Changed(nameof(ContainsOptions));
        Changed(nameof(BodyTypeOptions));
        RefreshMarkerFilters();
        RefreshFilteredSurveys();
    }

    private void RefreshFilteredSurveys()
    {
        var query = SearchText.Trim();
        FilteredSurveys = surveySorter.Apply(CatalogSurveys
            .Where(survey => string.Equals(SelectedContains, "All", StringComparison.OrdinalIgnoreCase)
                || survey.Markers.Any(marker => string.Equals(marker.Material, SelectedContains, StringComparison.OrdinalIgnoreCase)))
            .Where(survey => string.Equals(SelectedBodyType, "All", StringComparison.OrdinalIgnoreCase)
                || string.Equals(survey.BodyType, SelectedBodyType, StringComparison.OrdinalIgnoreCase))
            .Where(survey => query.Length == 0
                || Searchable(survey).Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(survey => new MineMapSurveyRowViewModel(survey))
            .ToArray());
    }

    private void RefreshMarkerFilters()
    {
        var previous = MarkerFilters.ToDictionary(filter => filter.Name, filter => filter.IsVisible, StringComparer.OrdinalIgnoreCase);
        MarkerFilters = ActiveSurvey?.Markers.Select(marker => marker.Material)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Select(value => new MineMapMarkerFilterViewModel(
                value,
                previous.GetValueOrDefault(value, true),
                RaiseMapState))
            .ToArray() ?? [];
        Changed(nameof(VisibleMaterials));
    }

    private void RaiseMapState()
    {
        Changed(nameof(VisibleMaterials));
    }

    public IReadOnlySet<string> VisibleMaterials => MarkerFilters
        .Where(filter => filter.IsVisible)
        .Select(filter => filter.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private void RaiseLiveState()
    {
        Changed(nameof(ActiveSurvey));
        Changed(nameof(HasActiveSurvey));
        Changed(nameof(LiveMapTitle));
        Changed(nameof(LiveMapDescription));
        Changed(nameof(LiveMapLocation));
        Changed(nameof(SelectedMapSystem));
        Changed(nameof(SelectedMapBody));
        Changed(nameof(SelectedMapSignal));
        Changed(nameof(SelectedMapAmount));
        Changed(nameof(SelectedMapDensity));
        Changed(nameof(MarkerCountText));
        Changed(nameof(PlayerLocation));
        Changed(nameof(PlayerHeading));
        Changed(nameof(ShouldShowOverlay));
        RefreshMarkerFilters();
    }

    private static bool IsOnGround(EliteStatus current) => current.OnFootOnPlanet
        || current.InSrv
        || current.InMainShip && current.Landed;

    private static string Searchable(MineMapSurvey survey) => string.Join(' ',
        survey.SystemName,
        survey.BodyName,
        survey.BodyType,
        survey.Name,
        survey.MineralAmount.ToString(),
        survey.Density.ToString(),
        survey.Notes,
        survey.SystemAddress.ToString(CultureInfo.InvariantCulture));

    private IReadOnlyList<MineMapSurvey> CatalogSurveys => service.Surveys;

}

public sealed record MineMapSurveyRowViewModel(
    Guid Id,
    string SignalNumber,
    string SystemName,
    string BodyName,
    string BodyType,
    string DistanceText,
    string ArrivalText,
    string MineralAmount,
    string Density,
    string UpdatedText,
    string Notes)
{
    private static readonly SrvSurvey.Core.Search.GalacticCoordinate Sol = new(0, 0, 0);

    public MineMapSurveyRowViewModel(MineMapSurvey survey)
        : this(
            survey.Id,
            survey.LocationSignal.ToString(CultureInfo.InvariantCulture),
            survey.SystemName,
            survey.BodyName,
            survey.BodyType,
            $"{Sol.DistanceTo(survey.SystemPosition):0.0} ly",
            $"{survey.ArrivalDistanceLs:0} ls",
            survey.MineralAmount.ToString(),
            survey.Density.ToString(),
            survey.UpdatedAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture),
            survey.Notes)
    {
    }

    public int SignalValue => int.Parse(SignalNumber, CultureInfo.InvariantCulture);

    public string DisplayBodyName => GalacticBookmark.TrimSystemPrefix(
        SystemName,
        BodyName);

    public double DistanceValue => double.Parse(
        DistanceText[..DistanceText.IndexOf(' ')],
        CultureInfo.CurrentCulture);

    public double ArrivalValue => double.Parse(
        ArrivalText[..ArrivalText.IndexOf(' ')],
        CultureInfo.CurrentCulture);

    public DateTime UpdatedValue => DateTime.Parse(
        UpdatedText,
        CultureInfo.CurrentCulture);
}

public sealed class SurfaceMiningCommodityRowViewModel : WorkspaceObservable
{
    private readonly Action selectionChanged;
    private bool isInOverlay;

    public SurfaceMiningCommodityRowViewModel(
        SurfaceMiningCommodity commodity,
        bool isInOverlay = false,
        Action? selectionChanged = null)
    {
        Category = commodity.Category;
        Name = commodity.Name;
        ColorHex = commodity.ColorHex;
        HighMetalContent = Available(commodity.HighMetalContent);
        MetalRich = Available(commodity.MetalRich);
        Rocky = Available(commodity.Rocky);
        RockyIce = Available(commodity.RockyIce);
        Icy = Available(commodity.Icy);
        AverageSellPrice = $"{commodity.AverageSellPrice:N0} CR/t";
        MaximumSellPrice = $"{commodity.MaximumSellPrice:N0} CR/t";
        BodyTypes = string.Join(", ", new[]
        {
            commodity.HighMetalContent ? "HMC" : null,
            commodity.MetalRich ? "MR" : null,
            commodity.Rocky ? "Rocky" : null,
            commodity.RockyIce ? "Rocky Ice" : null,
            commodity.Icy ? "Icy" : null,
        }.Where(value => value is not null));
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
    public string AverageSellPrice { get; }
    public string MaximumSellPrice { get; }
    public string BodyTypes { get; }

    public bool IsInOverlay
    {
        get => isInOverlay;
        set
        {
            if (!Set(ref isInOverlay, value)) return;
            selectionChanged();
        }
    }

    public int AverageSellPriceValue => ParsePrice(AverageSellPrice);

    public int MaximumSellPriceValue => ParsePrice(MaximumSellPrice);

    private static string Available(bool available) => available ? "✓" : "—";

    private static int ParsePrice(string value) => int.Parse(
        value[..value.IndexOf(' ')],
        NumberStyles.AllowThousands,
        CultureInfo.CurrentCulture);
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
    int PeakSellPriceValue)
{
    public SurfaceMiningHuntRowViewModel(SurfaceMiningHuntReference reference)
        : this(
            reference.SearchGroup,
            reference.Material,
            reference.StartWith,
            reference.AlsoPossibleOn,
            reference.Geology,
            reference.SpecialClue,
            $"{reference.AverageGalacticPrice:N0} CR/t",
            $"{reference.PeakSellPrice:N0} CR/t",
            reference.AverageGalacticPrice,
            reference.PeakSellPrice)
    {
    }
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
            if (Set(ref isVisible, value)) changed();
        }
    }
}
