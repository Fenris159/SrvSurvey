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
    private const double DefaultViewRadiusMeters = 5_000;
    private readonly MineMapService service;
    private readonly MineMapSettingsStore settingsStore;
    private readonly Action<string> notify;
    private MineMapCommandContext? context;
    private EliteStatus? status;
    private IReadOnlyList<MineMapSurveyRowViewModel> filteredSurveys = [];
    private IReadOnlyList<MineMapMarkerFilterViewModel> markerFilters = [];
    private string searchText = string.Empty;
    private string selectedContains = "All";
    private string selectedBodyType = "All";
    private int selectedTab;
    private double viewRadiusMeters = DefaultViewRadiusMeters;
    private bool onlyShowWhileOnGround;
    private MineMapSurveyRowViewModel? pendingDelete;
    private MineMapSurvey? editorSurvey;
    private string statusText = string.Empty;

    public MineMapViewModel(
        string dataDirectory,
        MineMapSettingsStore settingsStore,
        Action<string> notify)
    {
        service = new MineMapService(dataDirectory);
        this.settingsStore = settingsStore;
        this.notify = notify;
        onlyShowWhileOnGround = settingsStore.Load().OnlyShowWhileOnGround;
        service.Changed += OnServiceChanged;
        ZoomInCommand = new WorkspaceCommand(() => ViewRadiusMeters = Math.Max(2_500, ViewRadiusMeters - 500));
        ZoomOutCommand = new WorkspaceCommand(() => ViewRadiusMeters = Math.Min(10_000, ViewRadiusMeters + 500));
        ResetZoomCommand = new WorkspaceCommand(() => ViewRadiusMeters = DefaultViewRadiusMeters);
        ConfirmDeleteCommand = new WorkspaceCommand(() => _ = ConfirmDeleteAsync());
        CancelDeleteCommand = new WorkspaceCommand(() => PendingDelete = null);
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
        : "Stand on a mining-location border, face its center, and send .mining <heading> <number> <amount>/<density>.";

    public string LiveMapLocation => ActiveSurvey is { } survey
        ? $"{survey.Center.Latitude:0.000000}, {survey.Center.Longitude:0.000000}"
        : "—";

    public string MarkerCountText => ActiveSurvey is { } survey
        ? $"{survey.Markers.Count:N0} mapped deposit{(survey.Markers.Count == 1 ? string.Empty : "s")}"
        : "No survey selected";

    public SurfaceCoordinate? PlayerLocation => IsActiveSurveyCurrentContext
        ? context?.PlayerLocation
        : null;

    public double PlayerHeading => IsActiveSurveyCurrentContext
        ? status?.NormalizedHeading ?? 0
        : 0;

    public double ViewRadiusMeters
    {
        get => viewRadiusMeters;
        set
        {
            if (Set(ref viewRadiusMeters, Math.Clamp(value, 2_500, 10_000)))
            {
                Changed(nameof(ZoomText));
            }
        }
    }

    public string ZoomText => $"{ViewRadiusMeters / 1000:0.0} km radius";

    public bool OnlyShowWhileOnGround
    {
        get => onlyShowWhileOnGround;
        set
        {
            if (!Set(ref onlyShowWhileOnGround, value)) return;
            try
            {
                settingsStore.Save(new MineMapPreferences(value));
                StatusText = string.Empty;
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
            {
                StatusText = "Mine Map setting changed for this session but could not be saved: " + exception.Message;
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

    public MineMapSurveyRowViewModel? PendingDelete
    {
        get => pendingDelete;
        private set
        {
            if (Set(ref pendingDelete, value))
            {
                Changed(nameof(HasPendingDelete));
                Changed(nameof(DeletePrompt));
            }
        }
    }

    public bool HasPendingDelete => PendingDelete is not null;

    public string DeletePrompt => PendingDelete is { } row
        ? $"Delete {row.Name} on {row.BodyName}?"
        : string.Empty;

    public string StatusText
    {
        get => statusText;
        private set => Set(ref statusText, value);
    }

    public ICommand ZoomInCommand { get; }

    public ICommand ZoomOutCommand { get; }

    public ICommand ResetZoomCommand { get; }

    public ICommand ConfirmDeleteCommand { get; }

    public ICommand CancelDeleteCommand { get; }

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
        foreach (var result in results)
        {
            StatusText = result.Message;
            notify(result.Message);
        }
        RefreshCatalog();
        RaiseLiveState();
    }

    public void RequestDelete(MineMapSurveyRowViewModel row)
    {
        PendingDelete = row;
    }

    public void SelectSurvey(MineMapSurveyRowViewModel row)
    {
        if (!service.SelectSurvey(row.Id)) return;
        SelectedTab = 1;
        RaiseLiveState();
    }

    public void Dispose()
    {
        service.Changed -= OnServiceChanged;
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
                new MineMapMarker { Material = "ruby", Location = MineMapService.GetDestination(center, 15, 1240, radius), CreatedAt = now },
                new MineMapMarker { Material = "gold", Location = MineMapService.GetDestination(center, 90, 2100, radius), CreatedAt = now },
                new MineMapMarker { Material = "thorveitite", Location = MineMapService.GetDestination(center, 225, 3400, radius), CreatedAt = now },
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

    private async Task ConfirmDeleteAsync()
    {
        if (PendingDelete is not { } row) return;
        PendingDelete = null;
        try
        {
            if (await service.DeleteAsync(row.Id))
            {
                StatusText = $"Deleted {row.Name}.";
                notify(StatusText);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText = "The surface map could not be deleted: " + exception.Message;
        }
        RefreshCatalog();
        RaiseLiveState();
    }

    private void OnServiceChanged(object? sender, EventArgs eventArgs)
    {
        RefreshCatalog();
        RaiseLiveState();
    }

    private void RefreshCatalog()
    {
        ContainsOptions = ["All", .. service.Surveys.SelectMany(survey => survey.Markers)
            .Select(marker => marker.Material)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)];
        BodyTypeOptions = ["All", .. service.Surveys.Select(survey => survey.BodyType)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)];
        if (!ContainsOptions.Contains(SelectedContains, StringComparer.OrdinalIgnoreCase)) selectedContains = "All";
        if (!BodyTypeOptions.Contains(SelectedBodyType, StringComparer.OrdinalIgnoreCase)) selectedBodyType = "All";
        Changed(nameof(ContainsOptions));
        Changed(nameof(BodyTypeOptions));
        RefreshMarkerFilters();
        RefreshFilteredSurveys();
    }

    private void RefreshFilteredSurveys()
    {
        var query = SearchText.Trim();
        var currentPosition = context?.SystemPosition;
        FilteredSurveys = service.Surveys
            .Where(survey => string.Equals(SelectedContains, "All", StringComparison.OrdinalIgnoreCase)
                || survey.Markers.Any(marker => string.Equals(marker.Material, SelectedContains, StringComparison.OrdinalIgnoreCase)))
            .Where(survey => string.Equals(SelectedBodyType, "All", StringComparison.OrdinalIgnoreCase)
                || string.Equals(survey.BodyType, SelectedBodyType, StringComparison.OrdinalIgnoreCase))
            .Where(survey => query.Length == 0
                || Searchable(survey).Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(survey => new MineMapSurveyRowViewModel(
                survey,
                currentPosition is { } origin ? origin.DistanceTo(survey.SystemPosition) : null))
            .ToArray();
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
        survey.SystemAddress.ToString(CultureInfo.InvariantCulture));
}

public sealed record MineMapSurveyRowViewModel(
    Guid Id,
    string Name,
    string SystemName,
    string BodyName,
    string BodyType,
    string DistanceText,
    string ArrivalText,
    string MineralAmount,
    string Density,
    string UpdatedText)
{
    public MineMapSurveyRowViewModel(MineMapSurvey survey, double? distanceLy)
        : this(
            survey.Id,
            survey.Name,
            survey.SystemName,
            survey.BodyName,
            survey.BodyType,
            distanceLy is { } distance ? $"{distance:0.0} ly" : "—",
            $"{survey.ArrivalDistanceLs:0} ls",
            survey.MineralAmount.ToString(),
            survey.Density.ToString(),
            survey.UpdatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
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
