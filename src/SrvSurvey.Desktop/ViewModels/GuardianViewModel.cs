using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class GuardianViewModel : INotifyPropertyChanged
{
    private const string AllKinds = "All sites";
    private const string AllVisits = "All visits";
    private const string AllTypes = "All types";
    private static readonly IReadOnlyList<GuardianOverlaySizeOption>
        OverlaySizes =
        [
            new(0, 300, 400),
            new(1, 500, 500),
            new(2, 600, 700),
            new(3, 800, 1_000),
            new(4, 1_200, 1_200),
        ];

    private readonly GuardianSiteCatalog references;
    private readonly GuardianPublishedSiteCatalog publishedSites;
    private GuardianSiteTemplateCatalog templates;
    private GuardianSurveyCompletionCalculator completionCalculator;
    private readonly GuardianSiteMapProjector mapProjector = new();
    private readonly GuardianSiteProximityEvaluator proximityEvaluator = new();
    private readonly StatusBlinkDetector statusBlinkDetector;
    private readonly GuardianArtifactInventoryState artifactInventory = new();
    private readonly GuardianCommanderDataReader commanderDataReader;
    private readonly GuardianCommanderSurveyStore commanderSurveyStore;
    private readonly GuardianSurveyShareService surveyShareService;
    private readonly RamTahViewModel? ramTah;
    private readonly GuardianOverlaySettingsStore? overlaySettingsStore;
    private readonly Func<GuardianAerialAltitudes> aerialAltitudeProvider;
    private readonly IStarSystemResolver systemResolver;
    private readonly AsyncCommand refreshCommand;
    private readonly AsyncCommand toggleCurrentObeliskScannedCommand;
    private readonly AsyncCommand prepareShareBundleCommand;
    private readonly AsyncCommand lookupOriginCommand;
    private readonly AsyncCommand clearOriginCommand;
    private readonly AsyncCommand openSelectedSurveyCommand;
    private readonly AsyncCommand openShareWorkspaceCommand;
    private GuardianLiveSiteState liveSiteState;
    private GuardianCommanderDataReadResult commanderData =
        GuardianCommanderDataReadResult.Empty;
    private GuardianSiteVisitCatalog visits;
    private IReadOnlyList<GuardianSiteRowViewModel> rows = [];
    private GuardianSiteMapProjection? mapProjection;
    private GuardianSiteMapProjection? activeMapProjection;
    private GuardianSiteProximitySnapshot? proximity;
    private EliteStatus? currentStatus;
    private string filterText = string.Empty;
    private string selectedKindFilter = AllKinds;
    private string selectedVisitFilter = AllVisits;
    private string selectedSiteTypeFilter = AllTypes;
    private GuardianSiteRowViewModel? selectedSite;
    private GalacticCoordinate? currentPosition;
    private string? currentSystemName;
    private string originSystemName = string.Empty;
    private StarSystemReference? customOrigin;
    private bool isOriginLookupBusy;
    private string originLookupStatus =
        "Distances use the current journal system until a custom origin is selected.";
    private bool includeRamTahLogs;
    private bool showOnlyNeededRamTahLogs;
    private int selectedWorkspaceTabIndex;
    private string? activeFrontierId;
    private bool activeIsOdyssey = true;
    private bool isBusy;
    private string statusMessage;
    private string summary = string.Empty;
    private Func<string, Task>? clipboardWriter;
    private bool enableGuardianSites;
    private bool autoShowGuardianSummary;
    private bool autoShowRamTah;
    private bool suppressForActiveBuildProjects;
    private bool autoZoomNearObelisks;
    private bool autoZoomInSrvTurret;
    private bool showComponentMaterials;
    private bool showRuinsMeasurementGrid;
    private bool showAerialAlignmentGrid;
    private bool showMapNotes;
    private bool showMapLegend;
    private GuardianOverlaySizeOption selectedOverlaySize;
    private bool automaticMapZoom = true;
    private double activeMapScale = 1;
    private double activeMapRelativeHeading;
    private GuardianLiveMapMode liveMapMode = GuardianLiveMapMode.SiteType;
    private string? targetObeliskName;
    private bool hasActiveBuildProjects;
    private bool isSystemSummaryObscured;
    private string overlaySettingsStatus = string.Empty;
    private IReadOnlyList<string> shareSiteNames = [];
    private string? shareArchivePath;
    private string shareStatusMessage =
        "Prepare a bundle to find commander survey data not present in the published catalog.";
    private bool isPreparingShareBundle;
    private bool isBlinkGesturePrimed;

    public GuardianViewModel(
        string dataDirectory,
        GuardianSiteCatalog? references = null,
        GuardianPublishedSiteCatalog? publishedSites = null,
        GuardianSiteTemplateCatalog? templates = null,
        RamTahViewModel? ramTah = null,
        GuardianOverlaySettingsStore? overlaySettingsStore = null,
        IStarSystemResolver? systemResolver = null,
        Func<GuardianAerialAltitudes>? aerialAltitudeProvider = null,
        GuardianGesturePreferences? gesturePreferences = null)
    {
        this.references = references ?? GuardianSiteCatalog.LoadEmbedded();
        this.publishedSites = publishedSites
            ?? GuardianPublishedSiteCatalog.LoadEmbedded();
        this.templates = templates ?? GuardianSiteTemplateCatalog.LoadEmbedded();
        this.ramTah = ramTah;
        this.overlaySettingsStore = overlaySettingsStore;
        this.aerialAltitudeProvider = aerialAltitudeProvider
            ?? (() => GuardianAerialAltitudes.Default);
        this.systemResolver = systemResolver ?? new SpanshStarSystemResolver();
        var gestures = gesturePreferences ?? GuardianGesturePreferences.Default;
        statusBlinkDetector = new StatusBlinkDetector(
            gestures.BlinkTrigger,
            TimeSpan.FromMilliseconds(gestures.BlinkDurationMilliseconds));
        var overlayPreferences = overlaySettingsStore?.Load()
            ?? GuardianOverlayPreferences.Default;
        enableGuardianSites = overlayPreferences.EnableGuardianSites;
        autoShowGuardianSummary = overlayPreferences.AutoShowGuardianSummary;
        autoShowRamTah = overlayPreferences.AutoShowRamTah;
        suppressForActiveBuildProjects =
            overlayPreferences.SuppressForActiveBuildProjects;
        autoZoomNearObelisks = overlayPreferences.AutoZoomNearObelisks;
        autoZoomInSrvTurret = overlayPreferences.AutoZoomInSrvTurret;
        showComponentMaterials = overlayPreferences.ShowComponentMaterials;
        showRuinsMeasurementGrid =
            !overlayPreferences.DisableRuinsMeasurementGrid;
        showAerialAlignmentGrid =
            !overlayPreferences.DisableAerialAlignmentGrid;
        showMapNotes = overlayPreferences.ShowMapNotes;
        showMapLegend = overlayPreferences.ShowMapLegend;
        selectedOverlaySize = OverlaySizes[overlayPreferences.OverlaySizeIndex];
        if (this.ramTah is not null)
        {
            this.ramTah.PropertyChanged += (_, _) =>
            {
                NotifyCurrentObeliskChanged();
                NotifyAuxiliaryOverlayState();
                OnPropertyChanged(nameof(HasActiveRamTahMission));
                if (IncludeRamTahLogs)
                {
                    ApplyFilters();
                }
            };
        }
        completionCalculator = new GuardianSurveyCompletionCalculator(this.templates);
        commanderDataReader = new GuardianCommanderDataReader(dataDirectory);
        commanderSurveyStore = new GuardianCommanderSurveyStore(dataDirectory);
        surveyShareService = new GuardianSurveyShareService(
            dataDirectory,
            this.publishedSites);
        SurveyEditor = new GuardianSurveyEditorViewModel(
            commanderSurveyStore,
            OnSurveySavedAsync);
        TemplateAuthoring = new GuardianTemplateAuthoringViewModel(
            this.templates,
            OnTemplateDraftChanged);
        liveSiteState = new GuardianLiveSiteState(this.references);
        visits = GuardianSiteVisitCatalog.Merge(
            this.references,
            GuardianCommanderDataReadResult.Empty,
            this.publishedSites,
            completionCalculator);
        KindFilters =
        [
            AllKinds,
            "Beacons",
            "Ruins",
            "Structures",
        ];
        VisitFilters =
        [
            AllVisits,
            "Visited",
            "Unvisited",
        ];
        SiteTypeFilters =
        [
            AllTypes,
            .. this.references.Sites
                .Select(site => site.SiteType)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(siteType => siteType),
        ];
        statusMessage = "Reference data loaded. Commander visits will appear "
            + "after a journal profile is available.";
        refreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy);
        toggleCurrentObeliskScannedCommand = new AsyncCommand(
            ToggleCurrentObeliskScannedAsync,
            () => CurrentObelisk is not null && activeFrontierId is not null);
        prepareShareBundleCommand = new AsyncCommand(
            PrepareShareBundleAsync,
            () => activeFrontierId is not null && !isPreparingShareBundle);
        lookupOriginCommand = new AsyncCommand(
            LookupOriginAsync,
            () => !IsOriginLookupBusy
                && !string.IsNullOrWhiteSpace(OriginSystemName));
        clearOriginCommand = new AsyncCommand(
            ClearCustomOriginAsync,
            () => HasCustomOrigin);
        openSelectedSurveyCommand = new AsyncCommand(
            OpenSelectedSurveyAsync,
            () => SelectedSite?.Reference.Kind is GuardianSiteKind.Ruins
                or GuardianSiteKind.Structure);
        openShareWorkspaceCommand = new AsyncCommand(
            OpenShareWorkspaceAsync,
            () => true);
        RefreshCommand = refreshCommand;
        ToggleCurrentObeliskScannedCommand = toggleCurrentObeliskScannedCommand;
        PrepareShareBundleCommand = prepareShareBundleCommand;
        LookupOriginCommand = lookupOriginCommand;
        ClearOriginCommand = clearOriginCommand;
        OpenSelectedSurveyCommand = openSelectedSurveyCommand;
        OpenShareWorkspaceCommand = openShareWorkspaceCommand;
        ApplyFilters();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<string> KindFilters { get; }

    public IReadOnlyList<string> VisitFilters { get; }

    public IReadOnlyList<string> SiteTypeFilters { get; }

    public IReadOnlyList<GuardianOverlaySizeOption> OverlaySizeOptions =>
        OverlaySizes;

    public ICommand RefreshCommand { get; }

    public ICommand ToggleCurrentObeliskScannedCommand { get; }

    public ICommand PrepareShareBundleCommand { get; }

    public ICommand LookupOriginCommand { get; }

    public ICommand ClearOriginCommand { get; }

    public ICommand OpenSelectedSurveyCommand { get; }

    public ICommand OpenShareWorkspaceCommand { get; }

    public GuardianSurveyEditorViewModel SurveyEditor { get; }

    public GuardianTemplateAuthoringViewModel TemplateAuthoring { get; }

    public IReadOnlyList<string> ShareSiteNames
    {
        get => shareSiteNames;
        private set
        {
            if (SetField(ref shareSiteNames, value))
            {
                OnPropertyChanged(nameof(HasShareSites));
            }
        }
    }

    public bool HasShareSites => ShareSiteNames.Count > 0;

    public string? ShareArchivePath
    {
        get => shareArchivePath;
        private set
        {
            if (SetField(ref shareArchivePath, value))
            {
                OnPropertyChanged(nameof(HasShareArchive));
            }
        }
    }

    public bool HasShareArchive => !string.IsNullOrWhiteSpace(ShareArchivePath);

    public string ShareStatusMessage
    {
        get => shareStatusMessage;
        private set => SetField(ref shareStatusMessage, value);
    }

    public string ShareButtonText => isPreparingShareBundle
        ? "Preparing..."
        : "Prepare survey bundle";

    public bool EnableGuardianSites
    {
        get => enableGuardianSites;
        set
        {
            if (SetField(ref enableGuardianSites, value))
            {
                SaveOverlayPreferences();
                NotifyAuxiliaryOverlayState();
            }
        }
    }

    public bool AutoShowGuardianSummary
    {
        get => autoShowGuardianSummary;
        set
        {
            if (SetField(ref autoShowGuardianSummary, value))
            {
                SaveOverlayPreferences();
                NotifyAuxiliaryOverlayState();
            }
        }
    }

    public bool AutoShowRamTah
    {
        get => autoShowRamTah;
        set
        {
            if (SetField(ref autoShowRamTah, value))
            {
                SaveOverlayPreferences();
                NotifyAuxiliaryOverlayState();
            }
        }
    }

    public bool SuppressForActiveBuildProjects
    {
        get => suppressForActiveBuildProjects;
        set
        {
            if (SetField(ref suppressForActiveBuildProjects, value))
            {
                SaveOverlayPreferences();
                NotifyAuxiliaryOverlayState();
            }
        }
    }

    public bool AutoZoomNearObelisks
    {
        get => autoZoomNearObelisks;
        set
        {
            if (SetField(ref autoZoomNearObelisks, value))
            {
                SaveOverlayPreferences();
                RefreshAutomaticMapScale();
            }
        }
    }

    public bool AutoZoomInSrvTurret
    {
        get => autoZoomInSrvTurret;
        set
        {
            if (SetField(ref autoZoomInSrvTurret, value))
            {
                SaveOverlayPreferences();
                RefreshAutomaticMapScale();
            }
        }
    }

    public bool ShowRuinsMeasurementGrid
    {
        get => showRuinsMeasurementGrid;
        set
        {
            if (SetField(ref showRuinsMeasurementGrid, value))
            {
                SaveOverlayPreferences();
                NotifyGuardianGuidanceChanged();
            }
        }
    }

    public bool ShowComponentMaterials
    {
        get => showComponentMaterials;
        set
        {
            if (SetField(ref showComponentMaterials, value))
            {
                SaveOverlayPreferences();
                UpdateMapProjection();
                UpdateSurveyEditor();
                UpdateProximity();
            }
        }
    }

    public bool ShowAerialAlignmentGrid
    {
        get => showAerialAlignmentGrid;
        set
        {
            if (SetField(ref showAerialAlignmentGrid, value))
            {
                SaveOverlayPreferences();
                NotifyGuardianGuidanceChanged();
            }
        }
    }

    public bool ShowMapNotes
    {
        get => showMapNotes;
        set
        {
            if (SetField(ref showMapNotes, value))
            {
                SaveOverlayPreferences();
                OnPropertyChanged(nameof(ShouldShowMapNotes));
            }
        }
    }

    public bool ShouldShowMapNotes => ShowMapNotes && HasSelectedSite;

    public bool ShowMapLegend
    {
        get => showMapLegend;
        set
        {
            if (SetField(ref showMapLegend, value))
            {
                SaveOverlayPreferences();
            }
        }
    }

    public GuardianOverlaySizeOption SelectedOverlaySize
    {
        get => selectedOverlaySize;
        set
        {
            var normalized = OverlaySizes.FirstOrDefault(option =>
                    option.Index == value?.Index)
                ?? OverlaySizes[0];
            if (SetField(ref selectedOverlaySize, normalized))
            {
                SaveOverlayPreferences();
                OnPropertyChanged(nameof(PreferredOverlayWidth));
                OnPropertyChanged(nameof(PreferredOverlayHeight));
            }
        }
    }

    public int PreferredOverlayWidth => SelectedOverlaySize.Width;

    public int PreferredOverlayHeight => SelectedOverlaySize.Height;

    public bool IsAutomaticMapZoom => automaticMapZoom;

    public double ActiveMapScale => activeMapScale;

    public double ActiveMapRelativeHeading => activeMapRelativeHeading;

    public GuardianLiveMapMode LiveMapMode
    {
        get => liveMapMode;
        private set
        {
            if (SetField(ref liveMapMode, value))
            {
                OnPropertyChanged(nameof(HasLiveMapPrompt));
                OnPropertyChanged(nameof(LiveMapPromptTitle));
                OnPropertyChanged(nameof(LiveMapPromptText));
                NotifyGuardianGuidanceChanged();
            }
        }
    }

    public bool HasLiveMapPrompt => LiveMapMode != GuardianLiveMapMode.Map;

    public bool IsBlinkGesturePrimed
    {
        get => isBlinkGesturePrimed;
        private set
        {
            if (SetField(ref isBlinkGesturePrimed, value))
            {
                OnPropertyChanged(nameof(BlinkGestureText));
            }
        }
    }

    public string BlinkGestureText => IsBlinkGesturePrimed
        ? "GESTURE READY · toggle once more to confirm"
        : currentStatus?.OnFoot == true
            ? "Toggle shields twice to confirm the nearby Guardian action"
            : "Toggle cockpit mode twice to confirm the nearby Guardian action";

    public string LiveMapPromptTitle => LiveMapMode switch
    {
        GuardianLiveMapMode.SiteType => "IDENTIFY SITE TYPE",
        GuardianLiveMapMode.Heading => "ALIGN SITE HEADING",
        GuardianLiveMapMode.Origin => "ALIGN SITE ORIGIN",
        _ => "GUARDIAN SITE MAP",
    };

    public string LiveMapPromptText => LiveMapMode switch
    {
        GuardianLiveMapMode.SiteType =>
            "Type A, B, or G for ruins, or .site <type> for any mapped Guardian layout.",
        GuardianLiveMapMode.Heading =>
            "Face the mapped alignment feature and type .heading, or use .heading <degrees>.",
        GuardianLiveMapMode.Origin =>
            "Use the origin guidance to align an aerial screenshot. Type .map to return.",
        _ => string.Empty,
    };

    public GuardianAlignmentMode? AlignmentMode => LiveMapMode switch
    {
        GuardianLiveMapMode.Heading => GuardianAlignmentMode.Buttress,
        GuardianLiveMapMode.Origin when currentStatus?.OnFoot == true =>
            GuardianAlignmentMode.RelicTower,
        GuardianLiveMapMode.Origin => ParseAlignmentMode(GetActiveSiteType()),
        _ => null,
    };

    public double AlignmentTargetAltitude => AlignmentMode switch
    {
        GuardianAlignmentMode.Buttress => 20,
        GuardianAlignmentMode.RelicTower => 0,
        GuardianAlignmentMode.Alpha => aerialAltitudeProvider().Alpha,
        GuardianAlignmentMode.Beta => aerialAltitudeProvider().Beta,
        GuardianAlignmentMode.Gamma => aerialAltitudeProvider().Gamma,
        GuardianAlignmentMode.Robolobster => 1_000,
        GuardianAlignmentMode.Crossroads => 500,
        GuardianAlignmentMode.Fistbump => 450,
        null => 0,
        _ => 650,
    };

    public double AlignmentOpacity
    {
        get
        {
            if (AlignmentMode is not { } mode
                || currentStatus?.Landed == true
                || mode == GuardianAlignmentMode.Buttress
                    && !ShowRuinsMeasurementGrid
                || LiveMapMode == GuardianLiveMapMode.Origin
                    && (!ShowAerialAlignmentGrid
                        || currentStatus?.InSrv == true))
            {
                return 0;
            }

            if (mode == GuardianAlignmentMode.RelicTower)
            {
                return 0.8;
            }

            var altitude = Math.Max(0, currentStatus?.Altitude ?? 0);
            var delta = Math.Abs(altitude - AlignmentTargetAltitude);
            return delta > 220
                ? 0
                : delta < 20
                    ? 0.8
                    : (220 - delta) / 200;
        }
    }

    public bool IsAlignmentVisible => AlignmentOpacity > 0;

    public double AlignmentHeading => currentStatus?.NormalizedHeading ?? 0;

    public string AlignmentStatusText => AlignmentMode switch
    {
        GuardianAlignmentMode.Buttress =>
            $"Heading {AlignmentHeading:N0}° - align with the site buttress.",
        GuardianAlignmentMode.RelicTower =>
            $"Heading {AlignmentHeading:N0}° - align the relic tower.",
        not null => $"Heading {AlignmentHeading:N0}° - altitude "
            + $"{Math.Max(0, currentStatus?.Altitude ?? 0):N0} / "
            + $"{AlignmentTargetAltitude:N0} m.",
        _ => string.Empty,
    };

    public bool IsGlideApproach => currentStatus?.GlideMode == true
        && ActiveSite is not null;

    public string GlideApproachTitle => ActiveSite?.Kind == GuardianSiteKind.Ruins
        ? "APPROACHING GUARDIAN RUINS"
        : "APPROACHING GUARDIAN STRUCTURE";

    public string GlideApproachText => ActiveSite is not { } site
        ? string.Empty
        : site.Kind == GuardianSiteKind.Ruins
            ? $"Ruins #{site.Index} - {GetActiveSiteType() ?? "unknown layout"}"
            : GetGuardianBlueprintText(GetActiveSiteType());

    public string GlideApproachFooter =>
        "Remain in glide; the live survey map will continue after approach.";

    public void RefreshAerialGuidance()
    {
        NotifyGuardianGuidanceChanged();
    }

    public string? TargetObeliskName => targetObeliskName;

    public bool HasTargetObelisk => TargetObeliskName is not null;

    public string TargetObeliskText => TargetObeliskName is { } name
        ? $"TARGET {name} - {GetTargetObeliskDistance():N1} m"
        : SiteDistanceText;

    public string ActiveMapScaleText => automaticMapZoom
        ? $"AUTO - {ActiveMapScale:N2}x"
        : $"MANUAL - {ActiveMapScale:N2}x";

    public bool AdjustMapZoom(bool zoomIn)
    {
        var next = Math.Round(
            ActiveMapScale + (zoomIn ? 0.5 : -0.5),
            2);
        if (next is < 0.5 or > 15)
        {
            return false;
        }

        automaticMapZoom = next == GetAutomaticMapScale();
        activeMapScale = next;
        OnPropertyChanged(nameof(IsAutomaticMapZoom));
        OnPropertyChanged(nameof(ActiveMapScale));
        OnPropertyChanged(nameof(ActiveMapScaleText));
        return true;
    }

    public void EnableAutomaticMapZoom()
    {
        automaticMapZoom = true;
        RefreshAutomaticMapScale();
        OnPropertyChanged(nameof(IsAutomaticMapZoom));
        OnPropertyChanged(nameof(ActiveMapScaleText));
    }

    public string OverlaySettingsStatus
    {
        get => overlaySettingsStatus;
        private set
        {
            if (SetField(ref overlaySettingsStatus, value))
            {
                OnPropertyChanged(nameof(HasOverlaySettingsStatus));
            }
        }
    }

    public bool HasOverlaySettingsStatus =>
        !string.IsNullOrWhiteSpace(OverlaySettingsStatus);

    public IReadOnlyList<GuardianSiteRowViewModel> CurrentSystemSites => visits
        .Visits
        .Where(visit => visit.Reference.Kind != GuardianSiteKind.Beacon
            && string.Equals(
                visit.Reference.SystemName,
                currentSystemName,
                StringComparison.OrdinalIgnoreCase))
        .Select(visit => new GuardianSiteRowViewModel(
            visit,
            0,
            IsCurrentDestination(visit.Reference)))
        .OrderBy(row => row.Reference.BodyName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(row => row.Reference.Index)
        .ToArray();

    public bool HasCurrentSystemSites => CurrentSystemSites.Count > 0;

    public string CurrentSystemGuardianTitle => CurrentSystemSites.Count switch
    {
        1 => "1 Guardian site in this system",
        var count => $"{count:N0} Guardian sites in this system",
    };

    public IReadOnlyList<GuardianRamTahLogViewModel> CurrentRamTahLogs =>
        BuildCurrentRamTahLogs();

    public bool HasCurrentRamTahLogs => CurrentRamTahLogs.Count > 0;

    public string CurrentRamTahTitle => CurrentRamTahLogs.Count switch
    {
        0 => "No new Ram Tah logs at this site",
        1 => "1 Ram Tah log needed at this site",
        var count => $"{count:N0} Ram Tah logs needed at this site",
    };

    public bool ShouldShowGuardianSystemSummary => EnableGuardianSites
        && AutoShowGuardianSummary
        && !ShouldSuppressAuxiliaryOverlays
        && !isSystemSummaryObscured
        && HasCurrentSystemSites
        && IsGuardianSummaryStatusEligible(currentStatus);

    public bool ShouldShowRamTahOverlay => EnableGuardianSites
        && AutoShowRamTah
        && !ShouldSuppressAuxiliaryOverlays
        && ActiveSite is not null
        && ramTah?.IsAnyMissionActive == true
        && IsActiveSiteRelevantToRamTahMission()
        && currentStatus?.HasLatitudeLongitude == true
        && IsRamTahStatusEligible(currentStatus);

    private bool ShouldSuppressAuxiliaryOverlays =>
        SuppressForActiveBuildProjects && hasActiveBuildProjects;

    public IReadOnlyList<GuardianSiteRowViewModel> Rows
    {
        get => rows;
        private set => SetField(ref rows, value);
    }

    public GuardianSiteRowViewModel? SelectedSite
    {
        get => selectedSite;
        set
        {
            if (SetField(ref selectedSite, value))
            {
                OnPropertyChanged(nameof(HasSelectedSite));
                OnPropertyChanged(nameof(HasSelectedSurvey));
                OnPropertyChanged(nameof(ShouldShowMapNotes));
                OnPropertyChanged(nameof(SelectedCanonnUri));
                OnPropertyChanged(nameof(SelectedSpanshUri));
                OnPropertyChanged(nameof(SelectedEdsmUri));
                openSelectedSurveyCommand.RaiseCanExecuteChanged();
                UpdateMapProjection();
                UpdateSurveyEditor();
                UpdateProximity();
            }
        }
    }

    public bool HasSelectedSite => SelectedSite is not null;

    public bool HasSelectedSurvey => SelectedSite?.Reference.Kind
        is GuardianSiteKind.Ruins or GuardianSiteKind.Structure;

    public int SelectedWorkspaceTabIndex
    {
        get => selectedWorkspaceTabIndex;
        set => SetField(ref selectedWorkspaceTabIndex, value);
    }

    public Uri? SelectedCanonnUri => SelectedSite is { } row
        ? new Uri(
            "https://canonn-science.github.io/canonn-signals/?system="
                + Uri.EscapeDataString(row.Reference.SystemName))
        : null;

    public Uri? SelectedSpanshUri => SelectedSite is { } row
        ? new Uri($"https://spansh.co.uk/system/{row.Reference.SystemAddress}")
        : null;

    public Uri? SelectedEdsmUri => SelectedSite is { } row
        ? new Uri(
            $"https://www.edsm.net/en/system?systemID64={row.Reference.SystemAddress}")
        : null;

    public GuardianSiteMapProjection? MapProjection
    {
        get => mapProjection;
        private set => SetField(ref mapProjection, value);
    }

    public GuardianSiteMapProjection? ActiveMapProjection => activeMapProjection;

    public string ActiveMapTitle => ActiveSite is { } site
        ? $"{FindSurvey(site)?.SiteType ?? site.SiteType} · {site.BodyName}"
        : "Guardian site map";

    public string ActiveMapSummary => ActiveMapProjection is { } projection
        ? $"{projection.Points.Count:N0} mapped objects · "
            + $"{projection.ConfirmedPointCount:N0}/"
            + $"{projection.SurveyablePointCount:N0} confirmed"
        : "No active Guardian map is available.";

    public string MapTitle => SelectedSite is { } row
        ? $"{row.DisplayId} · {row.SiteDescription}"
        : "Select a Guardian site";

    public string MapSummary => MapProjection is { } projection
        ? $"{projection.Points.Count:N0} mapped objects · "
            + $"{projection.ConfirmedPointCount:N0} of "
            + $"{projection.SurveyablePointCount:N0} survey points confirmed"
        : "No compatible map template is available.";

    public string MapStatus => SelectedSite is { } row
        ? row.Visit.HasCommanderData
            ? "Commander survey states and raw POIs are overlaid on the reference map."
            : "Reference map only. Visit this site to begin a commander survey."
        : "Choose a site on the Sites & surveys tab.";

    public GuardianLiveSiteSnapshot? ActiveSite => liveSiteState.CurrentSite;

    public bool HasActiveSite => ActiveSite is not null;

    public bool ShouldShowLiveSiteOverlay => EnableGuardianSites
        && HasActiveSite
        && !(SuppressForActiveBuildProjects && hasActiveBuildProjects)
        && IsLiveSiteStatusEligible(currentStatus);

    public string ActiveSiteTitle => ActiveSite is { } site
        ? string.IsNullOrWhiteSpace(site.LocalizedName)
            ? site.Kind == GuardianSiteKind.Ruins
                ? $"Ancient Ruins ({site.Index})"
                : "Guardian Structure"
            : site.LocalizedName
        : "No live Guardian site detected";

    public string ActiveSiteDescription => ActiveSite is { } site
        ? $"{FindSurvey(site)?.SiteType ?? site.SiteType} "
            + $"{site.Kind.ToString().ToLowerInvariant()} on "
            + $"{site.BodyName}"
        : "Approach a Guardian ruins or structure settlement to activate its survey.";

    public string ActiveSiteReference => ActiveSite is { } site
        ? site.Reference?.DisplayId ?? "Uncatalogued site"
        : "WAITING";

    public string ActiveSiteLocation => ActiveSite?.Location is { } location
        ? FormattableString.Invariant(
            $"{location.Latitude:F6}, {location.Longitude:F6}")
        : "Surface location unavailable";

    public string ActiveSiteVisit => ActiveSite is { } site
        ? $"Last approach {site.LastVisited.ToLocalTime():g}"
        : "Journal monitoring is active.";

    public GuardianSiteProximitySnapshot? Proximity => proximity;

    public double? CurrentAltitude => currentStatus?.Altitude;

    public GuardianObelisk? CurrentObelisk => Proximity?.CurrentObelisk;

    public bool HasCurrentObelisk => CurrentObelisk is not null;

    public string SiteDistanceText => Proximity is { } value
        ? $"{value.DistanceFromSite:N1} m from survey origin"
        : HasActiveSite
            ? "Waiting for surface position, body radius, and site heading."
            : "No live Guardian site detected.";

    public string NearbyPointText => Proximity?.NearestPoint is { } nearby
        ? GetNearbyPointText(nearby)
        : HasActiveSite
            ? "No selectable mapped object is available."
            : "Approach a Guardian site to begin proximity tracking.";

    private string GetNearbyPointText(GuardianNearbyPoint nearby)
    {
        if (nearby.Point.Type == GuardianPoiType.DestructiblePanel)
        {
            var survey = ActiveSite is { } site ? FindSurvey(site) : null;
            var material = survey?.Survey.ComponentMaterials
                .GetValueOrDefault(nearby.Point.Name)
                ?.GetItem(0) ?? GuardianComponentMaterial.Unknown;
            return $"Destructible panel {nearby.Point.Name}: "
                + $"{GetComponentMaterialName(material)} · "
                + $"{nearby.Distance:N1} m";
        }

        return $"Nearest: {nearby.Point.Name} · {nearby.Point.Type} · "
            + $"{nearby.Distance:N1} m";
    }

    private static string GetComponentMaterialName(
        GuardianComponentMaterial material)
    {
        return material switch
        {
            GuardianComponentMaterial.Unknown => "?",
            GuardianComponentMaterial.Cell => "Power Cell",
            GuardianComponentMaterial.Conduit => "Power Conduit",
            GuardianComponentMaterial.Tech => "Technology Component",
            _ => material.ToString(),
        };
    }

    public string CurrentObeliskTitle => CurrentObelisk is { } obelisk
        ? $"{obelisk.Name} · active obelisk"
        : "No current active obelisk";

    public string CurrentObeliskLogText => CurrentObelisk is { } obelisk
        ? $"Log: {GetLogDisplayName(obelisk.LogCode)}"
        : "Move within 25 m of an active obelisk in an SRV or on foot.";

    public string CurrentObeliskRequirementsText => CurrentObelisk is { } obelisk
        ? artifactInventory.GetRequirements(obelisk.ItemCodes) is { Count: > 0 }
            requirements
                ? string.Join(
                    " + ",
                    requirements.Select(requirement =>
                        $"{requirement.DisplayName} "
                        + $"{requirement.Available}/{requirement.Required}"))
                : "No artifact requirement is recorded."
        : "Artifact requirements will appear here.";

    public bool HasCurrentObeliskArtifacts => CurrentObelisk is { } obelisk
        && artifactInventory.HasItems(obelisk.ItemCodes);

    public string CurrentObeliskArtifactStatus => CurrentObelisk is null
        ? "INACTIVE"
        : HasCurrentObeliskArtifacts
            ? "ARTIFACTS READY"
            : "ARTIFACTS MISSING";

    public string CurrentObeliskMissionStatus => CurrentObelisk is not { } obelisk
        ? "No current obelisk is available for mission tracking."
        : ramTah is null
            ? "Ram Tah tracking is unavailable."
            : !ramTah.IsAnyMissionActive
                ? "No Ram Tah mission is active."
                : ramTah.IsLogCompleted(GetMission(), obelisk.LogCode)
                    ? "Ram Tah log already acquired."
                    : "Needed for the active Ram Tah mission.";

    public string ToggleCurrentObeliskScannedText => CurrentObelisk?.Scanned == true
        ? "Mark not scanned"
        : "Mark scanned";

    public string CurrentObeliskScanStatus => CurrentObelisk is { } obelisk
        ? obelisk.Scanned
            ? "SCANNED"
            : "NOT SCANNED"
        : "NO OBELISK";

    public string FilterText
    {
        get => filterText;
        set
        {
            if (SetField(ref filterText, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedKindFilter
    {
        get => selectedKindFilter;
        set
        {
            if (SetField(ref selectedKindFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedVisitFilter
    {
        get => selectedVisitFilter;
        set
        {
            if (SetField(ref selectedVisitFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedSiteTypeFilter
    {
        get => selectedSiteTypeFilter;
        set
        {
            if (SetField(ref selectedSiteTypeFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public bool IncludeRamTahLogs
    {
        get => includeRamTahLogs;
        set
        {
            if (SetField(ref includeRamTahLogs, value))
            {
                ApplyFilters();
            }
        }
    }

    public bool ShowOnlyNeededRamTahLogs
    {
        get => showOnlyNeededRamTahLogs;
        set
        {
            if (SetField(ref showOnlyNeededRamTahLogs, value))
            {
                ApplyFilters();
            }
        }
    }

    public bool HasActiveRamTahMission => ramTah?.IsAnyMissionActive == true;

    public string OriginSystemName
    {
        get => originSystemName;
        set
        {
            if (SetField(ref originSystemName, value))
            {
                lookupOriginCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasCustomOrigin => customOrigin is not null;

    public bool IsOriginLookupBusy
    {
        get => isOriginLookupBusy;
        private set
        {
            if (SetField(ref isOriginLookupBusy, value))
            {
                lookupOriginCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(OriginLookupButtonText));
            }
        }
    }

    public string OriginLookupButtonText => IsOriginLookupBusy
        ? "Looking up..."
        : "Use origin";

    public string OriginLookupStatus
    {
        get => originLookupStatus;
        private set => SetField(ref originLookupStatus, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                refreshCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(RefreshButtonText));
            }
        }
    }

    public string RefreshButtonText => IsBusy ? "Refreshing..." : "Refresh sites";

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public string Summary
    {
        get => summary;
        private set => SetField(ref summary, value);
    }

    public string OriginStatus => customOrigin is { } origin
        ? $"Distances from custom origin {origin.Name}."
        : currentPosition is null
            ? "Distances unavailable until a journal supplies galactic coordinates."
            : $"Distances from {currentSystemName ?? "current system"}.";

    public void SetClipboardWriter(Func<string, Task>? writer)
    {
        clipboardWriter = writer;
    }

    public async Task CopyShareArchivePathAsync()
    {
        if (ShareArchivePath is null || clipboardWriter is null)
        {
            ShareStatusMessage = "The survey bundle path is not available to copy.";
            return;
        }

        try
        {
            await clipboardWriter(ShareArchivePath);
            ShareStatusMessage = "The survey bundle path was copied to the clipboard.";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or IOException
                or NotSupportedException
                or System.Runtime.InteropServices.ExternalException
                or UnauthorizedAccessException)
        {
            ShareStatusMessage = "The survey bundle path could not be copied: "
                + exception.Message;
        }
    }

    public void ReportShareLaunch(string message)
    {
        ShareStatusMessage = message;
    }

    public void ReportSelectedSiteLaunch(string message)
    {
        StatusMessage = message;
    }

    public async Task LookupOriginAsync()
    {
        var query = OriginSystemName.Trim();
        if (query.Length == 0)
        {
            OriginLookupStatus = "Enter a star-system name to set a custom origin.";
            return;
        }

        IsOriginLookupBusy = true;
        try
        {
            var matches = await systemResolver.SearchAsync(query);
            var match = matches.FirstOrDefault(candidate => string.Equals(
                    candidate.Name,
                    query,
                    StringComparison.OrdinalIgnoreCase))
                ?? matches.FirstOrDefault();
            if (match is null)
            {
                OriginLookupStatus = $"No star system matched '{query}'.";
                return;
            }

            customOrigin = match;
            OriginSystemName = match.Name;
            OnPropertyChanged(nameof(HasCustomOrigin));
            OnPropertyChanged(nameof(OriginStatus));
            clearOriginCommand.RaiseCanExecuteChanged();
            OriginLookupStatus = $"Using {match.Name} {match.Position} as the distance origin.";
            ApplyFilters();
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or InvalidDataException
                or TaskCanceledException)
        {
            OriginLookupStatus = "The star-system lookup failed: "
                + exception.Message;
        }
        finally
        {
            IsOriginLookupBusy = false;
        }
    }

    public Task ClearCustomOriginAsync()
    {
        customOrigin = null;
        OriginSystemName = string.Empty;
        OnPropertyChanged(nameof(HasCustomOrigin));
        OnPropertyChanged(nameof(OriginStatus));
        clearOriginCommand.RaiseCanExecuteChanged();
        OriginLookupStatus = currentPosition is null
            ? "Custom origin cleared. Distances will appear when the journal supplies coordinates."
            : $"Custom origin cleared. Distances now use {currentSystemName ?? "the current system"}.";
        ApplyFilters();
        return Task.CompletedTask;
    }

    private Task OpenSelectedSurveyAsync()
    {
        if (HasSelectedSurvey)
        {
            SelectedWorkspaceTabIndex = 1;
            StatusMessage = $"Opened the {SelectedSite!.DisplayId} survey workspace.";
        }

        return Task.CompletedTask;
    }

    private Task OpenShareWorkspaceAsync()
    {
        SelectedWorkspaceTabIndex = 2;
        return Task.CompletedTask;
    }

    public void SetActiveBuildProjects(bool hasProjects)
    {
        if (hasActiveBuildProjects == hasProjects)
        {
            return;
        }

        hasActiveBuildProjects = hasProjects;
        OnPropertyChanged(nameof(ShouldShowLiveSiteOverlay));
        NotifyAuxiliaryOverlayState();
    }

    public void SetSystemSummaryObscured(bool obscured)
    {
        if (isSystemSummaryObscured == obscured)
        {
            return;
        }

        isSystemSummaryObscured = obscured;
        OnPropertyChanged(nameof(ShouldShowGuardianSystemSummary));
    }

    public void UpdateCurrentSystem(
        string? systemName,
        GalacticCoordinate? position)
    {
        if (string.Equals(
                currentSystemName,
                systemName,
                StringComparison.Ordinal)
            && currentPosition == position)
        {
            return;
        }

        currentSystemName = systemName;
        currentPosition = position;
        OnPropertyChanged(nameof(OriginStatus));
        SelectedSite = null;
        ApplyFilters();
        NotifyAuxiliaryOverlayState();
    }

    public void UpdateStatus(EliteStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        currentStatus = status;
        OnPropertyChanged(nameof(BlinkGestureText));
        var blink = statusBlinkDetector.Update(status, DateTimeOffset.UtcNow);
        IsBlinkGesturePrimed = blink.IsPrimed;
        UpdateProximity();
        NotifyAuxiliaryOverlayState();
    }

    public async Task UpdateStatusAsync(
        EliteStatus status,
        bool allowGesture,
        DateTimeOffset? observedAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(status);
        currentStatus = status;
        OnPropertyChanged(nameof(BlinkGestureText));
        var blink = statusBlinkDetector.Update(
            status,
            observedAt ?? DateTimeOffset.UtcNow);
        IsBlinkGesturePrimed = blink.IsPrimed;
        UpdateProximity();
        NotifyAuxiliaryOverlayState();
        if (allowGesture && blink.Detected)
        {
            await HandleBlinkGestureAsync(status, cancellationToken);
        }
    }

    public void UpdateCargo(CargoSnapshot? cargo)
    {
        if (artifactInventory.Reset(cargo))
        {
            NotifyCurrentObeliskChanged();
            NotifyAuxiliaryOverlayState();
        }
    }

    public async Task LoadProfileAsync(
        string frontierId,
        bool isOdyssey,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
                activeFrontierId,
                frontierId,
                StringComparison.OrdinalIgnoreCase)
            || activeIsOdyssey != isOdyssey)
        {
            liveSiteState = new GuardianLiveSiteState(references);
            NotifyActiveSiteChanged();
            UpdateProximity();
        }

        activeFrontierId = frontierId;
        activeIsOdyssey = isOdyssey;
        ShareArchivePath = null;
        ShareSiteNames = [];
        ShareStatusMessage =
            "Prepare a bundle to find commander survey data not present in the published catalog.";
        prepareShareBundleCommand.RaiseCanExecuteChanged();
        await RefreshAsync(cancellationToken);
    }

    public async Task ApplyJournalEventsAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        string? commanderName,
        bool allowLiveCommands = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        var activeSiteChanged = false;
        var surveyChanged = false;
        var inventoryChanged = false;
        string? saveStatus = null;
        foreach (var journalEvent in journalEvents)
        {
            inventoryChanged |= artifactInventory.Apply(journalEvent);
            var previous = liveSiteState.CurrentSite;
            var recognized = liveSiteState.Apply(journalEvent);
            if (liveSiteState.CurrentSite != previous)
            {
                activeSiteChanged = true;
            }

            if (allowLiveCommands
                && TryGetSendText(journalEvent) is { } command)
            {
                await HandleLiveCommandAsync(command, cancellationToken);
            }

            if (!recognized
                || journalEvent.EventName != "ApproachSettlement"
                || liveSiteState.CurrentSite is null
                || activeFrontierId is null)
            {
                continue;
            }

            try
            {
                var existing = FindSurvey(liveSiteState.CurrentSite);
                var survey = liveSiteState.CreateOrUpdateSurvey(
                    commanderName ?? string.Empty,
                    legacy: !activeIsOdyssey,
                    existing);
                var path = await commanderSurveyStore.SaveAsync(
                    activeFrontierId,
                    activeIsOdyssey,
                    survey,
                    cancellationToken);
                ReplaceSurvey(survey with { Path = path }, existing);
                surveyChanged = true;
                saveStatus = $"Recorded the live Guardian site in "
                    + $"{Path.GetFileName(path)}.";
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException)
            {
                saveStatus = "The live Guardian site was detected but its survey "
                    + "could not be saved: "
                    + exception.Message;
            }
        }

        if (activeSiteChanged)
        {
            statusBlinkDetector.Reset();
            IsBlinkGesturePrimed = false;
            SetTargetObelisk(null);
            NotifyActiveSiteChanged();
            UpdateProximity();
            SetLiveMapModeFromSurvey();
        }

        if (surveyChanged)
        {
            visits = GuardianSiteVisitCatalog.Merge(
                references,
                commanderData,
                publishedSites,
                completionCalculator);
            ApplyFilters();
            SelectActiveReference();
            UpdateProximity();
        }

        if (inventoryChanged)
        {
            NotifyCurrentObeliskChanged();
            NotifyAuxiliaryOverlayState();
        }

        if (saveStatus is not null)
        {
            StatusMessage = saveStatus;
        }
    }

    public void SetProfileError(string error)
    {
        activeFrontierId = null;
        StatusMessage = error;
        toggleCurrentObeliskScannedCommand.RaiseCanExecuteChanged();
    }

    public Task CopySystemNameAsync()
    {
        return CopyAsync(SelectedSite?.Reference.SystemName, "system name");
    }

    public Task CopySystemAddressAsync()
    {
        return CopyAsync(
            SelectedSite?.Reference.SystemAddress.ToString(
                CultureInfo.InvariantCulture),
            "system address");
    }

    public Task CopyGalacticPositionAsync()
    {
        var position = SelectedSite?.Reference.Position;
        return CopyAsync(position?.ToString(), "galactic position");
    }

    public Task CopySurfaceLocationAsync()
    {
        var reference = SelectedSite?.Reference;
        var text = reference?.Latitude is double latitude
            && reference.Longitude is double longitude
                ? FormattableString.Invariant($"{latitude:F6}, {longitude:F6}")
                : null;
        return CopyAsync(text, "surface location");
    }

    public async Task ToggleCurrentObeliskScannedAsync()
    {
        var site = ActiveSite;
        var currentObelisk = CurrentObelisk;
        if (site is null
            || currentObelisk is null
            || activeFrontierId is null)
        {
            StatusMessage = "Approach an active Guardian obelisk before changing its scan state.";
            return;
        }

        var existing = FindSurvey(site);
        if (existing is null)
        {
            StatusMessage = "The current Guardian survey is not available to save.";
            return;
        }

        var scanned = !currentObelisk.Scanned;
        var updatedObelisk = currentObelisk with { Scanned = scanned };
        var updated = existing with
        {
            ActiveObelisks = existing.ActiveObelisks
                .Where(obelisk => !string.Equals(
                    obelisk.Name,
                    currentObelisk.Name,
                    StringComparison.OrdinalIgnoreCase))
                .Append(updatedObelisk)
                .OrderBy(obelisk => obelisk.Name)
                .ToArray(),
        };

        try
        {
            var path = await commanderSurveyStore.SaveAsync(
                activeFrontierId,
                activeIsOdyssey,
                updated);
            updated = updated with { Path = path };
            ReplaceSurvey(updated, existing);
            visits = GuardianSiteVisitCatalog.Merge(
                references,
                commanderData,
                publishedSites,
                completionCalculator);
            ApplyFilters();
            SelectActiveReference();
            UpdateProximity();

            var action = scanned ? "scanned" : "not scanned";
            if (!artifactInventory.HasItems(currentObelisk.ItemCodes))
            {
                StatusMessage = $"Marked {currentObelisk.Name} {action}. Ram Tah "
                    + "progress was not changed because the required artifacts are missing.";
                return;
            }

            if (ramTah is null || !ramTah.IsAnyMissionActive)
            {
                StatusMessage = $"Marked {currentObelisk.Name} {action}. No active "
                    + "Ram Tah mission required a checklist update.";
                return;
            }

            await ramTah.SetLogCompletedAsync(
                GetMission(),
                currentObelisk.LogCode,
                scanned);
            StatusMessage = $"Marked {currentObelisk.Name} {action} and updated "
                + $"Ram Tah log {currentObelisk.LogCode}.";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException)
        {
            StatusMessage = "The current obelisk scan state could not be saved: "
                + exception.Message;
        }
    }

    private async Task HandleLiveCommandAsync(
        string message,
        CancellationToken cancellationToken)
    {
        var text = message.Trim();
        if (text.Length == 0 || ActiveSite is null)
        {
            return;
        }

        if (string.Equals(text, ".aerial", StringComparison.OrdinalIgnoreCase))
        {
            SetLiveMapModeFromSurvey();
            if (LiveMapMode == GuardianLiveMapMode.Map)
            {
                LiveMapMode = GuardianLiveMapMode.Origin;
                StatusMessage = "Guardian origin-alignment mode enabled.";
            }

            return;
        }

        if (string.Equals(text, ".map", StringComparison.OrdinalIgnoreCase))
        {
            SetLiveMapModeFromSurvey(forceMap: true);
            return;
        }

        if (string.Equals(text, "z", StringComparison.OrdinalIgnoreCase))
        {
            EnableAutomaticMapZoom();
            StatusMessage = "Guardian map zoom returned to automatic mode.";
            return;
        }

        if (text.StartsWith('z')
            && double.TryParse(
                text[1..].Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var customZoom))
        {
            activeMapScale = Math.Clamp(customZoom, 0.1, 20);
            automaticMapZoom = false;
            OnPropertyChanged(nameof(IsAutomaticMapZoom));
            OnPropertyChanged(nameof(ActiveMapScale));
            OnPropertyChanged(nameof(ActiveMapScaleText));
            StatusMessage = $"Guardian map zoom set to {activeMapScale:N2}x.";
            return;
        }

        if (text.StartsWith(".note", StringComparison.OrdinalIgnoreCase))
        {
            var note = text[".note".Length..].Trim();
            if (note.Length == 0)
            {
                StatusMessage = "Type .note followed by text to append a site note.";
                return;
            }

            await SaveActiveSurveyMutationAsync(
                survey => survey with
                {
                    Notes = survey.Notes + $"\r\n{note}\r\n",
                },
                "Appended the Guardian site note.",
                cancellationToken);
            return;
        }

        var parsedType = LiveMapMode == GuardianLiveMapMode.SiteType
            ? ParseSiteType(text)
            : null;
        if (parsedType is null
            && text.StartsWith(".site", StringComparison.OrdinalIgnoreCase))
        {
            parsedType = ParseSiteType(text[".site".Length..].Trim());
        }

        if (parsedType is not null)
        {
            var siteType = parsedType.SiteType;
            if (await SaveActiveSurveyMutationAsync(
                survey => survey with
                {
                    SiteType = siteType,
                    Survey = CopySurveyData(
                        survey.Survey,
                        siteType: siteType),
                },
                $"Guardian site type set to {siteType}.",
                cancellationToken))
            {
                SetLiveMapModeFromSurvey();
            }

            return;
        }

        var changeHeading = false;
        var newHeading = -1;
        if (LiveMapMode == GuardianLiveMapMode.Heading)
        {
            changeHeading = int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out newHeading);
        }

        if (string.Equals(text, ".heading", StringComparison.OrdinalIgnoreCase))
        {
            if (LiveMapMode == GuardianLiveMapMode.Heading)
            {
                newHeading = currentStatus?.NormalizedHeading ?? -1;
                changeHeading = newHeading >= 0;
            }
            else
            {
                LiveMapMode = GuardianLiveMapMode.Heading;
                StatusMessage = "Face the Guardian alignment feature and type .heading again.";
                return;
            }
        }
        else if (text.StartsWith(".heading", StringComparison.OrdinalIgnoreCase))
        {
            changeHeading = int.TryParse(
                text[".heading".Length..].Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out newHeading);
        }
        else if (string.Equals(text, ".alphaflip", StringComparison.OrdinalIgnoreCase)
            && FindSurvey(ActiveSite) is { } alphaSurvey)
        {
            newHeading = alphaSurvey.Survey.SiteHeading + 180;
            changeHeading = true;
        }

        if (changeHeading)
        {
            var normalizedHeading = NormalizeHeading(newHeading);
            if (await SaveActiveSurveyMutationAsync(
                survey => survey with
                {
                    Survey = CopySurveyData(
                        survey.Survey,
                        siteHeading: normalizedHeading),
                },
                $"Guardian site heading set to {normalizedHeading}°.",
                cancellationToken))
            {
                LiveMapMode = normalizedHeading == 0
                    ? GuardianLiveMapMode.Heading
                    : GuardianLiveMapMode.Map;
            }

            return;
        }

        if (string.Equals(text, ".tower", StringComparison.OrdinalIgnoreCase)
            && ActiveSite.Kind == GuardianSiteKind.Ruins
            && currentStatus is not null)
        {
            var towerHeading = currentStatus.NormalizedHeading;
            await SaveActiveSurveyMutationAsync(
                survey => survey with
                {
                    Survey = CopySurveyData(
                        survey.Survey,
                        relicTowerHeading: towerHeading),
                },
                $"Guardian relic-tower heading set to {towerHeading}°.",
                cancellationToken);
            return;
        }

        if (text.StartsWith(".tower", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(
                text[".tower".Length..].Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedTowerHeading))
        {
            await SetNearestRelicHeadingAsync(
                NormalizeHeading(parsedTowerHeading),
                cancellationToken);
            return;
        }

        if (string.Equals(text, ".empty", StringComparison.OrdinalIgnoreCase))
        {
            await SetNearestPointEmptyAsync(cancellationToken);
            return;
        }

        if (string.Equals(text, ".os", StringComparison.OrdinalIgnoreCase))
        {
            await ToggleCurrentObeliskScannedAsync();
            return;
        }

        if (text.StartsWith(".to", StringComparison.OrdinalIgnoreCase))
        {
            SetTargetObelisk(text[".to".Length..].Trim());
            return;
        }

        if (text.StartsWith(".add", StringComparison.OrdinalIgnoreCase))
        {
            await AddRawPointAsync(
                text[".add".Length..].Trim(),
                cancellationToken);
            return;
        }

        if (text.StartsWith(".remove", StringComparison.OrdinalIgnoreCase))
        {
            await RemoveNearestRawPointAsync(cancellationToken);
        }
    }

    private async Task HandleBlinkGestureAsync(
        EliteStatus status,
        CancellationToken cancellationToken)
    {
        if (ActiveSite is null)
        {
            return;
        }

        if (status.OnFoot
            && string.Equals(
                status.SelectedWeapon,
                "$humanoid_companalyser_name;",
                StringComparison.Ordinal))
        {
            await ApplyOnFootRelicGestureAsync(status, cancellationToken);
            return;
        }

        var inVehicle = status.InSrv
            || status.InFighter
            || status.InMainShip && !status.Docked;
        if (!inVehicle)
        {
            return;
        }

        if (LiveMapMode == GuardianLiveMapMode.SiteType
            && ActiveSite.Kind == GuardianSiteKind.Ruins)
        {
            var type = PositiveModulo(status.FireGroup, 3) switch
            {
                0 => "Alpha",
                1 => "Beta",
                _ => "Gamma",
            };
            if (await SaveActiveSurveyMutationAsync(
                survey => survey with
                {
                    SiteType = type,
                    Survey = CopySurveyData(survey.Survey, siteType: type),
                },
                $"Guardian blink gesture set the site type to {type}.",
                cancellationToken))
            {
                SetLiveMapModeFromSurvey();
            }

            return;
        }

        if (LiveMapMode == GuardianLiveMapMode.Heading)
        {
            var heading = status.NormalizedHeading;
            if (await SaveActiveSurveyMutationAsync(
                survey => survey with
                {
                    Survey = CopySurveyData(
                        survey.Survey,
                        siteHeading: heading),
                },
                $"Guardian blink gesture set the site heading to {heading}°.",
                cancellationToken))
            {
                LiveMapMode = heading == 0
                    ? GuardianLiveMapMode.Heading
                    : GuardianLiveMapMode.Map;
            }

            return;
        }

        if (LiveMapMode != GuardianLiveMapMode.Map)
        {
            return;
        }

        if (CurrentObelisk is not null)
        {
            await ToggleCurrentObeliskScannedAsync();
            return;
        }

        var pointStatus = PositiveModulo(status.FireGroup, 3) switch
        {
            0 => GuardianPoiStatus.Present,
            1 => GuardianPoiStatus.Absent,
            _ => GuardianPoiStatus.Empty,
        };
        await SetNearestPointStatusFromGestureAsync(
            pointStatus,
            cancellationToken);
    }

    private async Task ApplyOnFootRelicGestureAsync(
        EliteStatus status,
        CancellationToken cancellationToken)
    {
        var nearestRelic = Proximity?.NearestPoint?.Point is
        { Type: GuardianPoiType.Relic } point
                ? point
                : null;
        if (ActiveSite?.Kind != GuardianSiteKind.Ruins
            && nearestRelic is null)
        {
            return;
        }

        var heading = status.NormalizedHeading;
        await SaveActiveSurveyMutationAsync(
            survey =>
            {
                var data = survey.Survey;
                var statuses = new Dictionary<string, GuardianPoiStatus>(
                    data.PoiStatuses,
                    StringComparer.Ordinal);
                var headings = new Dictionary<string, int>(
                    data.RelicHeadings,
                    StringComparer.Ordinal);
                var rawPoints = data.RawPointsOfInterest?.ToArray();
                if (nearestRelic is not null)
                {
                    statuses[nearestRelic.Name] = GuardianPoiStatus.Present;
                    var rawIndex = Array.FindIndex(
                        rawPoints ?? [],
                        candidate => string.Equals(
                            candidate.Name,
                            nearestRelic.Name,
                            StringComparison.Ordinal));
                    if (rawIndex >= 0 && rawPoints is not null)
                    {
                        rawPoints[rawIndex] = rawPoints[rawIndex] with
                        {
                            Rotation = heading,
                        };
                    }
                    else
                    {
                        headings[nearestRelic.Name] = heading;
                    }
                }

                return survey with
                {
                    Survey = CopySurveyData(
                        data,
                        relicTowerHeading:
                            ActiveSite?.Kind == GuardianSiteKind.Ruins
                                ? heading
                                : null,
                        poiStatuses: statuses,
                        relicHeadings: headings,
                        rawPoints: rawPoints,
                        replaceRawPoints: rawPoints is not null),
                };
            },
            nearestRelic is null
                ? $"Guardian blink gesture set the ruins relic-tower heading to {heading}°."
                : $"Guardian blink gesture set relic tower {nearestRelic.Name} to {heading}°.",
            cancellationToken);
    }

    private async Task SetNearestPointStatusFromGestureAsync(
        GuardianPoiStatus pointStatus,
        CancellationToken cancellationToken)
    {
        if (Proximity?.NearestPoint?.Point is not { } point
            || point.Type is GuardianPoiType.Obelisk
                or GuardianPoiType.BrokenObelisk
                or GuardianPoiType.EmptyPuddle
            || ActiveSite is not { } site
            || FindSurvey(site) is not { } existing
            || FindTemplate(existing.SiteType) is not { } template
            || !template.PointsOfInterest.Any(candidate => string.Equals(
                candidate.Name,
                point.Name,
                StringComparison.Ordinal)))
        {
            return;
        }

        if (pointStatus == GuardianPoiStatus.Empty
            && point.Type is not GuardianPoiType.Orb
                and not GuardianPoiType.Casket
                and not GuardianPoiType.Tablet
                and not GuardianPoiType.Totem
                and not GuardianPoiType.Urn)
        {
            StatusMessage = $"Guardian point {point.Name} cannot be marked empty.";
            return;
        }

        await SaveActiveSurveyMutationAsync(
            survey =>
            {
                var statuses = new Dictionary<string, GuardianPoiStatus>(
                    survey.Survey.PoiStatuses,
                    StringComparer.Ordinal)
                {
                    [point.Name] = pointStatus,
                };
                return survey with
                {
                    Survey = CopySurveyData(
                        survey.Survey,
                        poiStatuses: statuses),
                };
            },
            $"Guardian blink gesture marked {point.Name} {pointStatus.ToString().ToLowerInvariant()}.",
            cancellationToken);
    }

    private static int PositiveModulo(int value, int divisor)
    {
        return ((value % divisor) + divisor) % divisor;
    }

    private async Task<bool> SaveActiveSurveyMutationAsync(
        Func<GuardianCommanderSiteSurvey, GuardianCommanderSiteSurvey> mutate,
        string successMessage,
        CancellationToken cancellationToken)
    {
        if (ActiveSite is not { } site
            || activeFrontierId is null
            || FindSurvey(site) is not { } existing)
        {
            StatusMessage = "An active commander Guardian survey is required for that command.";
            return false;
        }

        try
        {
            var updated = mutate(existing);
            var path = await commanderSurveyStore.SaveAsync(
                activeFrontierId,
                activeIsOdyssey,
                updated,
                cancellationToken);
            var saved = updated with { Path = path };
            await OnSurveySavedAsync(existing, saved);
            UpdateSurveyEditor();
            OnPropertyChanged(nameof(ActiveSiteDescription));
            NotifyGuardianGuidanceChanged();
            StatusMessage = successMessage;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException)
        {
            StatusMessage = "The Guardian command could not be saved: "
                + exception.Message;
            return false;
        }
    }

    private async Task SetNearestRelicHeadingAsync(
        int heading,
        CancellationToken cancellationToken)
    {
        if (Proximity?.NearestPoint?.Point is not { Type: GuardianPoiType.Relic } point)
        {
            StatusMessage = "Approach a relic tower before setting its heading.";
            return;
        }

        await SaveActiveSurveyMutationAsync(
            survey =>
            {
                var data = survey.Survey;
                var statuses = new Dictionary<string, GuardianPoiStatus>(
                    data.PoiStatuses,
                    StringComparer.Ordinal)
                {
                    [point.Name] = GuardianPoiStatus.Present,
                };
                var headings = new Dictionary<string, int>(
                    data.RelicHeadings,
                    StringComparer.Ordinal);
                var rawPoints = data.RawPointsOfInterest?.ToArray();
                var rawIndex = Array.FindIndex(
                    rawPoints ?? [],
                    candidate => string.Equals(
                        candidate.Name,
                        point.Name,
                        StringComparison.Ordinal));
                if (rawIndex >= 0 && rawPoints is not null)
                {
                    rawPoints[rawIndex] = rawPoints[rawIndex] with
                    {
                        Rotation = heading,
                    };
                }
                else
                {
                    headings[point.Name] = heading;
                }

                return survey with
                {
                    Survey = CopySurveyData(
                        data,
                        poiStatuses: statuses,
                        relicHeadings: headings,
                        rawPoints: rawPoints,
                        replaceRawPoints: rawPoints is not null),
                };
            },
            $"Relic tower {point.Name} heading set to {heading}°.",
            cancellationToken);
    }

    private async Task SetNearestPointEmptyAsync(
        CancellationToken cancellationToken)
    {
        if (Proximity?.NearestPoint?.Point is not { } point)
        {
            StatusMessage = "Approach a Guardian point before marking it empty.";
            return;
        }

        await SaveActiveSurveyMutationAsync(
            survey =>
            {
                var statuses = new Dictionary<string, GuardianPoiStatus>(
                    survey.Survey.PoiStatuses,
                    StringComparer.Ordinal)
                {
                    [point.Name] = GuardianPoiStatus.Empty,
                };
                return survey with
                {
                    Survey = CopySurveyData(
                        survey.Survey,
                        poiStatuses: statuses),
                };
            },
            $"Marked Guardian point {point.Name} empty.",
            cancellationToken);
    }

    private async Task AddRawPointAsync(
        string typeName,
        CancellationToken cancellationToken)
    {
        if (!TryParseGuardianPointType(typeName, out var type)
            || type == GuardianPoiType.EmptyPuddle
            || Proximity is not { } measurement)
        {
            StatusMessage = "Type .add followed by a valid Guardian point type while at the point.";
            return;
        }

        var angle = GetSurveyPointAngle(measurement.MapX, measurement.MapY);
        var distance = measurement.DistanceFromSite;
        var rotation = type == GuardianPoiType.Relic
            ? -1
            : ActiveMapRelativeHeading;
        await SaveActiveSurveyMutationAsync(
            survey =>
            {
                var data = survey.Survey;
                var template = FindTemplate(survey.SiteType);
                var existingPoints = (template?.PointsOfInterest ?? [])
                    .Concat(data.RawPointsOfInterest ?? [])
                    .ToArray();
                if (existingPoints.Any(point => IsRawPointTooClose(
                        point,
                        type,
                        angle,
                        distance)))
                {
                    throw new InvalidDataException(
                        "The new point is too close to an existing Guardian point.");
                }

                var rawPoints = data.RawPointsOfInterest?.ToList() ?? [];
                var name = GetNextRawPointName(existingPoints.Select(point => point.Name));
                rawPoints.Add(new GuardianPointOfInterest(
                    name,
                    type,
                    angle,
                    distance,
                    rotation));
                return survey with
                {
                    Survey = CopySurveyData(
                        data,
                        rawPoints: rawPoints,
                        replaceRawPoints: true),
                };
            },
            $"Added a local raw {type} point to the Guardian survey.",
            cancellationToken);
    }

    private async Task RemoveNearestRawPointAsync(
        CancellationToken cancellationToken)
    {
        if (Proximity?.NearestPoint?.Point is not { } nearest)
        {
            StatusMessage = "Approach a local raw Guardian point before removing it.";
            return;
        }

        await SaveActiveSurveyMutationAsync(
            survey =>
            {
                var data = survey.Survey;
                var rawPoints = (data.RawPointsOfInterest ?? [])
                    .Where(point => !string.Equals(
                        point.Name,
                        nearest.Name,
                        StringComparison.Ordinal))
                    .ToArray();
                if (rawPoints.Length == (data.RawPointsOfInterest?.Count ?? 0))
                {
                    throw new InvalidDataException(
                        "The nearest Guardian point is not a local raw point.");
                }

                var statuses = new Dictionary<string, GuardianPoiStatus>(
                    data.PoiStatuses,
                    StringComparer.Ordinal);
                statuses.Remove(nearest.Name);
                var headings = new Dictionary<string, int>(
                    data.RelicHeadings,
                    StringComparer.Ordinal);
                headings.Remove(nearest.Name);
                return survey with
                {
                    Survey = CopySurveyData(
                        data,
                        poiStatuses: statuses,
                        relicHeadings: headings,
                        rawPoints: rawPoints.Length == 0 ? null : rawPoints,
                        replaceRawPoints: true),
                };
            },
            $"Removed local raw Guardian point {nearest.Name}.",
            cancellationToken);
    }

    private async Task RefreshAsync()
    {
        await RefreshAsync(CancellationToken.None);
    }

    public async Task PrepareShareBundleAsync()
    {
        if (activeFrontierId is null)
        {
            ShareStatusMessage = "A commander profile is required before sharing survey data.";
            return;
        }

        isPreparingShareBundle = true;
        prepareShareBundleCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(ShareButtonText));
        try
        {
            var bundle = await surveyShareService.PrepareAsync(
                activeFrontierId,
                activeIsOdyssey,
                commanderData);
            ShareArchivePath = bundle.ArchivePath;
            ShareSiteNames = bundle.Sites
                .Select(site => site.DisplayName + " — "
                    + string.Join(", ", site.Reasons))
                .ToArray();
            ShareStatusMessage = bundle.Sites.Count == 0
                ? "No unpublished Guardian survey data was found. An empty bundle was prepared for parity with the legacy workflow."
                : $"Prepared {bundle.Sites.Count:N0} Guardian survey "
                    + (bundle.Sites.Count == 1 ? "file." : "files.");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException)
        {
            ShareArchivePath = null;
            ShareSiteNames = [];
            ShareStatusMessage = "The Guardian survey bundle could not be prepared: "
                + exception.Message;
        }
        finally
        {
            isPreparingShareBundle = false;
            prepareShareBundleCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(ShareButtonText));
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (activeFrontierId is null)
        {
            StatusMessage = "Reference data is ready; no commander profile is active.";
            return;
        }

        IsBusy = true;
        try
        {
            commanderData = await commanderDataReader.ReadAsync(
                activeFrontierId,
                activeIsOdyssey,
                cancellationToken);
            visits = GuardianSiteVisitCatalog.Merge(
                references,
                commanderData,
                publishedSites,
                completionCalculator);
            ApplyFilters();
            UpdateProximity();
            StatusMessage = commanderData.Errors.Count == 0
                ? $"Loaded {commanderData.Surveys.Count} site survey file(s) and "
                    + $"{commanderData.Beacons.Count} beacon file(s)."
                : $"Loaded commander Guardian data with "
                    + $"{commanderData.Errors.Count} file error(s): "
                    + string.Join(" ", commanderData.Errors);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            StatusMessage = "Guardian commander data could not be loaded: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CopyAsync(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text) || clipboardWriter is null)
        {
            StatusMessage = $"The {label} is not available to copy.";
            return;
        }

        try
        {
            await clipboardWriter(text);
            StatusMessage = $"Copied {label}: {text}";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or IOException
                or UnauthorizedAccessException)
        {
            StatusMessage = $"The {label} could not be copied: {exception.Message}";
        }
    }

    private GuardianCommanderSiteSurvey? FindSurvey(
        GuardianLiveSiteSnapshot site)
    {
        return commanderData.Surveys.FirstOrDefault(survey =>
            survey.SystemAddress == site.SystemAddress
            && survey.Index == site.Index
            && IsSameBody(site, survey)
            && IsRuins(survey) == (site.Kind == GuardianSiteKind.Ruins));
    }

    private void ReplaceSurvey(
        GuardianCommanderSiteSurvey survey,
        GuardianCommanderSiteSurvey? replaced)
    {
        var surveys = commanderData.Surveys
            .Where(candidate => candidate != replaced)
            .Append(survey)
            .OrderBy(candidate => candidate.SystemName)
            .ThenBy(candidate => candidate.BodyName)
            .ThenBy(candidate => candidate.Index)
            .ToArray();
        commanderData = new GuardianCommanderDataReadResult(
            surveys,
            commanderData.Beacons,
            commanderData.Errors);
    }

    private void SelectActiveReference()
    {
        if (ActiveSite?.Reference is not { } reference)
        {
            return;
        }

        SelectedSite = Rows.FirstOrDefault(row => row.Reference == reference)
            ?? SelectedSite;
    }

    private static bool IsSameBody(
        GuardianLiveSiteSnapshot site,
        GuardianCommanderSiteSurvey survey)
    {
        return site.BodyId >= 0 && survey.BodyId >= 0
            ? site.BodyId == survey.BodyId
            : string.Equals(
                site.BodyName,
                survey.BodyName,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRuins(GuardianCommanderSiteSurvey survey)
    {
        return survey.Name.StartsWith(
                "$Ancient:#index=",
                StringComparison.Ordinal)
            || survey.Path.Contains("-ruins-", StringComparison.OrdinalIgnoreCase);
    }

    private RamTahMission GetMission()
    {
        return ActiveSite?.Kind == GuardianSiteKind.Ruins
            ? RamTahMission.AncientRuins
            : RamTahMission.GuardianLogs;
    }

    private static string GetLogDisplayName(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "Unknown";
        }

        var category = code[0] switch
        {
            'B' => "Biology",
            'C' => "Culture",
            'H' => "History",
            'L' => "Language",
            'T' => "Technology",
            '#' => "Guardian",
            _ => "Log",
        };
        var number = code[0] == '#' ? code : $"#{code[1..]}";
        return $"{category} {number}";
    }

    private IReadOnlyList<GuardianRamTahLogViewModel> BuildCurrentRamTahLogs()
    {
        var site = ActiveSite;
        var reference = site?.Reference;
        if (site is null
            || reference is null
            || ramTah is null
            || !IsActiveSiteRelevantToRamTahMission())
        {
            return [];
        }

        var mission = GetMission();
        var survey = FindSurvey(site);
        return GetMergedActiveObelisks(reference, survey)
            .Where(obelisk => !string.IsNullOrWhiteSpace(obelisk.LogCode)
                && !ramTah.IsLogCompleted(mission, obelisk.LogCode))
            .GroupBy(
                obelisk => obelisk.LogCode,
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var obelisks = group
                    .OrderBy(obelisk => obelisk.Name)
                    .ToArray();
                var requirements = artifactInventory.GetRequirements(
                    obelisks[0].ItemCodes);
                return new GuardianRamTahLogViewModel(
                    group.Key,
                    GetLogDisplayName(group.Key),
                    requirements.Count == 0
                        ? "No artifact requirement recorded"
                        : string.Join(
                            " + ",
                            requirements.Select(requirement =>
                                $"{requirement.DisplayName} "
                                + $"{requirement.Available}/"
                                + $"{requirement.Required}")),
                    requirements.All(requirement => requirement.IsMet),
                    string.Join(", ", obelisks.Select(obelisk => obelisk.Name)));
            })
            .OrderBy(log => log.LogCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private bool IsActiveSiteRelevantToRamTahMission()
    {
        return ActiveSite?.Kind switch
        {
            GuardianSiteKind.Ruins =>
                ramTah?.IsAncientRuinsMissionActive == true,
            GuardianSiteKind.Structure =>
                ramTah?.IsGuardianLogsMissionActive == true,
            _ => false,
        };
    }

    private bool IsCurrentDestination(GuardianSiteReference reference)
    {
        var destination = currentStatus?.Destination;
        return destination is not null
            && destination.System == reference.SystemAddress
            && destination.Body == reference.BodyId;
    }

    private static bool IsGuardianSummaryStatusEligible(EliteStatus? status)
    {
        if (status is null)
        {
            return false;
        }

        return status.GuiFocus is GuiFocus.ExternalPanel
                or GuiFocus.Orrery
                or GuiFocus.SystemMap
            || status.GuiFocus == GuiFocus.NoFocus
                && status.Flags.HasFlag(StatusFlags.Supercruise);
    }

    private static bool IsLiveSiteStatusEligible(EliteStatus? status)
    {
        if (status is null
            || !status.HasLatitudeLongitude
            || status.FsdChargingJump)
        {
            return false;
        }

        if (status.GlideMode)
        {
            return true;
        }

        if (status.GuiFocus is GuiFocus.CommsPanel or GuiFocus.InternalPanel)
        {
            return true;
        }

        if (status.GuiFocus != GuiFocus.NoFocus)
        {
            return false;
        }

        var flying = status.InMainShip
            && !status.Docked
            && !status.Landed
            && !status.Flags.HasFlag(StatusFlags.Supercruise);
        return status.InSrv
            || status.OnFoot
            || status.Landed
            || status.InFighter
            || flying;
    }

    private static bool IsRamTahStatusEligible(EliteStatus? status)
    {
        if (status is null)
        {
            return false;
        }

        if (status.GuiFocus is GuiFocus.CommsPanel or GuiFocus.InternalPanel)
        {
            return true;
        }

        if (status.GuiFocus != GuiFocus.NoFocus)
        {
            return false;
        }

        var flying = status.InMainShip
            && !status.Docked
            && !status.Landed
            && !status.Flags.HasFlag(StatusFlags.Supercruise)
            && !status.GlideMode;
        return status.InSrv
            || status.OnFoot
            || status.Landed
            || flying
            || status.InFighter;
    }

    private void SaveOverlayPreferences()
    {
        if (overlaySettingsStore is null)
        {
            return;
        }

        try
        {
            overlaySettingsStore.Save(new GuardianOverlayPreferences(
                EnableGuardianSites,
                AutoShowGuardianSummary,
                AutoShowRamTah,
                SuppressForActiveBuildProjects,
                AutoZoomNearObelisks,
                AutoZoomInSrvTurret,
                ShowComponentMaterials,
                SelectedOverlaySize.Index,
                DisableRuinsMeasurementGrid: !ShowRuinsMeasurementGrid,
                DisableAerialAlignmentGrid: !ShowAerialAlignmentGrid,
                ShowMapNotes,
                ShowMapLegend));
            OverlaySettingsStatus = string.Empty;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            OverlaySettingsStatus =
                $"Guardian overlay settings could not be saved: {exception.Message}";
        }
    }

    private void NotifyAuxiliaryOverlayState()
    {
        OnPropertyChanged(nameof(ShouldShowLiveSiteOverlay));
        OnPropertyChanged(nameof(CurrentSystemSites));
        OnPropertyChanged(nameof(HasCurrentSystemSites));
        OnPropertyChanged(nameof(CurrentSystemGuardianTitle));
        OnPropertyChanged(nameof(CurrentRamTahLogs));
        OnPropertyChanged(nameof(HasCurrentRamTahLogs));
        OnPropertyChanged(nameof(CurrentRamTahTitle));
        OnPropertyChanged(nameof(ShouldShowGuardianSystemSummary));
        OnPropertyChanged(nameof(ShouldShowRamTahOverlay));
    }

    private void NotifyActiveSiteChanged()
    {
        OnPropertyChanged(nameof(ActiveSite));
        OnPropertyChanged(nameof(HasActiveSite));
        OnPropertyChanged(nameof(ActiveSiteTitle));
        OnPropertyChanged(nameof(ActiveSiteDescription));
        OnPropertyChanged(nameof(ActiveSiteReference));
        OnPropertyChanged(nameof(ActiveSiteLocation));
        OnPropertyChanged(nameof(ActiveSiteVisit));
        OnPropertyChanged(nameof(ShouldShowLiveSiteOverlay));
        NotifyAuxiliaryOverlayState();
    }

    private void NotifyCurrentObeliskChanged()
    {
        RefreshAutomaticMapScale();
        OnPropertyChanged(nameof(Proximity));
        OnPropertyChanged(nameof(CurrentObelisk));
        OnPropertyChanged(nameof(HasCurrentObelisk));
        OnPropertyChanged(nameof(SiteDistanceText));
        OnPropertyChanged(nameof(NearbyPointText));
        OnPropertyChanged(nameof(CurrentObeliskTitle));
        OnPropertyChanged(nameof(CurrentObeliskLogText));
        OnPropertyChanged(nameof(CurrentObeliskRequirementsText));
        OnPropertyChanged(nameof(HasCurrentObeliskArtifacts));
        OnPropertyChanged(nameof(CurrentObeliskArtifactStatus));
        OnPropertyChanged(nameof(CurrentObeliskMissionStatus));
        OnPropertyChanged(nameof(ToggleCurrentObeliskScannedText));
        OnPropertyChanged(nameof(CurrentObeliskScanStatus));
        OnPropertyChanged(nameof(ActiveMapProjection));
        OnPropertyChanged(nameof(ActiveMapTitle));
        OnPropertyChanged(nameof(ActiveMapSummary));
        OnPropertyChanged(nameof(ActiveMapScale));
        OnPropertyChanged(nameof(ActiveMapScaleText));
        OnPropertyChanged(nameof(ActiveMapRelativeHeading));
        OnPropertyChanged(nameof(TargetObeliskText));
        OnPropertyChanged(nameof(ShouldShowLiveSiteOverlay));
        NotifyGuardianGuidanceChanged();
        OnPropertyChanged(nameof(CurrentRamTahLogs));
        OnPropertyChanged(nameof(HasCurrentRamTahLogs));
        OnPropertyChanged(nameof(CurrentRamTahTitle));
        toggleCurrentObeliskScannedCommand.RaiseCanExecuteChanged();
    }

    private void RefreshAutomaticMapScale()
    {
        if (!automaticMapZoom)
        {
            return;
        }

        activeMapScale = GetAutomaticMapScale();
        OnPropertyChanged(nameof(ActiveMapScale));
        OnPropertyChanged(nameof(ActiveMapScaleText));
    }

    private double GetAutomaticMapScale()
    {
        return CalculateAutomaticMapScale(
            ActiveSite?.Kind,
            Proximity?.DistanceFromSite,
            currentStatus?.OnFoot == true,
            currentStatus?.UsingSrvTurret == true,
            currentStatus is { } status && (status.InSrv || status.OnFoot),
            GetNearestObeliskDistance(),
            AutoZoomNearObelisks,
            AutoZoomInSrvTurret);
    }

    internal static double CalculateAutomaticMapScale(
        GuardianSiteKind? siteKind,
        double? distanceFromSite,
        bool onFoot,
        bool usingSrvTurret,
        bool mobileOnSurface,
        double nearestObeliskDistance,
        bool autoZoomNearObelisks,
        bool autoZoomInSrvTurret)
    {
        if (autoZoomInSrvTurret && usingSrvTurret)
        {
            return 3;
        }

        if (autoZoomNearObelisks
            && mobileOnSurface
            && nearestObeliskDistance < 30)
        {
            return 3;
        }

        if (onFoot)
        {
            return 2;
        }

        return siteKind == GuardianSiteKind.Ruins
            ? distanceFromSite > 1_000
                ? 0.2
                : distanceFromSite > 800
                    ? 0.5
                    : 0.65
            : distanceFromSite > 800
                ? 0.2
                : distanceFromSite > 500
                    ? 0.5
                    : 1.5;
    }

    private double GetNearestObeliskDistance()
    {
        if (Proximity is not { } current
            || ActiveMapProjection is not { } projection)
        {
            return double.PositiveInfinity;
        }

        return projection.Points
            .Where(point => point.Type is GuardianPoiType.Obelisk
                or GuardianPoiType.BrokenObelisk)
            .Select(point => Math.Sqrt(
                Math.Pow(point.X - current.MapX, 2)
                + Math.Pow(point.Y - current.MapY, 2)))
            .DefaultIfEmpty(double.PositiveInfinity)
            .Min();
    }

    private GuardianSiteTemplate? ParseSiteType(string value)
    {
        var normalized = value.Trim() switch
        {
            "a" or "A" => "Alpha",
            "b" or "B" => "Beta",
            "g" or "G" => "Gamma",
            var candidate => candidate,
        };
        return templates.Templates.FirstOrDefault(template => string.Equals(
            template.SiteType,
            normalized,
            StringComparison.OrdinalIgnoreCase));
    }

    private string? GetActiveSiteType()
    {
        return ActiveSite is { } site
            ? FindSurvey(site)?.SiteType ?? site.SiteType
            : null;
    }

    private static GuardianAlignmentMode? ParseAlignmentMode(string? siteType)
    {
        return Enum.TryParse<GuardianAlignmentMode>(
            siteType,
            ignoreCase: true,
            out var mode)
                ? mode
                : null;
    }

    internal static string GetGuardianBlueprintText(string? siteType)
    {
        var blueprint = siteType?.ToLowerInvariant() switch
        {
            "robolobster" or "squid" or "stickyhand" =>
                "Fighter blueprint",
            "turtle" => "Module blueprint",
            "bear" or "hammerbot" or "bowl" => "Weapon blueprint",
            _ => null,
        };
        return blueprint is null
            ? $"{siteType ?? "Unknown"} layout - no blueprint category recorded"
            : $"{siteType} layout - {blueprint}";
    }

    private void NotifyGuardianGuidanceChanged()
    {
        OnPropertyChanged(nameof(AlignmentMode));
        OnPropertyChanged(nameof(AlignmentTargetAltitude));
        OnPropertyChanged(nameof(AlignmentOpacity));
        OnPropertyChanged(nameof(IsAlignmentVisible));
        OnPropertyChanged(nameof(AlignmentHeading));
        OnPropertyChanged(nameof(AlignmentStatusText));
        OnPropertyChanged(nameof(IsGlideApproach));
        OnPropertyChanged(nameof(GlideApproachTitle));
        OnPropertyChanged(nameof(GlideApproachText));
        OnPropertyChanged(nameof(GlideApproachFooter));
    }

    private void SetLiveMapModeFromSurvey(bool forceMap = false)
    {
        var survey = ActiveSite is { } site ? FindSurvey(site) : null;
        var hasType = survey is not null
            && !string.Equals(
                survey.SiteType,
                "Unknown",
                StringComparison.OrdinalIgnoreCase)
            && FindTemplate(survey.SiteType) is not null;
        LiveMapMode = !hasType
            ? GuardianLiveMapMode.SiteType
            : survey!.Survey.SiteHeading is <= 0 or > 359
                ? GuardianLiveMapMode.Heading
                : GuardianLiveMapMode.Map;
        StatusMessage = LiveMapMode switch
        {
            GuardianLiveMapMode.SiteType =>
                "Identify the Guardian site type before opening its map.",
            GuardianLiveMapMode.Heading =>
                "Set the Guardian site heading before opening its map.",
            _ when forceMap => "Guardian map mode enabled.",
            _ => StatusMessage,
        };
    }

    private void SetTargetObelisk(string? requestedName)
    {
        var name = requestedName?.Trim().ToUpperInvariant();
        var survey = ActiveSite is { } site ? FindSurvey(site) : null;
        var target = GetMergedActiveObelisks(ActiveSite?.Reference, survey)
            .FirstOrDefault(obelisk => string.Equals(
                obelisk.Name,
                name,
                StringComparison.OrdinalIgnoreCase));
        targetObeliskName = target?.Name;
        OnPropertyChanged(nameof(TargetObeliskName));
        OnPropertyChanged(nameof(HasTargetObelisk));
        OnPropertyChanged(nameof(TargetObeliskText));
        StatusMessage = target is null
            ? "Cleared the Guardian obelisk target."
            : $"Targeting Guardian obelisk {target.Name}.";
    }

    private double GetTargetObeliskDistance()
    {
        if (TargetObeliskName is null
            || Proximity is not { } current
            || ActiveMapProjection?.Points.FirstOrDefault(point =>
                string.Equals(
                    point.Name,
                    TargetObeliskName,
                    StringComparison.OrdinalIgnoreCase)) is not { } target)
        {
            return 0;
        }

        return Math.Sqrt(
            Math.Pow(target.X - current.MapX, 2)
            + Math.Pow(target.Y - current.MapY, 2));
    }

    private static string? TryGetSendText(JournalEventEnvelope journalEvent)
    {
        return journalEvent.EventName == "SendText"
            && journalEvent.Payload.TryGetProperty("Message", out var message)
            && message.ValueKind == System.Text.Json.JsonValueKind.String
                ? message.GetString()
                : null;
    }

    private static int NormalizeHeading(int heading)
    {
        return ((heading % 360) + 360) % 360;
    }

    private static bool TryParseGuardianPointType(
        string value,
        out GuardianPoiType type)
    {
        if (value.Equals("brokeObelisk", StringComparison.OrdinalIgnoreCase))
        {
            type = GuardianPoiType.BrokenObelisk;
            return true;
        }

        if (value.Equals("destructablePanel", StringComparison.OrdinalIgnoreCase))
        {
            type = GuardianPoiType.DestructiblePanel;
            return true;
        }

        return Enum.TryParse(value, ignoreCase: true, out type);
    }

    private static double GetSurveyPointAngle(double mapX, double mapY)
    {
        return SurfaceNavigation.NormalizeDegrees(
            Math.Atan2(-mapX, mapY) * 180 / Math.PI);
    }

    private static bool IsRawPointTooClose(
        GuardianPointOfInterest point,
        GuardianPoiType type,
        double angle,
        double distance)
    {
        var angleDelta = Math.Abs(point.Angle - angle);
        angleDelta = Math.Min(angleDelta, 360 - angleDelta);
        var distanceDelta = Math.Abs(point.Distance - distance);
        return point.Type == type && angleDelta <= 3 && distanceDelta <= 10
            || angleDelta <= 1 && distanceDelta <= 3;
    }

    private static string GetNextRawPointName(IEnumerable<string> names)
    {
        var used = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; ; index++)
        {
            var candidate = $"x{index}";
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static GuardianSurveyData CopySurveyData(
        GuardianSurveyData source,
        string? siteType = null,
        int? siteHeading = null,
        int? relicTowerHeading = null,
        IReadOnlyDictionary<string, GuardianPoiStatus>? poiStatuses = null,
        IReadOnlyDictionary<string, int>? relicHeadings = null,
        IReadOnlyList<GuardianPointOfInterest>? rawPoints = null,
        bool replaceRawPoints = false)
    {
        return new GuardianSurveyData
        {
            SiteType = siteType ?? source.SiteType,
            SiteHeading = siteHeading ?? source.SiteHeading,
            RelicTowerHeading = relicTowerHeading
                ?? source.RelicTowerHeading,
            Location = source.Location,
            PoiStatuses = poiStatuses ?? source.PoiStatuses,
            RelicHeadings = relicHeadings ?? source.RelicHeadings,
            ComponentMaterials = source.ComponentMaterials,
            RawPointsOfInterest = replaceRawPoints
                ? rawPoints
                : source.RawPointsOfInterest,
        };
    }

    private void UpdateProximity()
    {
        proximity = null;
        activeMapProjection = null;
        activeMapRelativeHeading = 0;
        SurveyEditor.UpdateLiveMeasurement(null);
        TemplateAuthoring.UpdateContext(
            GetSelectedBaseTemplate(),
            measurement: null);
        var site = ActiveSite;
        if (site is null)
        {
            NotifyCurrentObeliskChanged();
            return;
        }

        var survey = FindSurvey(site);
        var reference = site.Reference;
        var published = reference is null ? null : publishedSites.Find(reference);
        var siteType = survey is not null
            && !string.Equals(
                survey.SiteType,
                "Unknown",
                StringComparison.OrdinalIgnoreCase)
                    ? survey.SiteType
                    : site.SiteType;
        var template = FindTemplate(siteType);
        var location = survey?.Survey.Location
            ?? published?.Location
            ?? site.Location;
        var siteHeading = survey?.Survey.SiteHeading is >= 0 and <= 359
            ? survey.Survey.SiteHeading
            : published?.SiteHeading is >= 0 and <= 359
                ? published.SiteHeading
                : reference?.SiteHeading ?? -1;
        if (template is null)
        {
            NotifyCurrentObeliskChanged();
            return;
        }

        var activeObelisks = GetMergedActiveObelisks(reference, survey);
        var obeliskGroups = GetObeliskGroups(published, survey);
        activeMapProjection = mapProjector.Project(
            template,
            survey?.Survey,
            activeObelisks,
            obeliskGroups,
            ShowComponentMaterials);
        if (currentStatus is null || location is null)
        {
            NotifyCurrentObeliskChanged();
            return;
        }

        activeMapRelativeHeading = SurfaceNavigation.NormalizeDegrees(
            currentStatus.NormalizedHeading - siteHeading);

        proximity = proximityEvaluator.Evaluate(
            currentStatus,
            location.Value,
            siteHeading,
            template,
            survey?.Survey,
            activeObelisks,
            obeliskGroups,
            ShowComponentMaterials);
        if (proximity is { } measurement
            && reference is not null
            && SelectedSite?.Reference == reference)
        {
            var angle = GetSurveyPointAngle(
                measurement.MapX,
                measurement.MapY);
            var rotation = SurfaceNavigation.NormalizeDegrees(
                currentStatus.NormalizedHeading - siteHeading);
            SurveyEditor.UpdateLiveMeasurement(new GuardianSurveyMeasurement(
                measurement.DistanceFromSite,
                angle,
                rotation));
            TemplateAuthoring.UpdateContext(
                GetSelectedBaseTemplate(),
                new GuardianSurveyMeasurement(
                    measurement.DistanceFromSite,
                    angle,
                    rotation));
        }

        NotifyCurrentObeliskChanged();
    }

    private void UpdateMapProjection()
    {
        var row = SelectedSite;
        if (row is null)
        {
            MapProjection = null;
            NotifyMapTextChanged();
            return;
        }

        var survey = FindSurvey(row.Reference);
        var siteType = survey is not null
            && !string.Equals(
                survey.SiteType,
                "Unknown",
                StringComparison.OrdinalIgnoreCase)
                    ? survey.SiteType
                    : row.Reference.SiteType;
        var template = FindTemplate(siteType)
            ?? FindTemplate(row.Reference.SiteType);
        var published = publishedSites.Find(row.Reference);
        MapProjection = template is null
            ? null
            : mapProjector.Project(
                template,
                survey?.Survey,
                GetMergedActiveObelisks(row.Reference, survey),
                GetObeliskGroups(published, survey),
                ShowComponentMaterials);
        NotifyMapTextChanged();
    }

    private IReadOnlyList<GuardianObelisk> GetMergedActiveObelisks(
        GuardianSiteReference? reference,
        GuardianCommanderSiteSurvey? survey)
    {
        var merged = new Dictionary<string, GuardianObelisk>(
            StringComparer.OrdinalIgnoreCase);
        var published = reference is null ? null : publishedSites.Find(reference);
        foreach (var obelisk in published?.ActiveObelisks ?? [])
        {
            merged[obelisk.Name] = obelisk;
        }

        foreach (var obelisk in survey?.ActiveObelisks ?? [])
        {
            merged[obelisk.Name] = obelisk;
        }

        return merged.Values.OrderBy(obelisk => obelisk.Name).ToArray();
    }

    private static IReadOnlySet<char> GetObeliskGroups(
        GuardianPublishedSite? published,
        GuardianCommanderSiteSurvey? survey)
    {
        return survey?.ObeliskGroups is { Count: > 0 } commanderGroups
            ? commanderGroups
            : published?.ObeliskGroups.ToHashSet() ?? new HashSet<char>();
    }

    private void UpdateSurveyEditor()
    {
        var row = SelectedSite;
        var survey = row is null ? null : FindSurvey(row.Reference);
        var siteType = survey is not null
            && !string.Equals(
                survey.SiteType,
                "Unknown",
                StringComparison.OrdinalIgnoreCase)
                    ? survey.SiteType
                    : row?.Reference.SiteType;
        var template = templates.Find(siteType);
        SurveyEditor.Load(
            activeFrontierId,
            activeIsOdyssey,
            survey,
            template,
            ShowComponentMaterials);
        TemplateAuthoring.UpdateContext(template, measurement: null);
    }

    private GuardianSiteTemplate? GetSelectedBaseTemplate()
    {
        var row = SelectedSite;
        if (row is null)
        {
            return null;
        }

        var survey = FindSurvey(row.Reference);
        var siteType = survey is not null
            && !string.Equals(
                survey.SiteType,
                "Unknown",
                StringComparison.OrdinalIgnoreCase)
                    ? survey.SiteType
                    : row.Reference.SiteType;
        return templates.Find(siteType) ?? templates.Find(row.Reference.SiteType);
    }

    private GuardianSiteTemplate? FindTemplate(string? siteType)
    {
        var preview = TemplateAuthoring.PreviewTemplate;
        return preview is not null
            && string.Equals(
                preview.SiteType,
                siteType,
                StringComparison.OrdinalIgnoreCase)
                    ? preview
                    : templates.Find(siteType);
    }

    private void OnTemplateDraftChanged(bool catalogChanged)
    {
        if (catalogChanged)
        {
            templates = TemplateAuthoring.Catalog;
            completionCalculator = new GuardianSurveyCompletionCalculator(
                templates);
            visits = GuardianSiteVisitCatalog.Merge(
                references,
                commanderData,
                publishedSites,
                completionCalculator);
            ApplyFilters();
        }

        UpdateMapProjection();
        UpdateProximity();
    }

    private Task OnSurveySavedAsync(
        GuardianCommanderSiteSurvey previous,
        GuardianCommanderSiteSurvey saved)
    {
        var selectedReference = SelectedSite?.Reference;
        ReplaceSurvey(saved, previous);
        visits = GuardianSiteVisitCatalog.Merge(
            references,
            commanderData,
            publishedSites,
            completionCalculator);
        ApplyFilters();
        if (selectedReference is not null)
        {
            SelectedSite = Rows.FirstOrDefault(
                row => row.Reference == selectedReference)
                ?? SelectedSite;
        }

        UpdateProximity();

        return Task.CompletedTask;
    }

    private GuardianCommanderSiteSurvey? FindSurvey(
        GuardianSiteReference reference)
    {
        return commanderData.Surveys.FirstOrDefault(survey =>
            survey.SystemAddress == reference.SystemAddress
            && survey.Index == reference.Index
            && (reference.BodyId >= 0 && survey.BodyId >= 0
                ? reference.BodyId == survey.BodyId
                : string.Equals(
                    survey.BodyName,
                    reference.FullBodyName,
                    StringComparison.OrdinalIgnoreCase))
            && IsRuins(survey) == (reference.Kind == GuardianSiteKind.Ruins));
    }

    private void NotifyMapTextChanged()
    {
        OnPropertyChanged(nameof(MapTitle));
        OnPropertyChanged(nameof(MapSummary));
        OnPropertyChanged(nameof(MapStatus));
    }

    private void ApplyFilters()
    {
        var previousReference = SelectedSite?.Reference;
        var origin = customOrigin?.Position ?? currentPosition;
        IEnumerable<GuardianSiteVisit> filtered = visits.Visits;
        filtered = selectedKindFilter switch
        {
            "Beacons" => filtered.Where(
                visit => visit.Reference.Kind == GuardianSiteKind.Beacon),
            "Ruins" => filtered.Where(
                visit => visit.Reference.Kind == GuardianSiteKind.Ruins),
            "Structures" => filtered.Where(
                visit => visit.Reference.Kind == GuardianSiteKind.Structure),
            _ => filtered,
        };
        filtered = selectedVisitFilter switch
        {
            "Visited" => filtered.Where(visit => visit.IsVisited),
            "Unvisited" => filtered.Where(visit => !visit.IsVisited),
            _ => filtered,
        };

        if (!string.Equals(
            selectedSiteTypeFilter,
            AllTypes,
            StringComparison.Ordinal))
        {
            filtered = filtered.Where(visit => string.Equals(
                visit.Reference.SiteType,
                selectedSiteTypeFilter,
                StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filterText))
        {
            var text = filterText.Trim();
            filtered = filtered.Where(visit => MatchesText(visit, text));
        }

        var projected = filtered
            .Select(visit => new GuardianSiteRowViewModel(
                visit,
                origin is GalacticCoordinate coordinate
                    ? coordinate.DistanceTo(visit.Reference.Position)
                    : null,
                ramTahLogCodes: GetRamTahLogCodes(visit.Reference)));
        projected = origin is null
            ? projected
                .OrderBy(row => row.Reference.SystemName)
                .ThenBy(row => row.Reference.BodyName)
            : projected
                .OrderBy(row => row.Distance)
                .ThenBy(row => row.Reference.SystemName);
        Rows = projected.ToArray();
        SelectedSite = previousReference is null
            ? Rows.FirstOrDefault()
            : Rows.FirstOrDefault(row => row.Reference == previousReference)
                ?? Rows.FirstOrDefault();
        var visited = Rows.Count(row => row.Visit.IsVisited);
        var surveyed = Rows.Count(row => row.Visit.IsSurveyComplete);
        Summary = $"{Rows.Count:N0} of {references.Count:N0} sites"
            + $" | visited: {visited:N0}"
            + $" | surveys complete: {surveyed:N0}";
        NotifyAuxiliaryOverlayState();
    }

    private bool MatchesText(GuardianSiteVisit visit, string text)
    {
        var reference = visit.Reference;
        return reference.SystemName.Contains(
                text,
                StringComparison.OrdinalIgnoreCase)
            || reference.BodyName.Contains(
                text,
                StringComparison.OrdinalIgnoreCase)
            || reference.SiteType.Contains(
                text,
                StringComparison.OrdinalIgnoreCase)
            || reference.DisplayId.Contains(
                text,
                StringComparison.OrdinalIgnoreCase)
            || reference.SystemAddress.ToString(CultureInfo.InvariantCulture)
                .Contains(text, StringComparison.OrdinalIgnoreCase)
            || visit.Notes.Contains(text, StringComparison.OrdinalIgnoreCase)
            || reference.RelatedStructure?.Contains(
                text,
                StringComparison.OrdinalIgnoreCase) == true
            || GetRamTahLogCodes(reference).Any(code =>
                code.Contains(text, StringComparison.OrdinalIgnoreCase)
                || GetLogDisplayName(code).Contains(
                    text,
                    StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyList<string> GetRamTahLogCodes(
        GuardianSiteReference reference)
    {
        if (!IncludeRamTahLogs || reference.Kind == GuardianSiteKind.Beacon)
        {
            return [];
        }

        var mission = reference.Kind == GuardianSiteKind.Ruins
            ? RamTahMission.AncientRuins
            : RamTahMission.GuardianLogs;
        var missionIsActive = reference.Kind == GuardianSiteKind.Ruins
            ? ramTah?.IsAncientRuinsMissionActive == true
            : ramTah?.IsGuardianLogsMissionActive == true;
        if (ShowOnlyNeededRamTahLogs && ramTah?.IsAnyMissionActive == true
            && !missionIsActive)
        {
            return [];
        }

        var survey = FindSurvey(reference);
        return GetMergedActiveObelisks(reference, survey)
            .Where(obelisk => !string.IsNullOrWhiteSpace(obelisk.LogCode)
                && (!ShowOnlyNeededRamTahLogs
                    || ramTah?.IsAnyMissionActive != true
                    || !ramTah.IsLogCompleted(mission, obelisk.LogCode)))
            .Select(obelisk => obelisk.LogCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

    private sealed class AsyncCommand(
        Func<Task> execute,
        Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return canExecute();
        }

        public async void Execute(object? parameter)
        {
            if (CanExecute(parameter))
            {
                await execute();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed record GuardianOverlaySizeOption(int Index, int Width, int Height)
{
    public string Label => $"{Width:N0} x {Height:N0}";
}

public enum GuardianLiveMapMode
{
    SiteType,
    Heading,
    Map,
    Origin,
}

public enum GuardianAlignmentMode
{
    Buttress,
    RelicTower,
    Alpha,
    Beta,
    Gamma,
    Bear,
    Bowl,
    Crossroads,
    Fistbump,
    Hammerbot,
    Lacrosse,
    Robolobster,
    Squid,
    Stickyhand,
    Turtle,
}

public sealed record GuardianAerialAltitudes(
    double Alpha,
    double Beta,
    double Gamma)
{
    public static GuardianAerialAltitudes Default { get; } =
        new(1_200, 1_550, 1_600);
}

public sealed class GuardianSiteRowViewModel(
    GuardianSiteVisit visit,
    double? distance,
    bool isDestination = false,
    IReadOnlyList<string>? ramTahLogCodes = null)
{
    public GuardianSiteVisit Visit { get; } = visit;

    public GuardianSiteReference Reference => Visit.Reference;

    public double? Distance { get; } = distance;

    public bool IsDestination { get; } = isDestination;

    public IReadOnlyList<string> RamTahLogCodes { get; } = ramTahLogCodes ?? [];

    public bool HasRamTahLogs => RamTahLogCodes.Count > 0;

    public string RamTahLogsText => RamTahLogCodes.Count == 0
        ? "No Ram Tah logs"
        : string.Join(
            ", ",
            RamTahLogCodes.Select(code => $"{code} ({GetRamTahLogName(code)})"));

    public string DisplayId => Reference.DisplayId;

    public string SiteDescription => Reference.Kind == GuardianSiteKind.Ruins
        ? $"{Reference.SiteType} ruins #{Reference.Index}"
        : Reference.SiteType;

    public string DistanceText => Distance is double value
        ? $"{value:N0} ly"
        : "-";

    public string ArrivalText => $"{Reference.DistanceToArrival:N0} ls";

    public string VisitText => Visit.IsVisited
        ? Visit.LastVisited.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : "Not visited";

    public string SurveyText => Reference.Kind == GuardianSiteKind.Beacon
        ? Visit.RecordedObeliskOrLocationCount > 0
            ? $"{Visit.RecordedObeliskOrLocationCount} scan(s)"
            : "Beacon"
        : Visit.SurveyProgress > 0
            ? $"{Visit.SurveyProgress}%"
            : "Not started";

    public string GalacticPosition => Reference.Position.ToString();

    public string SurfaceLocation => Reference.Latitude is double latitude
        && Reference.Longitude is double longitude
            ? FormattableString.Invariant($"{latitude:F6}, {longitude:F6}")
            : "Not recorded";

    public string Notes => string.IsNullOrWhiteSpace(Visit.Notes)
        ? Reference.RelatedStructure is null
            ? "No commander notes."
            : $"Related structure: {Reference.RelatedStructure}"
        : Visit.Notes;

    private static string GetRamTahLogName(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "Unknown";
        }

        var category = code[0] switch
        {
            'B' => "Biology",
            'C' => "Culture",
            'H' => "History",
            'L' => "Language",
            'T' => "Technology",
            '#' => "Guardian",
            _ => "Log",
        };
        var number = code[0] == '#' ? code : $"#{code[1..]}";
        return $"{category} {number}";
    }
}

public sealed record GuardianRamTahLogViewModel(
    string LogCode,
    string LogName,
    string RequirementsText,
    bool HasArtifacts,
    string ObeliskNamesText)
{
    public string ArtifactStatus => HasArtifacts ? "READY" : "MISSING";
}
