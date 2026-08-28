using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Network;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class GuardianViewModel
    : IGuardianOverlayPresentationState,
        IDisposable
{
    private const string AllKinds = "All sites";
    private const string AllVisits = "All visits";
    private const string AllTypes = "All types";
    private const string UnknownLabel = "Unknown";
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
    private readonly GuardianCommanderBeaconStore commanderBeaconStore;
    private readonly GuardianSurveyShareService surveyShareService;
    private readonly RamTahViewModel? ramTah;
    private readonly GuardianOverlaySettingsStore? overlaySettingsStore;
    private readonly Func<GuardianAerialAltitudes> aerialAltitudeProvider;
    private readonly Func<string?> screenshotTargetFolderProvider;
    private readonly IStarSystemResolver systemResolver;
    private readonly AsyncCommand refreshCommand;
    private readonly AsyncCommand toggleCurrentObeliskScannedCommand;
    private readonly AsyncCommand prepareShareBundleCommand;
    private readonly AsyncCommand lookupOriginCommand;
    private readonly AsyncCommand clearOriginCommand;
    private readonly AsyncCommand openSelectedSurveyCommand;
    private GuardianLiveSiteState liveSiteState;
    private GuardianCommanderDataReadResult commanderData =
        GuardianCommanderDataReadResult.Empty;
    private GuardianSiteVisitCatalog visits;
    private IReadOnlyList<GuardianSiteRowViewModel> rows = [];
    private IReadOnlyList<GuardianSiteRowViewModel> currentSystemSites = [];
    private IReadOnlyList<GuardianRamTahLogViewModel> currentRamTahLogs = [];
    private GuardianSiteMapProjection? mapProjection;
    private GuardianSiteMapProjection? activeMapProjection;
    private GuardianSiteProximitySnapshot? proximity;
    private EliteStatus? currentStatus;
    private string? musicTrack;
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
    private bool isLiveStatusObscured;
    private string overlaySettingsStatus = string.Empty;
    private IReadOnlyList<string> shareSiteNames = [];
    private string? shareArchivePath;
    private string shareStatusMessage =
        "Prepare a bundle to find commander survey data not present in the published catalog.";
    private bool isPreparingShareBundle;
    private bool isBlinkGesturePrimed;
    private bool guardianEncodedMaterialsFull;
    private bool guardianMaterialWarningPhase;
    private long guardianMaterialWarningFrame = -1;
    private GuardianSiteBrowserSort siteBrowserSort = GuardianSiteBrowserSort.Distance;
    private bool siteBrowserSortDescending;
    private bool disposed;

    public GuardianViewModel(
        string dataDirectory,
        GuardianViewModelOptions? options = null)
    {
        options ??= new GuardianViewModelOptions();
        var resolvedReferences = options.References;
        var resolvedPublishedSites = options.PublishedSites;
        var resolvedTemplates = options.Templates;
        var resolvedRamTah = options.RamTah;
        var resolvedOverlaySettingsStore = options.OverlaySettingsStore;
        var resolvedSystemResolver = options.SystemResolver;
        var resolvedAerialAltitudeProvider = options.AerialAltitudeProvider;
        var gesturePreferences = options.GesturePreferences;
        var resolvedScreenshotTargetFolderProvider =
            options.ScreenshotTargetFolderProvider;

        this.references = resolvedReferences ?? GuardianSiteCatalog.LoadEmbedded();
        this.publishedSites = resolvedPublishedSites
            ?? GuardianPublishedSiteCatalog.LoadEmbedded();
        this.templates = resolvedTemplates ?? GuardianSiteTemplateCatalog.LoadEmbedded();
        this.ramTah = resolvedRamTah;
        this.overlaySettingsStore = resolvedOverlaySettingsStore;
        this.aerialAltitudeProvider = resolvedAerialAltitudeProvider
            ?? (() => GuardianAerialAltitudes.Default);
        this.screenshotTargetFolderProvider = resolvedScreenshotTargetFolderProvider
            ?? (() => null);
        this.systemResolver = resolvedSystemResolver ?? new SpanshStarSystemResolver();
        var gestures = gesturePreferences ?? GuardianGesturePreferences.Default;
        statusBlinkDetector = new StatusBlinkDetector(
            gestures.BlinkTrigger,
            TimeSpan.FromMilliseconds(gestures.BlinkDurationMilliseconds));
        var overlayPreferences = resolvedOverlaySettingsStore?.Load()
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
        commanderDataReader = new GuardianCommanderDataReader(
            dataDirectory,
            this.publishedSites);
        commanderSurveyStore = new GuardianCommanderSurveyStore(dataDirectory);
        commanderBeaconStore = new GuardianCommanderBeaconStore(dataDirectory);
        surveyShareService = new GuardianSurveyShareService(
            dataDirectory,
            this.publishedSites);
        SurveyEditor = new GuardianSurveyEditorViewModel(
            commanderSurveyStore,
            OnSurveySavedAsync);
        TemplateAuthoring = new GuardianTemplateAuthoringViewModel(
            this.templates,
            OnTemplateDraftChanged,
            pointPreviewChanged: OnTemplatePointPreviewChanged);
        SurveyEditor.PropertyChanged += OnSurveyEditorPropertyChanged;
        TemplateAuthoring.PropertyChanged +=
            OnTemplateAuthoringPropertyChanged;
        liveSiteState = new GuardianLiveSiteState(this.references);
        visits = GuardianSiteVisitCatalog.Merge(
            this.references,
            GuardianCommanderDataReadResult.Empty,
            this.publishedSites,
            completionCalculator);
        UpdateLiveSiteRecoveryReferences();
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
        var openShareWorkspaceCommand = new AsyncCommand(
            OpenShareWorkspaceAsync,
            () => true);
        RefreshCommand = refreshCommand;
        ToggleCurrentObeliskScannedCommand = toggleCurrentObeliskScannedCommand;
        PrepareShareBundleCommand = prepareShareBundleCommand;
        LookupOriginCommand = lookupOriginCommand;
        ClearOriginCommand = clearOriginCommand;
        OpenSelectedSurveyCommand = openSelectedSurveyCommand;
        OpenShareWorkspaceCommand = openShareWorkspaceCommand;
        SortSitesCommand = new ParameterCommand(SortSites);
        ApplyFilters();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        commanderBeaconStore.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<string> KindFilters { get; }

    public IReadOnlyList<string> VisitFilters { get; }

    public IReadOnlyList<string> SiteTypeFilters { get; }

    public IReadOnlyList<GuardianOverlaySizeOption> OverlaySizeOptions { get; } =
        OverlaySizes;

    public ICommand RefreshCommand { get; }

    public ICommand ToggleCurrentObeliskScannedCommand { get; }

    public ICommand PrepareShareBundleCommand { get; }

    public ICommand LookupOriginCommand { get; }

    public ICommand ClearOriginCommand { get; }

    public ICommand OpenSelectedSurveyCommand { get; }

    public ICommand OpenShareWorkspaceCommand { get; }

    public ICommand SortSitesCommand { get; }

    public string SortStatusText => $"Sorted by {GetSortLabel(siteBrowserSort)} "
        + (siteBrowserSortDescending ? "descending" : "ascending");

    public string IdSortIndicator => GetSortIndicator(GuardianSiteBrowserSort.Id);

    public string SystemSortIndicator => GetSortIndicator(GuardianSiteBrowserSort.System);

    public string BodySortIndicator => GetSortIndicator(GuardianSiteBrowserSort.Body);

    public string DistanceSortIndicator => GetSortIndicator(GuardianSiteBrowserSort.Distance);

    public string ArrivalSortIndicator => GetSortIndicator(GuardianSiteBrowserSort.Arrival);

    public string VisitedSortIndicator => GetSortIndicator(GuardianSiteBrowserSort.Visited);

    public string TypeSortIndicator => GetSortIndicator(GuardianSiteBrowserSort.Type);

    public string IndexSortIndicator => GetSortIndicator(GuardianSiteBrowserSort.Index);

    public string ImagesSortIndicator => GetSortIndicator(GuardianSiteBrowserSort.Images);

    public string SurveySortIndicator => GetSortIndicator(GuardianSiteBrowserSort.Survey);

    public string RamTahSortIndicator => GetSortIndicator(GuardianSiteBrowserSort.RamTah);

    public string NotesSortIndicator => GetSortIndicator(GuardianSiteBrowserSort.Notes);

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
                OnPropertyChanged(nameof(DisableRuinsMeasurementGrid));
                SaveOverlayPreferences();
                NotifyGuardianGuidanceChanged();
            }
        }
    }

    /// <summary>
    /// Upstream FormSettings polarity: checked means the heading grid is off.
    /// </summary>
    public bool DisableRuinsMeasurementGrid
    {
        get => !ShowRuinsMeasurementGrid;
        set => ShowRuinsMeasurementGrid = !value;
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
                OnPropertyChanged(nameof(DisableAerialAlignmentGrid));
                SaveOverlayPreferences();
                NotifyGuardianGuidanceChanged();
            }
        }
    }

    /// <summary>
    /// Upstream FormSettings polarity: checked means the aerial grid is off.
    /// </summary>
    public bool DisableAerialAlignmentGrid
    {
        get => !ShowAerialAlignmentGrid;
        set => ShowAerialAlignmentGrid = !value;
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
                NotifyGuardianStatusPanelChanged();
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
                OnPropertyChanged(nameof(GuardianChoiceGestureText));
                OnPropertyChanged(nameof(GuardianOnFootFooter));
                OnPropertyChanged(nameof(GuardianStatusObeliskFooter));
            }
        }
    }

    public string BlinkGestureText => IsBlinkGesturePrimed
        ? "GESTURE READY · toggle once more to confirm"
        : $"Toggle {GetBlinkTriggerName()} 2x to confirm.";

    public string GuardianChoiceGestureText => IsBlinkGesturePrimed
        ? "Cycle firegroup to choose; toggle once more to conf."
        : $"Cycle firegroup to choose; toggle {GetBlinkTriggerName()} 2x to conf.";

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
                : (delta < 20) switch
                {
                    true => 0.8,
                    false => (220 - delta) / 200
                };
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

    public string? HeadingGuideAssetPath => LiveMapMode != GuardianLiveMapMode.Heading
        ? null
        : GetHeadingGuideAssetPath(GetActiveSiteType(), ActiveSite?.Name);

    internal static string? GetHeadingGuideAssetPath(
        string? siteType,
        string? siteName)
    {
        var exact = siteType?.ToLowerInvariant() switch
        {
            "alpha" => "avares://SrvSurvey.Desktop/Assets/GuardianGuidance/alpha-heading-guide.png",
            "beta" => "avares://SrvSurvey.Desktop/Assets/GuardianGuidance/beta-heading-guide.png",
            "gamma" => "avares://SrvSurvey.Desktop/Assets/GuardianGuidance/gamma-heading-guide.png",
            "crossroads" => "avares://SrvSurvey.Desktop/Assets/GuardianGuidance/crossroads-heading-guide.png",
            "fistbump" => "avares://SrvSurvey.Desktop/Assets/GuardianGuidance/fistbump-heading-guide.png",
            "lacrosse" => "avares://SrvSurvey.Desktop/Assets/GuardianGuidance/lacrosse-heading-guide.png",
            _ => null,
        };
        if (exact is not null)
        {
            return exact;
        }

        return siteName?.StartsWith(
                    "$Ancient_Medium",
                    StringComparison.OrdinalIgnoreCase) == true
                || siteName?.StartsWith(
                    "$Ancient_Small",
                    StringComparison.OrdinalIgnoreCase) == true
            ? "avares://SrvSurvey.Desktop/Assets/GuardianGuidance/data-port-heading-guide.png"
            : null;
    }

    public bool HasHeadingGuide => HeadingGuideAssetPath is not null;

    public bool IsGlideApproach => ActiveSite is not null
        && OverlayGameModeResolver.Resolve(
            currentStatus,
            musicTrack: musicTrack) == OverlayGameMode.GlideMode;

    public bool IsLocalGuardianStatus => !IsGlideApproach;

    public string GlideApproachTitle => ActiveSite?.Kind == GuardianSiteKind.Ruins
        ? "APPROACHING GUARDIAN RUINS"
        : "APPROACHING GUARDIAN STRUCTURE";

    public string GlideApproachText => ActiveSite is not { } site
        ? string.Empty
        : (site.Kind == GuardianSiteKind.Ruins) switch
        {
            true => $"Ruins #{site.Index} - {GetActiveSiteType() ?? "unknown layout"}",
            false => GetGuardianBlueprintText(GetActiveSiteType())
        };

    public string GlideApproachFooter =>
        "Remain in glide; the live survey map will continue after approach.";

    public void RefreshAerialGuidance()
    {
        NotifyGuardianGuidanceChanged();
    }

    public void RefreshScreenshotAvailability()
    {
        ApplyFilters();
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

        automaticMapZoom = Math.Abs(next - GetAutomaticMapScale())
            <= 0.0001d;
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

    public IReadOnlyList<GuardianSiteRowViewModel> CurrentSystemSites =>
        currentSystemSites;

    private GuardianSiteRowViewModel[] BuildCurrentSystemSites() => visits
        .Visits
        .Where(visit => visit.Reference.Kind != GuardianSiteKind.Beacon
            && string.Equals(
                visit.Reference.SystemName,
                currentSystemName,
                StringComparison.OrdinalIgnoreCase))
        .Select(visit =>
        {
            var survey = FindSurvey(visit.Reference);
            var neededLogs = GetNeededRamTahLogCodes(
                    visit.Reference.Kind,
                    GetMergedActiveObelisks(visit.Reference, survey))
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return new GuardianSiteRowViewModel(
                visit,
                0,
                IsCurrentDestination(visit.Reference),
                neededLogs);
        })
        .OrderBy(row => row.Reference.BodyName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(row => row.Reference.Index)
        .ToArray();

    public bool HasCurrentSystemSites => CurrentSystemSites.Count > 0;

    public string CurrentSystemGuardianTitle =>
        $"GUARDIAN SITES: {CurrentSystemSites.Count:N0}";

    public IReadOnlyList<GuardianRamTahLogViewModel> CurrentRamTahLogs =>
        currentRamTahLogs;

    public bool HasCurrentRamTahLogs => CurrentRamTahLogs.Count > 0;

    public string CurrentRamTahTitle =>
        $"UNSCANNED RAM TAH LOGS: {CurrentRamTahLogs.Count:N0}";

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
            WellKnownUris.CanonnSignalsSystemPrefix
                + Uri.EscapeDataString(row.Reference.SystemName))
        : null;

    public Uri? SelectedSpanshUri => SelectedSite is { } row
        ? new Uri(
            WellKnownUris.SpanshSystemPrefix
                + row.Reference.SystemAddress.ToString(
                    CultureInfo.InvariantCulture))
        : null;

    public Uri? SelectedEdsmUri => SelectedSite is { } row
        ? new Uri(
            WellKnownUris.EdsmSystemById64Prefix
                + row.Reference.SystemAddress.ToString(
                    CultureInfo.InvariantCulture))
        : null;

    public GuardianSiteMapProjection? MapProjection
    {
        get => mapProjection;
        private set => SetField(ref mapProjection, value);
    }

    public GuardianSiteProximitySnapshot? SelectedMapCommanderPosition =>
        SelectedSite?.Reference is { } selectedReference
        && ActiveSite?.Reference is { } activeReference
        && selectedReference == activeReference
            ? Proximity
            : null;

    public string? SelectedMapTargetPointName =>
        SelectedMapCommanderPosition?.NearestPoint is
        {
            Distance: <= GuardianSiteProximityEvaluator.NearbyPointDistance,
        } nearest
            ? nearest.Point.Name
            : SelectedSite?.Reference is { } selectedReference
                && ActiveSite?.Reference is { } activeReference
                && selectedReference == activeReference
                    ? TargetObeliskName
                    : null;

    public string? SelectedMapPointName =>
        SurveyEditor.SelectedPointName ?? SelectedMapTargetPointName;

    public string? ActiveMapSelectedPointName =>
        SelectedSite?.Reference is { } selectedReference
        && ActiveSite?.Reference is { } activeReference
        && selectedReference == activeReference
        && !string.IsNullOrWhiteSpace(SurveyEditor.SelectedPointName)
            ? SurveyEditor.SelectedPointName
            : Proximity?.NearestPoint is
            {
                Distance: <= GuardianSiteProximityEvaluator.NearbyPointDistance,
            } nearest
                ? nearest.Point.Name
                : TargetObeliskName;

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
        ? (row.Visit.HasCommanderData) switch
        {
            true => "Commander survey states and raw POIs are overlaid on the reference map.",
            false => "Reference map only. Visit this site to begin a commander survey."
        }
        : "Choose a site on the Sites & surveys tab.";

    public GuardianLiveSiteSnapshot? ActiveSite => liveSiteState.CurrentSite;

    public bool HasActiveSite => ActiveSite is not null;

    public bool ShouldShowLiveSiteOverlay => EnableGuardianSites
        && HasActiveSite
        && !(SuppressForActiveBuildProjects && hasActiveBuildProjects)
        && IsLiveMapStatusEligible(currentStatus);

    public bool ShouldShowGuardianStatusOverlay => EnableGuardianSites
        && HasActiveSite
        && IsLiveStatusVisible
        && !(SuppressForActiveBuildProjects && hasActiveBuildProjects)
        && IsGuardianStatusEligible(currentStatus);

    public bool IsLiveStatusVisible => !isLiveStatusObscured;

    public string ActiveSiteTitle => ActiveSite is { } site
        ? (string.IsNullOrWhiteSpace(site.LocalizedName)) switch
        {
            true => (site.Kind == GuardianSiteKind.Ruins) switch
            {
                true => $"Ancient Ruins ({site.Index})",
                false => "Guardian Structure"
            },
            false => site.LocalizedName
        }
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
        ? string.Create(CultureInfo.InvariantCulture,
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
        : (HasActiveSite) switch
        {
            true => "Waiting for surface position, body radius, and site heading.",
            false => "No live Guardian site detected."
        };

    public string NearbyPointText => Proximity?.NearestPoint is { } nearby
        ? GetNearbyPointText(nearby)
        : (HasActiveSite) switch
        {
            true => "No selectable mapped object is available.",
            false => "Approach a Guardian site to begin proximity tracking."
        };

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

    public string CurrentObeliskRequirementsText
    {
        get
        {
            if (CurrentObelisk is not { } obelisk)
            {
                return "Artifact requirements will appear here.";
            }

            var requirements = artifactInventory.GetRequirements(obelisk.ItemCodes);
            return requirements is { Count: > 0 }
                ? string.Join(
                    " + ",
                    requirements.Select(requirement =>
                        $"{requirement.DisplayName} "
                        + $"{requirement.Available}/{requirement.Required}"))
                : "No artifact requirement is recorded.";
        }
    }

    public bool HasCurrentObeliskArtifacts => CurrentObelisk is { } obelisk
        && artifactInventory.HasItems(obelisk.ItemCodes);

    public string CurrentObeliskArtifactStatus
    {
        get
        {
            if (CurrentObelisk is null)
            {
                return "INACTIVE";
            }

            return HasCurrentObeliskArtifacts
                ? "ARTIFACTS READY"
                : "ARTIFACTS MISSING";
        }
    }

    public string CurrentObeliskMissionStatus
    {
        get
        {
            if (CurrentObelisk is not { } obelisk)
            {
                return "No current obelisk is available for mission tracking.";
            }

            if (ramTah is null)
            {
                return "Ram Tah tracking is unavailable.";
            }

            if (!ramTah.IsAnyMissionActive)
            {
                return "No Ram Tah mission is active.";
            }

            if (ramTah.IsLogCompleted(GetMission(), obelisk.LogCode))
            {
                return "Ram Tah log already acquired.";
            }

            return "Needed for the active Ram Tah mission.";
        }
    }

    public string ToggleCurrentObeliskScannedText => CurrentObelisk?.Scanned == true
        ? "Mark not scanned"
        : "Mark scanned";

    public string CurrentObeliskScanStatus
    {
        get
        {
            if (CurrentObelisk is not { } obelisk)
            {
                return "NO OBELISK";
            }

            return obelisk.Scanned
                ? "SCANNED"
                : "NOT SCANNED";
        }
    }

    public bool IsGuardianSiteTypeChoiceVisible => IsLocalGuardianStatus
        && LiveMapMode == GuardianLiveMapMode.SiteType;

    public bool IsGuardianHeadingChoiceVisible => IsLocalGuardianStatus
        && LiveMapMode == GuardianLiveMapMode.Heading;

    public bool IsGuardianOriginVisible => IsLocalGuardianStatus
        && LiveMapMode == GuardianLiveMapMode.Origin;

    public bool IsGuardianObeliskVisible => IsLocalGuardianStatus
        && LiveMapMode == GuardianLiveMapMode.Map
        && Proximity?.NearestPoint?.Point.Type == GuardianPoiType.Obelisk;

    public bool IsGuardianOnFootRelicVisible => IsLocalGuardianStatus
        && LiveMapMode == GuardianLiveMapMode.Map
        && !IsGuardianObeliskVisible
        && currentStatus?.OnFoot == true;

    public bool IsGuardianPoiChoiceVisible => IsLocalGuardianStatus
        && LiveMapMode == GuardianLiveMapMode.Map
        && currentStatus?.OnFoot != true
        && !IsGuardianObeliskVisible
        && Proximity?.NearestPoint?.Point is { } point
        && IsSurveyStatusPoint(point);

    public bool IsGuardianNoPointVisible => IsLocalGuardianStatus
        && LiveMapMode == GuardianLiveMapMode.Map
        && currentStatus?.OnFoot != true
        && !IsGuardianObeliskVisible
        && !IsGuardianPoiChoiceVisible;

    public string GuardianStatusTitle
    {
        get
        {
            if (IsGuardianSiteTypeChoiceVisible)
            {
                return "SITE TYPE UNKNOWN";
            }

            if (IsGuardianHeadingChoiceVisible)
            {
                return "ALIGN SITE HEADING";
            }

            if (IsGuardianOriginVisible)
            {
                return "ALIGN SITE ORIGIN";
            }

            if (IsGuardianOnFootRelicVisible)
            {
                return Proximity?.NearestPoint?.Point is
                { Type: GuardianPoiType.Relic } relic
                        ? $"RELIC TOWER {relic.Name}"
                        : "RELIC TOWER SURVEY";
            }

            if (Proximity?.NearestPoint is not { } nearby)
            {
                return "NO NEARBY GUARDIAN POINT";
            }

            var state = ActiveMapProjection?.Points.FirstOrDefault(point =>
                string.Equals(
                    point.Name,
                    nearby.Point.Name,
                    StringComparison.Ordinal))?.Status
                ?? GuardianPoiStatus.Unknown;
            return $"{nearby.Point.Type.ToString().ToUpperInvariant()} "
                + $"{nearby.Point.Name} · {state.ToString().ToUpperInvariant()}";
        }
    }

    public string GuardianStatusDetail
    {
        get
        {
            if (LiveMapMode == GuardianLiveMapMode.SiteType)
            {
                return ActiveSite?.Kind == GuardianSiteKind.Ruins
                    ? "Select the ruins layout with the active fire group, then use the configured confirmation control twice."
                    : "Type .site <type> to select the mapped Guardian layout.";
            }

            if (IsGuardianHeadingChoiceVisible)
            {
                return
                    "Face the mapped alignment feature, then use the configured confirmation control twice.";
            }

            if (IsGuardianOriginVisible)
            {
                return $"Align with the survey origin and rise to {AlignmentTargetAltitude:N0} m.";
            }

            if (IsGuardianOnFootRelicVisible)
            {
                return GetOnFootRelicGuidance();
            }

            return Proximity?.NearestPoint is { } nearby
                ? $"{nearby.Distance:N1} m away · choose the point state with the active fire group."
                : $"Move within {GuardianSiteProximityEvaluator.NearbyPointDistance:N0} m of a mapped point.";
        }
    }

    public string GuardianOriginFooter =>
        "Use the aerial guide to center and orient the site. Type .map to return to the survey map.";

    public string GuardianOnFootFooter
    {
        get
        {
            if (HasGeneticSamplerEquipped
                && Proximity?.NearestPoint?.Point.Type == GuardianPoiType.Relic)
            {
                return BlinkGestureText;
            }

            return HasGeneticSamplerEquipped
                ? "Approach a relic tower to record its heading."
                : "Equip the genetic sampler and approach a relic tower to record its heading.";
        }
    }

    private GuardianObelisk? GuardianStatusObelisk =>
        Proximity?.NearestPoint?.ActiveObelisk;

    public string GuardianStatusObeliskTitle => GuardianStatusObelisk is { } obelisk
        ? $"{obelisk.Name} · ACTIVE OBELISK"
        : "NO CURRENT ACTIVE OBELISK";

    public string GuardianStatusObeliskLogText => GuardianStatusObelisk is { } obelisk
        ? $"Log: {GetLogDisplayName(obelisk.LogCode)}"
        : "This mapped obelisk is not active at the current site.";

    public string GuardianStatusObeliskRequirementsText
    {
        get
        {
            if (GuardianStatusObelisk is not { } obelisk)
            {
                return "Artifact requirements are unavailable for an inactive obelisk.";
            }

            var requirements = artifactInventory.GetRequirements(obelisk.ItemCodes);
            return requirements is { Count: > 0 }
                ? string.Join(
                    " + ",
                    requirements.Select(requirement =>
                        $"{requirement.DisplayName} {requirement.Available}/{requirement.Required}"))
                : "No artifact requirement is recorded.";
        }
    }

    public IReadOnlyList<GuardianArtifactRequirementViewModel>
        GuardianStatusObeliskArtifacts => GuardianStatusObelisk is { } obelisk
            ? CreateArtifactRequirementRows(
                artifactInventory.GetRequirements(obelisk.ItemCodes))
            : [];

    public string GuardianStatusObeliskMissionStatus
    {
        get
        {
            if (GuardianStatusObelisk is not { } obelisk)
            {
                return "No Ram Tah log is available for this obelisk.";
            }

            if (ramTah is null)
            {
                return "Ram Tah tracking is unavailable.";
            }

            if (!ramTah.IsAnyMissionActive)
            {
                return "No Ram Tah mission is active.";
            }

            if (ramTah.IsLogCompleted(GetMission(), obelisk.LogCode))
            {
                return "Ram Tah log already acquired.";
            }

            return "Needed for the active Ram Tah mission.";
        }
    }

    public string GuardianStatusObeliskScanStatus
    {
        get
        {
            if (GuardianStatusObelisk is not { } obelisk)
            {
                return "INACTIVE";
            }

            return obelisk.Scanned
                ? "SCANNED"
                : "NOT SCANNED";
        }
    }

    public string GuardianStatusObeliskFooter => CurrentObelisk is null
        ? $"Move within {GuardianSiteProximityEvaluator.CurrentObeliskDistance:N0} m to update this obelisk."
        : BlinkGestureText;

    public bool AreGuardianEncodedMaterialsFull => guardianEncodedMaterialsFull;

    public bool HasGuardianMaterialCapacityWarning =>
        AreGuardianEncodedMaterialsFull && GuardianStatusObelisk is not null;

    public string GuardianMaterialCapacityWarning => guardianMaterialWarningPhase
        ? $"Guardian mats full - toggle {GetBlinkTriggerName()} 2x to mark scanned"
        : $"Guardian mats full - toggle {GetBlinkTriggerName()} again to mark scanned";

    public void UpdateOverlayAnimation(DateTimeOffset observedAt)
    {
        if (!HasGuardianMaterialCapacityWarning)
        {
            return;
        }

        var frame = observedAt.ToUnixTimeMilliseconds() / 750;
        if (frame == guardianMaterialWarningFrame)
        {
            return;
        }

        guardianMaterialWarningFrame = frame;
        guardianMaterialWarningPhase = frame % 2 != 0;
        OnPropertyChanged(nameof(GuardianMaterialCapacityWarning));
    }

    public string GuardianChoiceOneText => IsGuardianSiteTypeChoiceVisible
        ? "ALPHA"
        : "PRESENT";

    public string GuardianChoiceTwoText => IsGuardianSiteTypeChoiceVisible
        ? "BETA"
        : "ABSENT";

    public string GuardianChoiceThreeText => IsGuardianSiteTypeChoiceVisible
        ? "GAMMA"
        : "EMPTY";

    public bool IsGuardianChoiceThreeVisible => IsGuardianSiteTypeChoiceVisible
        || Proximity?.NearestPoint?.Point.Type is GuardianPoiType.Orb
            or GuardianPoiType.Casket
            or GuardianPoiType.Tablet
            or GuardianPoiType.Totem
            or GuardianPoiType.Urn;

    public bool IsGuardianChoiceOneSelected => CurrentFireGroupChoice == 0;

    public bool IsGuardianChoiceTwoSelected => CurrentFireGroupChoice == 1;

    public bool IsGuardianChoiceThreeSelected => CurrentFireGroupChoice == 2;

    private int CurrentFireGroupChoice => PositiveModulo(
        currentStatus?.FireGroup ?? 0,
        3);

    private bool HasGeneticSamplerEquipped => string.Equals(
        currentStatus?.SelectedWeapon,
        "$humanoid_companalyser_name;",
        StringComparison.Ordinal);

    private string GetBlinkTriggerName()
    {
        var trigger = currentStatus?.OnFootExterior == true
            ? StatusFlags.ShieldsUp
            : statusBlinkDetector.Trigger;
        return trigger switch
        {
            StatusFlags.HudInAnalysisMode => "cockpit mode",
            StatusFlags.ShieldsUp => "shields",
            StatusFlags.LightsOn => "lights",
            StatusFlags.CargoScoopDeployed => "the cargo scoop",
            StatusFlags.LandingGearDown => "the landing gear",
            StatusFlags.NightVision => "night vision",
            _ => trigger.ToString(),
        };
    }

    private string GetOnFootRelicGuidance()
    {
        if (!HasGeneticSamplerEquipped
            || Proximity?.NearestPoint?.Point is not
            { Type: GuardianPoiType.Relic } relic)
        {
            return HasGeneticSamplerEquipped
                ? "Approach a relic tower to begin its heading survey."
                : "Equip the genetic sampler and approach a relic tower to begin its heading survey.";
        }

        var heading = ActiveMapProjection?.Points.FirstOrDefault(point =>
            string.Equals(point.Name, relic.Name, StringComparison.Ordinal))
            ?.RelicHeading ?? -1;
        return heading >= 0
            ? $"Recorded heading {heading}°. Face the relic tower and toggle shields 2x to update it."
            : "Face the relic tower, then toggle shields 2x to record its heading.";
    }

    private static bool IsSurveyStatusPoint(GuardianPointOfInterest point)
    {
        return point.Type is not GuardianPoiType.Obelisk
            and not GuardianPoiType.BrokenObelisk
            and not GuardianPoiType.EmptyPuddle
            and not GuardianPoiType.DestructiblePanel;
    }

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

    public string OriginStatus
    {
        get
        {
            if (customOrigin is { } origin)
            {
                return $"Distances from custom origin {origin.Name}.";
            }

            if (currentPosition is null)
            {
                return "Distances unavailable until a journal supplies galactic coordinates.";
            }

            return $"Distances from {currentSystemName ?? "current system"}.";
        }
    }

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
                ?? (matches.Count > 0 ? matches[0] : null);
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

    public void SetLiveStatusObscured(bool obscured)
    {
        if (isLiveStatusObscured == obscured)
        {
            return;
        }

        isLiveStatusObscured = obscured;
        OnPropertyChanged(nameof(IsLiveStatusVisible));
        OnPropertyChanged(nameof(ShouldShowGuardianStatusOverlay));
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
        SynchronizeActiveSiteFromStatus(status);
        OnPropertyChanged(nameof(BlinkGestureText));
        OnPropertyChanged(nameof(GuardianChoiceGestureText));
        OnPropertyChanged(nameof(GuardianMaterialCapacityWarning));
        OnPropertyChanged(nameof(GuardianOnFootFooter));
        OnPropertyChanged(nameof(GuardianStatusObeliskFooter));
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
        SynchronizeActiveSiteFromStatus(status);
        OnPropertyChanged(nameof(BlinkGestureText));
        OnPropertyChanged(nameof(GuardianChoiceGestureText));
        OnPropertyChanged(nameof(GuardianMaterialCapacityWarning));
        OnPropertyChanged(nameof(GuardianOnFootFooter));
        OnPropertyChanged(nameof(GuardianStatusObeliskFooter));
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

    public void UpdateCargo(CargoSnapshot cargo)
    {
        ArgumentNullException.ThrowIfNull(cargo);
        if (artifactInventory.Reset(cargo))
        {
            NotifyCurrentObeliskChanged();
            NotifyAuxiliaryOverlayState();
        }
    }

    public void ClearCargo()
    {
        if (artifactInventory.Reset(null))
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
            guardianEncodedMaterialsFull = false;
            guardianMaterialWarningFrame = -1;
            OnPropertyChanged(nameof(AreGuardianEncodedMaterialsFull));
            OnPropertyChanged(nameof(HasGuardianMaterialCapacityWarning));
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

    public async Task<IReadOnlyDictionary<
        JournalEventEnvelope,
        ScreenshotGuardianContext>> ApplyJournalEventsAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        string? commanderName,
        bool allowLiveCommands = true,
        EliteStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        var screenshotStatus = currentStatus ?? status;
        var screenshotContexts = new Dictionary<
            JournalEventEnvelope,
            ScreenshotGuardianContext>();
        if (status is not null)
        {
            currentStatus = status;
            SynchronizeActiveSiteFromStatus(status);
            UpdateProximity();
        }

        var activeSiteChanged = false;
        var surveyChanged = false;
        var inventoryChanged = false;
        var modeChanged = false;
        string? saveStatus = null;
        var isInSrv = (status ?? currentStatus)?.InSrv == true;
        foreach (var journalEvent in journalEvents)
        {
            var outcome = await ApplySingleJournalEventAsync(
                journalEvent,
                commanderName,
                allowLiveCommands,
                screenshotStatus,
                isInSrv,
                cancellationToken);
            modeChanged |= outcome.ModeChanged;
            inventoryChanged |= outcome.InventoryChanged;
            activeSiteChanged |= outcome.ActiveSiteChanged;
            surveyChanged |= outcome.SurveyChanged;
            if (outcome.SaveStatus is not null)
            {
                saveStatus = outcome.SaveStatus;
            }

            if (outcome.ScreenshotContext is { } screenshotContext)
            {
                screenshotContexts[journalEvent] = screenshotContext;
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
            RebuildVisits();
            ApplyFilters();
            SelectActiveReference();
            UpdateProximity();
        }

        if (inventoryChanged)
        {
            NotifyCurrentObeliskChanged();
            NotifyAuxiliaryOverlayState();
        }

        if (modeChanged)
        {
            NotifyAuxiliaryOverlayState();
        }

        if (saveStatus is not null)
        {
            StatusMessage = saveStatus;
        }

        return screenshotContexts;
    }

    private async Task<JournalEventApplyOutcome> ApplySingleJournalEventAsync(
        JournalEventEnvelope journalEvent,
        string? commanderName,
        bool allowLiveCommands,
        EliteStatus? screenshotStatus,
        bool isInSrv,
        CancellationToken cancellationToken)
    {
        if (ramTah is not null)
        {
            await ramTah.ApplyJournalEventsAsync([journalEvent]);
        }

        var modeChanged = ApplyMusicOrHeaderTrack(journalEvent);
        ApplyEncodedMaterialCapacityWarning(journalEvent);
        var inventoryChanged = artifactInventory.Apply(journalEvent, isInSrv);
        var previous = liveSiteState.CurrentSite;
        var recognized = liveSiteState.Apply(journalEvent);
        var activeSiteChanged = liveSiteState.CurrentSite != previous;
        if (activeSiteChanged)
        {
            NotifyActiveSiteChanged();
            UpdateProximity();
        }

        ScreenshotGuardianContext? screenshotContext = null;
        if (journalEvent.EventName == "Screenshot"
            && CreateScreenshotContext(
                journalEvent,
                screenshotStatus) is { } createdContext)
        {
            screenshotContext = createdContext;
        }

        var surveyChanged = await ApplyCodexOrMaterialJournalAsync(
            journalEvent,
            cancellationToken);
        if (allowLiveCommands
            && TryGetSendText(journalEvent) is { } command)
        {
            await HandleLiveCommandAsync(command, cancellationToken);
        }

        var saveStatus = await TryPersistApproachSettlementAsync(
            journalEvent,
            commanderName,
            recognized,
            cancellationToken);
        if (saveStatus is not null)
        {
            surveyChanged = true;
        }

        return new JournalEventApplyOutcome(
            modeChanged,
            inventoryChanged,
            activeSiteChanged,
            surveyChanged,
            saveStatus,
            screenshotContext);
    }

    private bool ApplyMusicOrHeaderTrack(JournalEventEnvelope journalEvent)
    {
        if (journalEvent.EventName is "Fileheader" or "LoadGame")
        {
            var changed = musicTrack is not null;
            musicTrack = null;
            return changed;
        }

        if (journalEvent.EventName != "Music"
            || !journalEvent.Payload.TryGetProperty(
                "MusicTrack",
                out var track))
        {
            return false;
        }

        var nextMusicTrack = track.GetString();
        var modeChanged = !string.Equals(
            musicTrack,
            nextMusicTrack,
            StringComparison.Ordinal);
        musicTrack = nextMusicTrack;
        return modeChanged;
    }

    private void ApplyEncodedMaterialCapacityWarning(
        JournalEventEnvelope journalEvent)
    {
        if (guardianEncodedMaterialsFull
            || !HasFullGuardianEncodedMaterial(journalEvent))
        {
            return;
        }

        guardianEncodedMaterialsFull = true;
        OnPropertyChanged(nameof(AreGuardianEncodedMaterialsFull));
        OnPropertyChanged(nameof(HasGuardianMaterialCapacityWarning));
        OnPropertyChanged(nameof(GuardianMaterialCapacityWarning));
    }

    private async Task<bool> ApplyCodexOrMaterialJournalAsync(
        JournalEventEnvelope journalEvent,
        CancellationToken cancellationToken)
    {
        if (journalEvent.EventName == "CodexEntry")
        {
            return await PersistGuardianBeaconScanAsync(
                       journalEvent,
                       cancellationToken)
                || await MarkNearestRelicPresentAsync(cancellationToken);
        }

        return journalEvent.EventName == "MaterialCollected"
            && CurrentObelisk is not null
            && await SetCurrentObeliskScannedAsync(
                scanned: true,
                cancellationToken);
    }

    private async Task<string?> TryPersistApproachSettlementAsync(
        JournalEventEnvelope journalEvent,
        string? commanderName,
        bool recognized,
        CancellationToken cancellationToken)
    {
        if (!recognized
            || journalEvent.EventName != "ApproachSettlement"
            || liveSiteState.CurrentSite is null
            || activeFrontierId is null)
        {
            return null;
        }

        try
        {
            var existing = FindSurvey(liveSiteState.CurrentSite);
            var survey = liveSiteState.CreateOrUpdateSurvey(
                commanderName ?? string.Empty,
                legacy: !activeIsOdyssey,
                existing);
            survey = HydrateSurveyFromPublished(
                liveSiteState.CurrentSite,
                survey);
            var path = await commanderSurveyStore.SaveAsync(
                activeFrontierId,
                activeIsOdyssey,
                survey,
                cancellationToken);
            ReplaceSurvey(survey with { Path = path }, existing);
            UpdateProximity();
            return $"Recorded the live Guardian site in "
                + $"{Path.GetFileName(path)}.";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            return "The live Guardian site was detected but its survey "
                + "could not be saved: "
                + exception.Message;
        }
    }

    private readonly record struct JournalEventApplyOutcome(
        bool ModeChanged,
        bool InventoryChanged,
        bool ActiveSiteChanged,
        bool SurveyChanged,
        string? SaveStatus,
        ScreenshotGuardianContext? ScreenshotContext);

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

    public Task CopyBodyNameAsync()
    {
        return CopyAsync(SelectedSite?.Reference.FullBodyName, "body name");
    }

    public Task CopyNotesAsync()
    {
        return CopyAsync(SelectedSite?.Visit.Notes, "commander notes");
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
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{latitude:F6}, {longitude:F6}")
                : null;
        return CopyAsync(text, "surface location");
    }

    private async Task<bool> PersistGuardianBeaconScanAsync(
        JournalEventEnvelope journalEvent,
        CancellationToken cancellationToken)
    {
        if (activeFrontierId is null
            || !string.Equals(
                GetJsonString(journalEvent.Payload, "Name"),
                "$Codex_Ent_Guardian_Beacons_Name;",
                StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryBuildBeaconVisitFromJournal(
                journalEvent,
                out var existing,
                out var beacon,
                out var incompleteMessage))
        {
            StatusMessage = incompleteMessage
                ?? "A Guardian beacon scan could not be recorded.";
            return true;
        }

        await SaveGuardianBeaconVisitAsync(
            existing,
            beacon,
            cancellationToken);
        return true;
    }

    private bool TryBuildBeaconVisitFromJournal(
        JournalEventEnvelope journalEvent,
        out GuardianCommanderBeaconVisit? existing,
        out GuardianCommanderBeaconVisit beacon,
        out string? incompleteMessage)
    {
        existing = null;
        beacon = default!;
        incompleteMessage = null;

        var systemAddress = GetJsonInt64(
            journalEvent.Payload,
            "SystemAddress");
        if (systemAddress is null)
        {
            incompleteMessage =
                "A Guardian beacon scan was detected without a system address.";
            return false;
        }

        var bodyId = GetJsonInt32(journalEvent.Payload, "BodyID") ?? -1;
        var reference = references.FindBySystemAddress(systemAddress.Value)
            .Where(candidate => candidate.Kind == GuardianSiteKind.Beacon)
            .FirstOrDefault(candidate => bodyId < 0
                || candidate.BodyId == bodyId);
        var systemName = GetJsonString(journalEvent.Payload, "System")
            ?? reference?.SystemName
            ?? currentSystemName;
        if (string.IsNullOrWhiteSpace(systemName))
        {
            incompleteMessage =
                "A Guardian beacon scan was detected without a system name.";
            return false;
        }

        var bodyName = GetJsonString(journalEvent.Payload, "BodyName")
            ?? reference?.FullBodyName
            ?? currentStatus?.BodyName
            ?? string.Empty;
        if (bodyId < 0)
        {
            bodyId = reference?.BodyId ?? -1;
        }

        var timestamp = journalEvent.Timestamp ?? DateTimeOffset.UtcNow;
        existing = FindExistingBeaconVisit(
            systemAddress.Value,
            bodyId,
            bodyName);
        var scannedLocations = BuildBeaconScannedLocations(
            existing,
            journalEvent,
            timestamp);
        var firstVisited = existing?.FirstVisited is { } first
            && first != DateTimeOffset.MinValue
                ? first
                : timestamp;
        var lastVisited = existing?.LastVisited > timestamp
            ? existing.LastVisited
            : timestamp;
        beacon = new GuardianCommanderBeaconVisit(
            existing?.Path ?? string.Empty,
            firstVisited,
            lastVisited,
            systemName,
            systemAddress.Value,
            bodyName,
            bodyId,
            existing?.Notes ?? string.Empty,
            !activeIsOdyssey,
            scannedLocations);
        return true;
    }

    private GuardianCommanderBeaconVisit? FindExistingBeaconVisit(
        long systemAddress,
        int bodyId,
        string bodyName)
    {
        return commanderData.Beacons.FirstOrDefault(beacon =>
            beacon.SystemAddress == systemAddress
            && (bodyId >= 0 && beacon.BodyId >= 0
                ? beacon.BodyId == bodyId
                : string.Equals(
                    beacon.BodyName,
                    bodyName,
                    StringComparison.OrdinalIgnoreCase)));
    }

    private Dictionary<DateTimeOffset, GuardianSurfaceLocation>
        BuildBeaconScannedLocations(
            GuardianCommanderBeaconVisit? existing,
            JournalEventEnvelope journalEvent,
            DateTimeOffset timestamp)
    {
        var scannedLocations = new Dictionary<
            DateTimeOffset,
            GuardianSurfaceLocation>(existing?.ScannedLocations
                ?? new Dictionary<DateTimeOffset, GuardianSurfaceLocation>());
        var location = GetJournalLocation(journalEvent.Payload);
        if (location is null
            && currentStatus?.HasLatitudeLongitude == true)
        {
            location = new GuardianSurfaceLocation(
                currentStatus.Latitude,
                currentStatus.Longitude);
        }

        if (location is { } scannedLocation)
        {
            scannedLocations[timestamp] = scannedLocation;
        }

        return scannedLocations;
    }

    private async Task SaveGuardianBeaconVisitAsync(
        GuardianCommanderBeaconVisit? existing,
        GuardianCommanderBeaconVisit beacon,
        CancellationToken cancellationToken)
    {
        if (activeFrontierId is null)
        {
            return;
        }

        try
        {
            var path = await commanderBeaconStore.SaveAsync(
                activeFrontierId,
                activeIsOdyssey,
                beacon,
                cancellationToken);
            beacon = beacon with { Path = path };
            commanderData = new GuardianCommanderDataReadResult(
                commanderData.Surveys,
                commanderData.Beacons
                    .Where(candidate => candidate != existing)
                    .Append(beacon)
                    .OrderBy(candidate => candidate.SystemName)
                    .ThenBy(candidate => candidate.BodyName)
                    .ToArray(),
                commanderData.Errors);
            StatusMessage = $"Recorded Guardian beacon scan in {Path.GetFileName(path)}.";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException)
        {
            StatusMessage = "The Guardian beacon scan could not be saved: "
                + exception.Message;
        }
    }

    private Task<bool> MarkNearestRelicPresentAsync(
        CancellationToken cancellationToken)
    {
        if (Proximity?.NearestPoint?.Point is not
            { Type: GuardianPoiType.Relic } point)
        {
            return Task.FromResult(false);
        }

        return SaveActiveSurveyMutationAsync(
            survey =>
            {
                var statuses = new Dictionary<string, GuardianPoiStatus>(
                    survey.Survey.PoiStatuses,
                    StringComparer.Ordinal)
                {
                    [point.Name] = GuardianPoiStatus.Present,
                };
                return survey with
                {
                    Survey = CopySurveyData(
            new GuardianSurveyCopyOptions
            {
                Source = survey.Survey,
                PoiStatuses = statuses,
            }),
                };
            },
            $"Guardian Codex scan marked relic tower {point.Name} present.",
            cancellationToken);
    }

    private static GuardianSurfaceLocation? GetJournalLocation(JsonElement root)
    {
        var latitude = GetJsonDouble(root, "Latitude");
        var longitude = GetJsonDouble(root, "Longitude");
        return latitude is >= -90 and <= 90
            && longitude is >= -180 and <= 180
                ? new GuardianSurfaceLocation(latitude.Value, longitude.Value)
                : null;
    }

    private static string? GetJsonString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static int? GetJsonInt32(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value)
            && value.TryGetInt32(out var number)
                ? number
                : null;
    }

    private static long? GetJsonInt64(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value)
            && value.TryGetInt64(out var number)
                ? number
                : null;
    }

    private static double? GetJsonDouble(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value)
            && value.TryGetDouble(out var number)
            && double.IsFinite(number)
                ? number
                : null;
    }

    private ScreenshotGuardianContext? CreateScreenshotContext(
        JournalEventEnvelope screenshot,
        EliteStatus? statusAtBatchStart)
    {
        if (ActiveSite is not { } site)
        {
            return null;
        }

        var survey = FindSurvey(site);
        var published = GetPublishedSite(site);
        var referenceLocation = site.Reference is
        { Latitude: double latitude, Longitude: double longitude }
                ? new GuardianSurfaceLocation(latitude, longitude)
                : (GuardianSurfaceLocation?)null;
        var origin = survey?.Survey.Location
            ?? published?.Location
            ?? referenceLocation
            ?? site.Location;
        var screenshotLocation = GetJournalLocation(screenshot.Payload);
        EliteStatus? statusWithRadius = null;
        if (statusAtBatchStart?.PlanetRadius > 0)
        {
            statusWithRadius = statusAtBatchStart;
        }
        else if (currentStatus?.PlanetRadius > 0)
        {
            statusWithRadius = currentStatus;
        }
        double? distance = null;
        if (origin is not null
            && screenshotLocation is not null
            && statusWithRadius is not null)
        {
            distance = SurfaceNavigation.GetDistance(
                new SurfaceCoordinate(
                    screenshotLocation.Value.Latitude,
                    screenshotLocation.Value.Longitude),
                new SurfaceCoordinate(
                    origin.Value.Latitude,
                    origin.Value.Longitude),
                (double)statusWithRadius.PlanetRadius);
        }

        var altitude = GetJsonDouble(screenshot.Payload, "Altitude")
            ?? statusAtBatchStart?.Altitude
            ?? currentStatus?.Altitude;
        return new ScreenshotGuardianContext(
            GetActiveSiteType() ?? site.SiteType,
            distance,
            altitude,
            site.Kind,
            site.Index,
            site.LocalizedName);
    }

    public async Task ToggleCurrentObeliskScannedAsync()
    {
        await SetCurrentObeliskScannedAsync(
            CurrentObelisk?.Scanned != true,
            CancellationToken.None);
    }

    private async Task<bool> SetCurrentObeliskScannedAsync(
        bool scanned,
        CancellationToken cancellationToken)
    {
        var site = ActiveSite;
        var currentObelisk = CurrentObelisk;
        if (site is null
            || currentObelisk is null
            || activeFrontierId is null)
        {
            StatusMessage = "Approach an active Guardian obelisk before changing its scan state.";
            return false;
        }

        var existing = FindSurvey(site);
        if (existing is null)
        {
            StatusMessage = "The current Guardian survey is not available to save.";
            return false;
        }

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
                updated,
                cancellationToken);
            updated = updated with { Path = path };
            ReplaceSurvey(updated, existing);
            RebuildVisits();
            ApplyFilters();
            SelectActiveReference();
            UpdateProximity();

            await ApplyObeliskScanRamTahSideEffectsAsync(currentObelisk, scanned);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException)
        {
            StatusMessage = "The current obelisk scan state could not be saved: "
                + exception.Message;
            return false;
        }
    }

    private async Task ApplyObeliskScanRamTahSideEffectsAsync(
        GuardianObelisk currentObelisk,
        bool scanned)
    {
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

    private async Task HandleLiveCommandAsync(
        string message,
        CancellationToken cancellationToken)
    {
        var text = message.Trim();
        if (text.Length == 0 || ActiveSite is null)
        {
            return;
        }

        if (await TryHandleMapModeCommandAsync(text))
        {
            return;
        }

        if (await TryHandlePointStatusCommandAsync(text, cancellationToken))
        {
            return;
        }

        if (await TryHandleNoteCommandAsync(text, cancellationToken))
        {
            return;
        }

        if (await TryHandleSiteTypeCommandAsync(text, cancellationToken))
        {
            return;
        }

        if (await TryHandleHeadingCommandAsync(text, cancellationToken))
        {
            return;
        }

        if (await TryHandleTowerCommandAsync(text, cancellationToken))
        {
            return;
        }

        await TryHandleUtilityCommandAsync(text, cancellationToken);
    }

    private Task<bool> TryHandleMapModeCommandAsync(string text)
    {
        if (string.Equals(text, ".aerial", StringComparison.OrdinalIgnoreCase))
        {
            SetLiveMapModeFromSurvey();
            if (LiveMapMode == GuardianLiveMapMode.Map)
            {
                LiveMapMode = GuardianLiveMapMode.Origin;
                StatusMessage = "Guardian origin-alignment mode enabled.";
            }

            return Task.FromResult(true);
        }

        if (string.Equals(text, ".map", StringComparison.OrdinalIgnoreCase))
        {
            SetLiveMapModeFromSurvey(forceMap: true);
            return Task.FromResult(true);
        }

        if (string.Equals(text, "z", StringComparison.OrdinalIgnoreCase))
        {
            EnableAutomaticMapZoom();
            StatusMessage = "Guardian map zoom returned to automatic mode.";
            return Task.FromResult(true);
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
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    private async Task<bool> TryHandlePointStatusCommandAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var explicitPointStatus = text.ToLowerInvariant() switch
        {
            ".p" => GuardianPoiStatus.Present,
            ".m" => GuardianPoiStatus.Absent,
            ".e" => GuardianPoiStatus.Empty,
            _ => (GuardianPoiStatus?)null,
        };
        if (explicitPointStatus is not { } pointStatus)
        {
            return false;
        }

        await SetNearestPointStatusAsync(
            pointStatus,
            "Guardian command",
            cancellationToken);
        return true;
    }

    private async Task<bool> TryHandleNoteCommandAsync(
        string text,
        CancellationToken cancellationToken)
    {
        if (!text.StartsWith(".note", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var note = text[".note".Length..].Trim();
        if (note.Length == 0)
        {
            StatusMessage = "Type .note followed by text to append a site note.";
            return true;
        }

        await SaveActiveSurveyMutationAsync(
            survey => survey with
            {
                Notes = survey.Notes + $"\r\n{note}\r\n",
            },
            "Appended the Guardian site note.",
            cancellationToken);
        return true;
    }

    private async Task<bool> TryHandleSiteTypeCommandAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var parsedType = LiveMapMode == GuardianLiveMapMode.SiteType
            ? ParseSiteType(text)
            : null;
        if (parsedType is null
            && text.StartsWith(".site", StringComparison.OrdinalIgnoreCase))
        {
            parsedType = ParseSiteType(text[".site".Length..].Trim());
        }

        if (parsedType is null)
        {
            return false;
        }

        var siteType = parsedType.SiteType;
        var isInitialSiteType = ActiveSite is { } activeSite
            && FindSurvey(activeSite) is { } activeSurvey
            && string.Equals(
                activeSurvey.SiteType,
                UnknownLabel,
                StringComparison.OrdinalIgnoreCase);
        if (await SaveActiveSurveyMutationAsync(
            survey => survey with
            {
                SiteType = siteType,
                Survey = CopySurveyData(
                    new GuardianSurveyCopyOptions
                    {
                        Source = survey.Survey,
                        SiteType = siteType,
                    }),
            },
            $"Guardian site type set to {siteType}.",
            cancellationToken))
        {
            SetLiveMapModeFromSurvey();
            if (isInitialSiteType && RevealAndSelectActiveReference())
            {
                SelectedWorkspaceTabIndex = 1;
            }
        }

        return true;
    }

    private async Task<bool> TryHandleHeadingCommandAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var parseResult = TryParseHeadingCommand(text);
        if (parseResult.EnteredHeadingMode)
        {
            return true;
        }

        if (!parseResult.ChangeHeading)
        {
            return false;
        }

        var normalizedHeading = NormalizeHeading(parseResult.NewHeading);
        if (await SaveActiveSurveyMutationAsync(
            survey => survey with
            {
                Survey = CopySurveyData(
                    new GuardianSurveyCopyOptions
                    {
                        Source = survey.Survey,
                        SiteHeading = normalizedHeading,
                    }),
            },
            $"Guardian site heading set to {normalizedHeading}°.",
            cancellationToken))
        {
            LiveMapMode = normalizedHeading == 0
                ? GuardianLiveMapMode.Heading
                : GuardianLiveMapMode.Map;
        }

        return true;
    }

    private readonly record struct HeadingCommandParseResult(
        bool ChangeHeading,
        int NewHeading,
        bool EnteredHeadingMode);

    private HeadingCommandParseResult TryParseHeadingCommand(string text)
    {
        if (LiveMapMode == GuardianLiveMapMode.Heading
            && int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var freeformHeading))
        {
            return new HeadingCommandParseResult(true, freeformHeading, false);
        }

        if (string.Equals(text, ".heading", StringComparison.OrdinalIgnoreCase))
        {
            return ParseDotHeadingCommand();
        }

        if (text.StartsWith(".heading", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(
                text[".heading".Length..].Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedHeading))
        {
            return new HeadingCommandParseResult(true, parsedHeading, false);
        }

        if (string.Equals(text, ".alphaflip", StringComparison.OrdinalIgnoreCase)
            && ActiveSite is { } activeSite
            && FindSurvey(activeSite) is { } alphaSurvey)
        {
            return new HeadingCommandParseResult(
                true,
                alphaSurvey.Survey.SiteHeading + 180,
                false);
        }

        return new HeadingCommandParseResult(false, -1, false);
    }

    private HeadingCommandParseResult ParseDotHeadingCommand()
    {
        if (LiveMapMode == GuardianLiveMapMode.Heading)
        {
            var newHeading = currentStatus?.NormalizedHeading ?? -1;
            return new HeadingCommandParseResult(
                newHeading >= 0,
                newHeading,
                false);
        }

        LiveMapMode = GuardianLiveMapMode.Heading;
        StatusMessage =
            "Face the Guardian alignment feature and type .heading again.";
        return new HeadingCommandParseResult(false, -1, true);
    }

    private async Task<bool> TryHandleTowerCommandAsync(
        string text,
        CancellationToken cancellationToken)
    {
        if (string.Equals(text, ".tower", StringComparison.OrdinalIgnoreCase)
            && ActiveSite?.Kind == GuardianSiteKind.Ruins
            && currentStatus is not null)
        {
            var towerHeading = currentStatus.NormalizedHeading;
            await SaveActiveSurveyMutationAsync(
                survey => survey with
                {
                    Survey = CopySurveyData(
                        new GuardianSurveyCopyOptions
                        {
                            Source = survey.Survey,
                            RelicTowerHeading = towerHeading,
                        }),
                },
                $"Guardian relic-tower heading set to {towerHeading}°.",
                cancellationToken);
            return true;
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
            return true;
        }

        return false;
    }

    private async Task TryHandleUtilityCommandAsync(
        string text,
        CancellationToken cancellationToken)
    {
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

        if (await TryHandleVehicleSiteTypeBlinkAsync(status, cancellationToken))
        {
            return;
        }

        if (await TryHandleVehicleHeadingBlinkAsync(status, cancellationToken))
        {
            return;
        }

        await HandleVehicleMapBlinkAsync(status, cancellationToken);
    }

    private async Task<bool> TryHandleVehicleSiteTypeBlinkAsync(
        EliteStatus status,
        CancellationToken cancellationToken)
    {
        if (LiveMapMode != GuardianLiveMapMode.SiteType
            || ActiveSite is not { Kind: GuardianSiteKind.Ruins })
        {
            return false;
        }

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
                Survey = CopySurveyData(
        new GuardianSurveyCopyOptions
        {
            Source = survey.Survey,
            SiteType = type,
        }),
            },
            $"Guardian blink gesture set the site type to {type}.",
            cancellationToken))
        {
            SetLiveMapModeFromSurvey();
        }

        return true;
    }

    private async Task<bool> TryHandleVehicleHeadingBlinkAsync(
        EliteStatus status,
        CancellationToken cancellationToken)
    {
        if (LiveMapMode != GuardianLiveMapMode.Heading)
        {
            return false;
        }

        var heading = status.NormalizedHeading;
        if (await SaveActiveSurveyMutationAsync(
            survey => survey with
            {
                Survey = CopySurveyData(
        new GuardianSurveyCopyOptions
        {
            Source = survey.Survey,
            SiteHeading = heading,
        }),
            },
            $"Guardian blink gesture set the site heading to {heading}°.",
            cancellationToken))
        {
            LiveMapMode = heading == 0
                ? GuardianLiveMapMode.Heading
                : GuardianLiveMapMode.Map;
        }

        return true;
    }

    private async Task HandleVehicleMapBlinkAsync(
        EliteStatus status,
        CancellationToken cancellationToken)
    {
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
        await SetNearestPointStatusAsync(
            pointStatus,
            "Guardian blink gesture",
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
            new GuardianSurveyCopyOptions
            {
                Source = data,
                RelicTowerHeading = ActiveSite?.Kind == GuardianSiteKind.Ruins
                                ? heading
                                : null,
                PoiStatuses = statuses,
                RelicHeadings = headings,
                RawPoints = rawPoints,
                ReplaceRawPoints = rawPoints is not null,
            }),
                };
            },
            nearestRelic is null
                ? $"Guardian blink gesture set the ruins relic-tower heading to {heading}°."
                : $"Guardian blink gesture set relic tower {nearestRelic.Name} to {heading}°.",
            cancellationToken);
    }

    private async Task SetNearestPointStatusAsync(
        GuardianPoiStatus pointStatus,
        string actionSource,
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
            new GuardianSurveyCopyOptions
            {
                Source = survey.Survey,
                PoiStatuses = statuses,
            }),
                };
            },
            $"{actionSource} marked {point.Name} {pointStatus.ToString().ToLowerInvariant()}.",
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
            new GuardianSurveyCopyOptions
            {
                Source = data,
                PoiStatuses = statuses,
                RelicHeadings = headings,
                RawPoints = rawPoints,
                ReplaceRawPoints = rawPoints is not null,
            }),
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
            new GuardianSurveyCopyOptions
            {
                Source = survey.Survey,
                PoiStatuses = statuses,
            }),
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
            new GuardianSurveyCopyOptions
            {
                Source = data,
                RawPoints = rawPoints,
                ReplaceRawPoints = true,
            }),
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
            new GuardianSurveyCopyOptions
            {
                Source = data,
                PoiStatuses = statuses,
                RelicHeadings = headings,
                RawPoints = rawPoints.Length == 0 ? null : rawPoints,
                ReplaceRawPoints = true,
            }),
                };
            },
            $"Removed local raw Guardian point {nearest.Name}.",
            cancellationToken);
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
                    + ((bundle.Sites.Count == 1) switch
                    {
                        true => "file.",
                        false => "files."
                    });
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

    private async Task RefreshAsync()
    {
        await RefreshAsync(CancellationToken.None);
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
            RebuildVisits();
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

    private bool SelectActiveReference()
    {
        if (ActiveSite is not { } site)
        {
            return false;
        }

        var activeRow = Rows.FirstOrDefault(row => IsSameSite(row.Reference, site));
        if (activeRow is null)
        {
            return false;
        }

        SelectedSite = activeRow;
        return true;
    }

    private bool RevealAndSelectActiveReference()
    {
        if (SelectActiveReference())
        {
            return true;
        }

        var filtersChanged = false;
        filtersChanged |= ResetFilter(
            ref filterText,
            string.Empty,
            nameof(FilterText));
        filtersChanged |= ResetFilter(
            ref selectedKindFilter,
            AllKinds,
            nameof(SelectedKindFilter));
        filtersChanged |= ResetFilter(
            ref selectedVisitFilter,
            AllVisits,
            nameof(SelectedVisitFilter));
        filtersChanged |= ResetFilter(
            ref selectedSiteTypeFilter,
            AllTypes,
            nameof(SelectedSiteTypeFilter));
        if (filtersChanged)
        {
            ApplyFilters();
        }

        return SelectActiveReference();
    }

    private bool ResetFilter(
        ref string field,
        string value,
        string propertyName)
    {
        if (string.Equals(field, value, StringComparison.Ordinal))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private static bool IsSameSite(
        GuardianSiteReference reference,
        GuardianLiveSiteSnapshot site)
    {
        return reference.SystemAddress == site.SystemAddress
            && reference.Index == site.Index
            && reference.Kind == site.Kind
            && (reference.BodyId >= 0 && site.BodyId >= 0
                ? reference.BodyId == site.BodyId
                : string.Equals(
                    reference.FullBodyName,
                    site.BodyName,
                    StringComparison.OrdinalIgnoreCase));
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

    internal static string GetLogDisplayName(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return UnknownLabel;
        }

        var category = code[0] switch
        {
            'B' => "Biology",
            'C' => "Culture",
            'H' => "History",
            'L' => "Language",
            'T' => "Technology",
            '#' => string.Empty,
            _ => "Log",
        };
        var number = code[0] == '#' ? code : $"#{code[1..]}";
        return $"{category} {number}".Trim();
    }

    private GuardianRamTahLogViewModel[] BuildCurrentRamTahLogs()
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
                var isCurrent = CurrentObelisk is { } current
                    && obelisks.Any(obelisk => string.Equals(
                        obelisk.Name,
                        current.Name,
                        StringComparison.OrdinalIgnoreCase));
                var isTarget = TargetObeliskName is { } target
                    && obelisks.Any(obelisk => string.Equals(
                        obelisk.Name,
                        target,
                        StringComparison.OrdinalIgnoreCase));
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
                    string.Join(", ", obelisks.Select(obelisk => obelisk.Name)),
                    isCurrent,
                    isTarget,
                    CreateArtifactRequirementRows(requirements));
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

    private bool IsGuardianSummaryStatusEligible(EliteStatus? status)
    {
        if (status is null)
        {
            return false;
        }

        var mode = OverlayGameModeResolver.Resolve(
            status,
            musicTrack: musicTrack);
        return mode is OverlayGameMode.ExternalPanel
            or OverlayGameMode.Orrery
            or OverlayGameMode.SystemMap
            or OverlayGameMode.SuperCruising;
    }

    private bool IsLiveMapStatusEligible(EliteStatus? status)
    {
        if (status is null
            || !status.HasLatitudeLongitude
            || status.FsdChargingJump)
        {
            return false;
        }

        var mode = OverlayGameModeResolver.Resolve(
            status,
            musicTrack: musicTrack);
        return mode is OverlayGameMode.CommsPanel
            or OverlayGameMode.RolePanel
            or OverlayGameMode.InSrv
            or OverlayGameMode.OnFoot
            or OverlayGameMode.Landed
            or OverlayGameMode.InFighter
            or OverlayGameMode.Flying;
    }

    private static string GetShortArtifactName(string displayName) =>
        displayName.StartsWith("Guardian ", StringComparison.OrdinalIgnoreCase)
            ? displayName["Guardian ".Length..]
            : displayName;

    private static GuardianArtifactRequirementViewModel[] CreateArtifactRequirementRows(
        IReadOnlyList<GuardianArtifactRequirement> requirements) =>
        requirements.Select((requirement, index) =>
                new GuardianArtifactRequirementViewModel(
                    requirement.ShortCode,
                    GetShortArtifactName(requirement.DisplayName),
                    requirement.IsMet,
                    index == requirements.Count - 1
                        ? string.Empty
                        : "+"))
            .ToArray();

    private bool IsGuardianStatusEligible(EliteStatus? status)
    {
        if (status is null)
        {
            return false;
        }

        var mode = OverlayGameModeResolver.Resolve(
            status,
            musicTrack: musicTrack);
        return mode == OverlayGameMode.GlideMode
            || IsLiveMapStatusEligible(status);
    }

    private bool IsRamTahStatusEligible(EliteStatus? status)
    {
        if (status is null)
        {
            return false;
        }

        var mode = OverlayGameModeResolver.Resolve(
            status,
            musicTrack: musicTrack);
        return mode is OverlayGameMode.CommsPanel
            or OverlayGameMode.InternalPanel
            or OverlayGameMode.InSrv
            or OverlayGameMode.OnFoot
            or OverlayGameMode.Landed
            or OverlayGameMode.Flying
            or OverlayGameMode.InFighter;
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
        currentSystemSites = BuildCurrentSystemSites();
        currentRamTahLogs = BuildCurrentRamTahLogs();
        OnPropertyChanged(nameof(ShouldShowLiveSiteOverlay));
        OnPropertyChanged(nameof(ShouldShowGuardianStatusOverlay));
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
        OnPropertyChanged(nameof(ResolvedActiveSiteType));
        OnPropertyChanged(nameof(ShouldShowLiveSiteOverlay));
        OnPropertyChanged(nameof(ShouldShowGuardianStatusOverlay));
        OnPropertyChanged(nameof(SelectedMapCommanderPosition));
        OnPropertyChanged(nameof(SelectedMapTargetPointName));
        OnPropertyChanged(nameof(SelectedMapPointName));
        OnPropertyChanged(nameof(ActiveMapSelectedPointName));
        NotifyAuxiliaryOverlayState();
    }

    private void NotifyCurrentObeliskChanged()
    {
        RefreshAutomaticMapScale();
        OnPropertyChanged(nameof(Proximity));
        OnPropertyChanged(nameof(SelectedMapCommanderPosition));
        OnPropertyChanged(nameof(SelectedMapTargetPointName));
        OnPropertyChanged(nameof(SelectedMapPointName));
        OnPropertyChanged(nameof(ActiveMapSelectedPointName));
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
        NotifyGuardianStatusPanelChanged();
        OnPropertyChanged(nameof(ActiveMapProjection));
        OnPropertyChanged(nameof(ActiveMapTitle));
        OnPropertyChanged(nameof(ActiveMapSummary));
        OnPropertyChanged(nameof(ActiveMapScale));
        OnPropertyChanged(nameof(ActiveMapScaleText));
        OnPropertyChanged(nameof(ActiveMapRelativeHeading));
        OnPropertyChanged(nameof(TargetObeliskText));
        OnPropertyChanged(nameof(ShouldShowLiveSiteOverlay));
        OnPropertyChanged(nameof(ShouldShowGuardianStatusOverlay));
        NotifyGuardianGuidanceChanged();
        currentRamTahLogs = BuildCurrentRamTahLogs();
        OnPropertyChanged(nameof(CurrentRamTahLogs));
        OnPropertyChanged(nameof(HasCurrentRamTahLogs));
        OnPropertyChanged(nameof(CurrentRamTahTitle));
        toggleCurrentObeliskScannedCommand.RaiseCanExecuteChanged();
    }

    private void NotifyGuardianStatusPanelChanged()
    {
        OnPropertyChanged(nameof(IsGuardianSiteTypeChoiceVisible));
        OnPropertyChanged(nameof(IsGuardianHeadingChoiceVisible));
        OnPropertyChanged(nameof(IsGuardianOriginVisible));
        OnPropertyChanged(nameof(IsGuardianObeliskVisible));
        OnPropertyChanged(nameof(IsGuardianOnFootRelicVisible));
        OnPropertyChanged(nameof(IsGuardianPoiChoiceVisible));
        OnPropertyChanged(nameof(IsGuardianNoPointVisible));
        OnPropertyChanged(nameof(GuardianStatusTitle));
        OnPropertyChanged(nameof(GuardianStatusDetail));
        OnPropertyChanged(nameof(GuardianOriginFooter));
        OnPropertyChanged(nameof(GuardianOnFootFooter));
        OnPropertyChanged(nameof(GuardianStatusObeliskTitle));
        OnPropertyChanged(nameof(GuardianStatusObeliskLogText));
        OnPropertyChanged(nameof(GuardianStatusObeliskRequirementsText));
        OnPropertyChanged(nameof(GuardianStatusObeliskArtifacts));
        OnPropertyChanged(nameof(GuardianStatusObeliskMissionStatus));
        OnPropertyChanged(nameof(GuardianStatusObeliskScanStatus));
        OnPropertyChanged(nameof(GuardianStatusObeliskFooter));
        OnPropertyChanged(nameof(HasGuardianMaterialCapacityWarning));
        OnPropertyChanged(nameof(GuardianMaterialCapacityWarning));
        OnPropertyChanged(nameof(GuardianChoiceOneText));
        OnPropertyChanged(nameof(GuardianChoiceTwoText));
        OnPropertyChanged(nameof(GuardianChoiceThreeText));
        OnPropertyChanged(nameof(IsGuardianChoiceThreeVisible));
        OnPropertyChanged(nameof(IsGuardianChoiceOneSelected));
        OnPropertyChanged(nameof(IsGuardianChoiceTwoSelected));
        OnPropertyChanged(nameof(IsGuardianChoiceThreeSelected));
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
            new GuardianAutomaticMapScaleOptions
            {
                SiteKind = ActiveSite?.Kind,
                DistanceFromSite = Proximity?.DistanceFromSite,
                OnFoot = currentStatus?.OnFoot == true,
                UsingSrvTurret = currentStatus?.UsingSrvTurret == true,
                MobileOnSurface = currentStatus is { } status
                    && (status.InSrv || status.OnFoot),
                NearestObeliskDistance = GetNearestObeliskDistance(),
                AutoZoomNearObelisks = AutoZoomNearObelisks,
                AutoZoomInSrvTurret = AutoZoomInSrvTurret,
            });
    }

    internal static double CalculateAutomaticMapScale(
        GuardianAutomaticMapScaleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.AutoZoomInSrvTurret && options.UsingSrvTurret)
        {
            return 3;
        }

        if (options.AutoZoomNearObelisks
            && options.MobileOnSurface
            && options.NearestObeliskDistance < 30)
        {
            return 3;
        }

        if (options.OnFoot)
        {
            return 2;
        }

        if (options.SiteKind == GuardianSiteKind.Ruins)
        {
            if (options.DistanceFromSite is not { } ruinsDistance)
            {
                return 0.65;
            }

            if (ruinsDistance > 1_000)
            {
                return 0.2;
            }

            return ruinsDistance > 800 ? 0.5 : 0.65;
        }

        if (options.DistanceFromSite is not { } distance)
        {
            return 1.5;
        }

        if (distance > 800)
        {
            return 0.2;
        }

        if (distance > 500)
        {
            return 0.5;
        }

        return 1.5;
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
        if (ActiveSite is not { } site)
        {
            return null;
        }

        var commanderType = FindSurvey(site)?.SiteType;
        if (!string.IsNullOrWhiteSpace(commanderType)
            && !string.Equals(
                commanderType,
                UnknownLabel,
                StringComparison.OrdinalIgnoreCase))
        {
            return commanderType;
        }

        return GetPublishedSite(site)?.SiteType
            ?? site.Reference?.SiteType
            ?? site.SiteType;
    }

    public string? ResolvedActiveSiteType => GetActiveSiteType();

    private void SynchronizeActiveSiteFromStatus(EliteStatus status)
    {
        var retainDuringGlide = OverlayGameModeResolver.Resolve(
            status,
            musicTrack: musicTrack) == OverlayGameMode.GlideMode;
        if (!liveSiteState.SynchronizeProximity(status, retainDuringGlide))
        {
            return;
        }

        statusBlinkDetector.Reset();
        IsBlinkGesturePrimed = false;
        SetTargetObelisk(null);
        NotifyActiveSiteChanged();
        SetLiveMapModeFromSurvey();
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

    internal static bool HasFullGuardianEncodedMaterial(
        JournalEventEnvelope journalEvent)
    {
        if (journalEvent.EventName != "Materials"
            || !journalEvent.Payload.TryGetProperty("Encoded", out var encoded)
            || encoded.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var material in encoded.EnumerateArray())
        {
            if (!material.TryGetProperty("Name", out var nameProperty)
                || !material.TryGetProperty("Count", out var countProperty)
                || !countProperty.TryGetInt32(out var count)
                || count < 150)
            {
                continue;
            }

            if (nameProperty.GetString()?.ToLowerInvariant() is
                "ancientbiologicaldata"
                or "ancientlanguagedata"
                or "ancientculturaldata"
                or "ancienttechnologicaldata"
                or "ancienthistoricaldata")
            {
                return true;
            }
        }

        return false;
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
            ? $"{siteType ?? UnknownLabel} layout - no blueprint category recorded"
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
        OnPropertyChanged(nameof(HeadingGuideAssetPath));
        OnPropertyChanged(nameof(HasHeadingGuide));
        OnPropertyChanged(nameof(IsGlideApproach));
        OnPropertyChanged(nameof(IsLocalGuardianStatus));
        OnPropertyChanged(nameof(ShouldShowLiveSiteOverlay));
        OnPropertyChanged(nameof(ShouldShowGuardianStatusOverlay));
        OnPropertyChanged(nameof(GlideApproachTitle));
        OnPropertyChanged(nameof(GlideApproachText));
        OnPropertyChanged(nameof(GlideApproachFooter));
    }

    private void SetLiveMapModeFromSurvey(bool forceMap = false)
    {
        var site = ActiveSite;
        var survey = site is null ? null : FindSurvey(site);
        var published = site is null ? null : GetPublishedSite(site);
        var siteType = GetActiveSiteType();
        var hasType = FindTemplate(siteType) is not null;
        var heading = FirstValidHeading(
            survey?.Survey.SiteHeading,
            published?.SiteHeading,
            site?.Reference?.SiteHeading);
        if (!hasType)
        {
            LiveMapMode = GuardianLiveMapMode.SiteType;
        }
        else if (heading is < 0 or > 359
            || heading == 0 && !forceMap)
        {
            LiveMapMode = GuardianLiveMapMode.Heading;
        }
        else
        {
            LiveMapMode = GuardianLiveMapMode.Map;
        }
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
        currentRamTahLogs = BuildCurrentRamTahLogs();
        OnPropertyChanged(nameof(TargetObeliskName));
        OnPropertyChanged(nameof(SelectedMapTargetPointName));
        OnPropertyChanged(nameof(SelectedMapPointName));
        OnPropertyChanged(nameof(ActiveMapSelectedPointName));
        OnPropertyChanged(nameof(HasTargetObelisk));
        OnPropertyChanged(nameof(TargetObeliskText));
        OnPropertyChanged(nameof(CurrentRamTahLogs));
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
        var index = 1;
        while (true)
        {
            var candidate = $"x{index}";
            if (!used.Contains(candidate))
            {
                return candidate;
            }

            index++;
        }
    }

    private static GuardianSurveyData CopySurveyData(
        GuardianSurveyCopyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var source = options.Source;
        return new GuardianSurveyData
        {
            SiteType = options.SiteType ?? source.SiteType,
            SiteHeading = options.SiteHeading ?? source.SiteHeading,
            RelicTowerHeading = options.RelicTowerHeading
                ?? source.RelicTowerHeading,
            Location = source.Location,
            PoiStatuses = options.PoiStatuses ?? source.PoiStatuses,
            RelicHeadings = options.RelicHeadings ?? source.RelicHeadings,
            ComponentMaterials = source.ComponentMaterials,
            RawPointsOfInterest = options.ReplaceRawPoints
                ? options.RawPoints
                : source.RawPointsOfInterest,
        };
    }

    private GuardianPublishedSite? GetPublishedSite(
        GuardianLiveSiteSnapshot site)
    {
        if (site.Reference is { } reference)
        {
            return publishedSites.Find(reference);
        }

        var fullBodyName = site.BodyName.StartsWith(
            site.SystemName,
            StringComparison.OrdinalIgnoreCase)
                ? site.BodyName
                : $"{site.SystemName} {site.BodyName}".Trim();
        return string.IsNullOrWhiteSpace(fullBodyName)
            ? null
            : publishedSites.Find(site.Kind, fullBodyName, site.Index);
    }

    private GuardianCommanderSiteSurvey HydrateSurveyFromPublished(
        GuardianLiveSiteSnapshot site,
        GuardianCommanderSiteSurvey survey)
    {
        var published = GetPublishedSite(site);
        if (published is null)
        {
            return survey;
        }

        var siteType = string.Equals(
            survey.SiteType,
            UnknownLabel,
            StringComparison.OrdinalIgnoreCase)
                ? published.SiteType
                : survey.SiteType;
        var data = survey.Survey;
        var hydrated = new GuardianSurveyData
        {
            SiteType = siteType,
            SiteHeading = FirstValidHeading(
                data.SiteHeading,
                published.SiteHeading),
            RelicTowerHeading = FirstValidHeading(
                data.RelicTowerHeading,
                published.RelicTowerHeading),
            Location = data.Location ?? published.Location,
            PoiStatuses = data.PoiStatuses,
            RelicHeadings = data.RelicHeadings,
            ComponentMaterials = data.ComponentMaterials,
            RawPointsOfInterest = data.RawPointsOfInterest,
        };
        return survey with
        {
            SiteType = siteType,
            Survey = hydrated,
            ObeliskGroups = survey.ObeliskGroups.Count > 0
                ? survey.ObeliskGroups
                : published.ObeliskGroups.ToHashSet(),
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
        var published = GetPublishedSite(site);
        var siteType = survey is not null
            && !string.Equals(
                survey.SiteType,
                UnknownLabel,
                StringComparison.OrdinalIgnoreCase)
                    ? survey.SiteType
                    : site.SiteType;
        var template = FindTemplate(siteType);
        var location = survey?.Survey.Location
            ?? published?.Location
            ?? site.Location;
        if (SelectedSite?.Reference is { } selectedReference
            && IsSameSite(selectedReference, site)
            && SurveyEditor.TryGetPreviewSurfaceLocation(
                out var previewLocation))
        {
            location = previewLocation;
        }

        var siteHeading = survey?.Survey.SiteHeading is >= 0 and <= 359
            ? survey.Survey.SiteHeading
            : (published?.SiteHeading is >= 0 and <= 359) switch
            {
                true => published.SiteHeading,
                false => reference?.SiteHeading ?? -1
            };
        if (template is null)
        {
            NotifyCurrentObeliskChanged();
            return;
        }

        var activeObelisks = GetMergedActiveObelisks(reference, survey);
        var obeliskGroups = GetObeliskGroups(published, survey);
        var rendererSurvey = MergeRendererSurvey(
            siteType,
            survey?.Survey,
            published,
            reference);
        activeMapProjection = mapProjector.Project(
            template,
            rendererSurvey,
            activeObelisks,
            obeliskGroups,
            ShowComponentMaterials,
            GetNeededRamTahLogCodes(site.Kind, activeObelisks));
        if (currentStatus is null || location is null)
        {
            NotifyCurrentObeliskChanged();
            return;
        }

        activeMapRelativeHeading = SurfaceNavigation.NormalizeDegrees(
            currentStatus.NormalizedHeading - siteHeading);

        proximity = proximityEvaluator.Evaluate(new GuardianSiteProximityEvaluateRequest
        {
            Status = currentStatus,
            SiteLocation = location.Value,
            SiteHeading = siteHeading,
            Template = template,
            Survey = rendererSurvey,
            ActiveObelisks = activeObelisks,
            ObeliskGroups = obeliskGroups,
            IncludeComponentMaterials = ShowComponentMaterials,
        });
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
                UnknownLabel,
                StringComparison.OrdinalIgnoreCase)
                    ? survey.SiteType
                    : row.Reference.SiteType;
        var template = FindTemplate(siteType)
            ?? FindTemplate(row.Reference.SiteType);
        var published = publishedSites.Find(row.Reference);
        var activeObelisks = GetMergedActiveObelisks(row.Reference, survey);
        var rendererSurvey = MergeRendererSurvey(
            siteType,
            survey?.Survey,
            published,
            row.Reference);
        MapProjection = template is null
            ? null
            : mapProjector.Project(
                template,
                rendererSurvey,
                activeObelisks,
                GetObeliskGroups(published, survey),
                ShowComponentMaterials,
                GetNeededRamTahLogCodes(
                    row.Reference.Kind,
                    activeObelisks));
        NotifyMapTextChanged();
    }

    internal static GuardianSurveyData MergeRendererSurvey(
        string siteType,
        GuardianSurveyData? commander,
        GuardianPublishedSite? published,
        GuardianSiteReference? reference)
    {
        var statuses = new Dictionary<string, GuardianPoiStatus>(
            published?.PoiStatuses
                ?? new Dictionary<string, GuardianPoiStatus>(),
            StringComparer.Ordinal);
        foreach (var pair in commander?.PoiStatuses
                     ?? new Dictionary<string, GuardianPoiStatus>())
        {
            statuses[pair.Key] = pair.Value;
        }

        var relicHeadings = new Dictionary<string, int>(
            published?.RelicHeadings ?? new Dictionary<string, int>(),
            StringComparer.Ordinal);
        foreach (var pair in commander?.RelicHeadings
                     ?? new Dictionary<string, int>())
        {
            relicHeadings[pair.Key] = pair.Value;
        }

        return new GuardianSurveyData
        {
            SiteType = siteType,
            SiteHeading = FirstValidHeading(
                commander?.SiteHeading,
                published?.SiteHeading,
                reference?.SiteHeading),
            RelicTowerHeading = FirstValidHeading(
                commander?.RelicTowerHeading,
                published?.RelicTowerHeading,
                reference?.RelicTowerHeading),
            Location = commander?.Location ?? published?.Location,
            PoiStatuses = statuses,
            RelicHeadings = relicHeadings,
            ComponentMaterials = commander?.ComponentMaterials
                ?? new Dictionary<string, GuardianComponentLoadout>(),
            RawPointsOfInterest = commander?.RawPointsOfInterest,
        };
    }

    private HashSet<string> GetNeededRamTahLogCodes(
        GuardianSiteKind kind,
        IReadOnlyList<GuardianObelisk> activeObelisks)
    {
        if (ramTah is null || !IsRamTahMissionActive(kind))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var mission = GetMission(kind);
        return activeObelisks
            .Where(obelisk => !string.IsNullOrWhiteSpace(obelisk.LogCode)
                && !ramTah.IsLogCompleted(mission, obelisk.LogCode))
            .Select(obelisk => obelisk.LogCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private bool IsRamTahMissionActive(GuardianSiteKind kind)
    {
        return kind switch
        {
            GuardianSiteKind.Ruins =>
                ramTah?.IsAncientRuinsMissionActive == true,
            GuardianSiteKind.Structure =>
                ramTah?.IsGuardianLogsMissionActive == true,
            _ => false,
        };
    }

    private static RamTahMission GetMission(GuardianSiteKind kind)
    {
        return kind == GuardianSiteKind.Ruins
            ? RamTahMission.AncientRuins
            : RamTahMission.GuardianLogs;
    }

    private static int FirstValidHeading(params int?[] headings)
    {
        return headings.FirstOrDefault(
            heading => heading is >= 0 and <= 359) ?? -1;
    }

    private GuardianObelisk[] GetMergedActiveObelisks(
        GuardianSiteReference? reference,
        GuardianCommanderSiteSurvey? survey)
    {
        var merged = new Dictionary<string, GuardianObelisk>(
            StringComparer.OrdinalIgnoreCase);
        GuardianPublishedSite? published;
        if (reference is null && ActiveSite is { } site)
        {
            published = GetPublishedSite(site);
        }
        else if (reference is null)
        {
            published = null;
        }
        else
        {
            published = publishedSites.Find(reference);
        }
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
                UnknownLabel,
                StringComparison.OrdinalIgnoreCase)
                    ? survey.SiteType
                    : row?.Reference.SiteType;
        var baseTemplate = templates.Find(siteType);
        var displayTemplate = FindTemplate(siteType) ?? baseTemplate;
        var displayCatalog = displayTemplate is null
            ? templates
            : templates.WithTemplate(displayTemplate);
        SurveyEditor.Load(new GuardianSurveyEditorLoadContext(
            activeFrontierId,
            activeIsOdyssey,
            survey,
            displayTemplate)
        {
            ShowComponentMaterials = ShowComponentMaterials,
            TemplateCatalog = displayCatalog,
            ReferenceProjection = MapProjection,
            SiteReference = row?.Reference,
        });
        TemplateAuthoring.UpdateContext(baseTemplate, measurement: null);
        TemplateAuthoring.SelectPoint(SurveyEditor.SelectedPointName);
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
                UnknownLabel,
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

    private void RebuildVisits()
    {
        visits = GuardianSiteVisitCatalog.Merge(
            references,
            commanderData,
            publishedSites,
            completionCalculator);
        UpdateLiveSiteRecoveryReferences();
        if (currentStatus is not null)
        {
            SynchronizeActiveSiteFromStatus(currentStatus);
        }
    }

    private void UpdateLiveSiteRecoveryReferences()
    {
        liveSiteState.SetRecoveryReferences(
            visits.Visits.Select(visit => visit.Reference));
    }

    private void OnTemplateDraftChanged(bool catalogChanged)
    {
        if (catalogChanged)
        {
            templates = TemplateAuthoring.Catalog;
            completionCalculator = new GuardianSurveyCompletionCalculator(
                templates);
            RebuildVisits();
            ApplyFilters();
        }

        UpdateMapProjection();
        UpdateSurveyEditor();
        UpdateProximity();
    }

    private void OnTemplatePointPreviewChanged()
    {
        UpdateMapProjection();
        UpdateProximity();
    }

    private void OnSurveyEditorPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName
            == nameof(GuardianSurveyEditorViewModel.SelectedPointName))
        {
            OnPropertyChanged(nameof(SelectedMapPointName));
            OnPropertyChanged(nameof(ActiveMapSelectedPointName));
            TemplateAuthoring.SelectPoint(SurveyEditor.SelectedPointName);
        }
        else if (args.PropertyName is
                 nameof(GuardianSurveyEditorViewModel.SurfaceLatitude)
                 or nameof(GuardianSurveyEditorViewModel.SurfaceLongitude))
        {
            UpdateProximity();
        }
    }

    private void OnTemplateAuthoringPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName
            != nameof(GuardianTemplateAuthoringViewModel.SelectedPoint))
        {
            return;
        }

        var selectedName = TemplateAuthoring.SelectedPoint?.Name;
        if (!string.Equals(
                SurveyEditor.SelectedPointName,
                selectedName,
                StringComparison.OrdinalIgnoreCase))
        {
            SurveyEditor.SelectedPointName = selectedName;
        }
    }

    private Task OnSurveySavedAsync(
        GuardianCommanderSiteSurvey previous,
        GuardianCommanderSiteSurvey saved)
    {
        var selectedReference = SelectedSite?.Reference;
        ReplaceSurvey(saved, previous);
        RebuildVisits();
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

        var filteredVisits = filtered.ToArray();
        var screenshots = LoadGuardianScreenshotNames(filteredVisits);
        var projected = filteredVisits
            .Select(visit => new GuardianSiteRowViewModel(
                visit,
                origin is GalacticCoordinate coordinate
                    ? coordinate.DistanceTo(visit.Reference.Position)
                    : null,
                ramTahLogCodes: GetRamTahLogCodes(visit.Reference),
                hasImages: HasGuardianSiteImages(
                    visit.Reference,
                    screenshots)));
        projected = SortSiteRows(projected, origin is not null);
        Rows = projected.ToArray();
        var firstRow = Rows.Count > 0 ? Rows[0] : null;
        SelectedSite = previousReference is null
            ? firstRow
            : Rows.FirstOrDefault(row => row.Reference == previousReference)
                ?? firstRow;
        var visited = Rows.Count(row => row.Visit.IsVisited);
        var surveyed = Rows.Count(row => row.Visit.IsSurveyComplete);
        Summary = $"{Rows.Count:N0} of {visits.Visits.Count:N0} sites"
            + $" | visited: {visited:N0}"
            + $" | surveys complete: {surveyed:N0}";
        NotifyAuxiliaryOverlayState();
    }

    private void SortSites(object? parameter)
    {
        if (parameter is not string value
            || !Enum.TryParse<GuardianSiteBrowserSort>(
                value,
                ignoreCase: true,
                out var requested))
        {
            return;
        }

        if (siteBrowserSort == requested)
        {
            siteBrowserSortDescending = !siteBrowserSortDescending;
        }
        else
        {
            siteBrowserSort = requested;
            siteBrowserSortDescending = false;
        }

        RaiseSiteSortProperties();
        ApplyFilters();
    }

    private string GetSortIndicator(GuardianSiteBrowserSort sort)
    {
        if (siteBrowserSort != sort)
        {
            return string.Empty;
        }

        return siteBrowserSortDescending ? "▼" : "▲";
    }

    private void RaiseSiteSortProperties()
    {
        OnPropertyChanged(nameof(SortStatusText));
        OnPropertyChanged(nameof(IdSortIndicator));
        OnPropertyChanged(nameof(SystemSortIndicator));
        OnPropertyChanged(nameof(BodySortIndicator));
        OnPropertyChanged(nameof(DistanceSortIndicator));
        OnPropertyChanged(nameof(ArrivalSortIndicator));
        OnPropertyChanged(nameof(VisitedSortIndicator));
        OnPropertyChanged(nameof(TypeSortIndicator));
        OnPropertyChanged(nameof(IndexSortIndicator));
        OnPropertyChanged(nameof(ImagesSortIndicator));
        OnPropertyChanged(nameof(SurveySortIndicator));
        OnPropertyChanged(nameof(RamTahSortIndicator));
        OnPropertyChanged(nameof(NotesSortIndicator));
    }

    private IEnumerable<GuardianSiteRowViewModel> SortSiteRows(
        IEnumerable<GuardianSiteRowViewModel> source,
        bool hasOrigin)
    {
        IOrderedEnumerable<GuardianSiteRowViewModel> sorted;
        if (!hasOrigin && siteBrowserSort == GuardianSiteBrowserSort.Distance)
        {
            sorted = siteBrowserSortDescending
                ? source.OrderByDescending(row => row.Reference.SystemName)
                : source.OrderBy(row => row.Reference.SystemName);
        }
        else
        {
            sorted = (siteBrowserSort, siteBrowserSortDescending) switch
            {
                (GuardianSiteBrowserSort.Id, false) =>
                    source.OrderBy(row => row.Reference.SiteId),
                (GuardianSiteBrowserSort.Id, true) =>
                    source.OrderByDescending(row => row.Reference.SiteId),
                (GuardianSiteBrowserSort.System, false) =>
                    source.OrderBy(row => row.Reference.SystemName),
                (GuardianSiteBrowserSort.System, true) =>
                    source.OrderByDescending(row => row.Reference.SystemName),
                (GuardianSiteBrowserSort.Body, false) =>
                    source.OrderBy(row => row.Reference.BodyName),
                (GuardianSiteBrowserSort.Body, true) =>
                    source.OrderByDescending(row => row.Reference.BodyName),
                (GuardianSiteBrowserSort.Distance, false) =>
                    source.OrderBy(row => row.Distance),
                (GuardianSiteBrowserSort.Distance, true) =>
                    source.OrderByDescending(row => row.Distance),
                (GuardianSiteBrowserSort.Arrival, false) =>
                    source.OrderBy(row => row.Reference.DistanceToArrival),
                (GuardianSiteBrowserSort.Arrival, true) =>
                    source.OrderByDescending(row => row.Reference.DistanceToArrival),
                (GuardianSiteBrowserSort.Visited, false) =>
                    source.OrderBy(row => row.Visit.LastVisited),
                (GuardianSiteBrowserSort.Visited, true) =>
                    source.OrderByDescending(row => row.Visit.LastVisited),
                (GuardianSiteBrowserSort.Type, false) =>
                    source.OrderBy(row => row.Reference.SiteType),
                (GuardianSiteBrowserSort.Type, true) =>
                    source.OrderByDescending(row => row.Reference.SiteType),
                (GuardianSiteBrowserSort.Index, false) =>
                    source.OrderBy(row => row.Reference.Index),
                (GuardianSiteBrowserSort.Index, true) =>
                    source.OrderByDescending(row => row.Reference.Index),
                (GuardianSiteBrowserSort.Images, false) =>
                    source.OrderBy(row => row.HasImages),
                (GuardianSiteBrowserSort.Images, true) =>
                    source.OrderByDescending(row => row.HasImages),
                (GuardianSiteBrowserSort.Survey, false) =>
                    source.OrderBy(row => row.Visit.SurveyProgress),
                (GuardianSiteBrowserSort.Survey, true) =>
                    source.OrderByDescending(row => row.Visit.SurveyProgress),
                (GuardianSiteBrowserSort.RamTah, false) =>
                    source.OrderBy(row => row.RamTahLogsText),
                (GuardianSiteBrowserSort.RamTah, true) =>
                    source.OrderByDescending(row => row.RamTahLogsText),
                (GuardianSiteBrowserSort.Notes, false) =>
                    source.OrderBy(row => row.Notes),
                _ => source.OrderByDescending(row => row.Notes),
            };
        }

        return sorted
            .ThenBy(row => row.Reference.SystemName)
            .ThenBy(row => row.Reference.BodyName)
            .ThenBy(row => row.Reference.Index);
    }

    private Dictionary<string, IReadOnlyList<string>>
        LoadGuardianScreenshotNames(IEnumerable<GuardianSiteVisit> source)
    {
        var root = screenshotTargetFolderProvider();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return new Dictionary<string, IReadOnlyList<string>>(
                StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var systemName in source
                     .Select(visit => visit.Reference.SystemName)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var folder = Path.Combine(root, systemName);
                result[systemName] = Directory.Exists(folder)
                    ? Directory.GetFiles(folder, "*.png")
                        .Select(Path.GetFileNameWithoutExtension)
                        .Where(name => name is not null)
                        .Cast<string>()
                        .ToArray()
                    : [];
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or ArgumentException)
            {
                result[systemName] = [];
            }
        }

        return result;
    }

    private static bool HasGuardianSiteImages(
        GuardianSiteReference reference,
        Dictionary<string, IReadOnlyList<string>> screenshots)
    {
        if (reference.Kind == GuardianSiteKind.Beacon)
        {
            return false;
        }

        if (!screenshots.TryGetValue(reference.SystemName, out var files))
        {
            return false;
        }

        var suffix = reference.Kind == GuardianSiteKind.Ruins
            ? $", Ruins{reference.Index}"
            : reference.SiteType;
        return files.Any(file =>
            file.StartsWith(reference.FullBodyName, StringComparison.OrdinalIgnoreCase)
            && file.Contains(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetSortLabel(GuardianSiteBrowserSort sort) => sort switch
    {
        GuardianSiteBrowserSort.Id => "site ID",
        GuardianSiteBrowserSort.System => "system",
        GuardianSiteBrowserSort.Body => "body",
        GuardianSiteBrowserSort.Distance => "system distance",
        GuardianSiteBrowserSort.Arrival => "arrival distance",
        GuardianSiteBrowserSort.Visited => "last visit",
        GuardianSiteBrowserSort.Type => "site type",
        GuardianSiteBrowserSort.Index => "site index",
        GuardianSiteBrowserSort.Images => "images",
        GuardianSiteBrowserSort.Survey => "survey completion",
        GuardianSiteBrowserSort.RamTah => "Ram Tah logs",
        _ => "notes",
    };

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

    private string[] GetRamTahLogCodes(
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

    private sealed class ParameterCommand(Action<object?> execute) : ICommand
    {
        // Never raises: CanExecute is always true for sort commands.
        public event EventHandler? CanExecuteChanged = delegate { };

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            execute(parameter);
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

public enum GuardianSiteBrowserSort
{
    Id,
    System,
    Body,
    Distance,
    Arrival,
    Visited,
    Type,
    Index,
    Images,
    Survey,
    RamTah,
    Notes,
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
    IReadOnlyList<string>? ramTahLogCodes = null,
    bool hasImages = false)
{
    public GuardianSiteVisit Visit { get; } = visit;

    public GuardianSiteReference Reference => Visit.Reference;

    public double? Distance { get; } = distance;

    public bool IsDestination { get; } = isDestination;

    public IReadOnlyList<string> RamTahLogCodes { get; } = ramTahLogCodes ?? [];

    public bool HasRamTahLogs => RamTahLogCodes.Count > 0;

    public bool HasImages { get; } = hasImages;

    public string ImagesText => HasImages ? "yes" : string.Empty;

    public string RamTahLogsText => RamTahLogCodes.Count == 0
        ? "No Ram Tah logs"
        : string.Join(", ", RamTahLogCodes);

    public string DisplayId => Reference.DisplayId;

    public string SiteDescription => Reference.Kind == GuardianSiteKind.Ruins
        ? $"{Reference.SiteType} ruins #{Reference.Index}"
        : Reference.SiteType;

    public bool HasBlueprint => Reference.Kind == GuardianSiteKind.Structure
        && Reference.SiteType.ToLowerInvariant() is
            "robolobster"
            or "squid"
            or "stickyhand"
            or "turtle"
            or "bear"
            or "hammerbot"
            or "bowl";

    public string BlueprintText => HasBlueprint
        ? GuardianViewModel.GetGuardianBlueprintText(Reference.SiteType)
        : string.Empty;

    public string DistanceText => Distance is double value
        ? $"{value:N0} ly"
        : "-";

    public string ArrivalText => $"{Reference.DistanceToArrival:N0} ls";

    public string VisitText => Visit.IsVisited
        ? Visit.LastVisited.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : "Not visited";

    public string SurveyText => Reference.Kind == GuardianSiteKind.Beacon
        ? (Visit.RecordedObeliskOrLocationCount > 0) switch
        {
            true => $"{Visit.RecordedObeliskOrLocationCount} scan(s)",
            false => "Beacon"
        }
        : (Visit.SurveyProgress > 0) switch
        {
            true => $"{Visit.SurveyProgress}%",
            false => "Not started"
        };

    public string GalacticPosition => Reference.Position.ToString();

    public string SurfaceLocation
    {
        get
        {
            if (Reference.Latitude is double latitude
                && Reference.Longitude is double longitude)
            {
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"{latitude:F6}, {longitude:F6}");
            }

            return "Not recorded";
        }
    }

    public string Notes
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Visit.Notes))
            {
                return Visit.Notes;
            }

            return Reference.RelatedStructure is null
                ? "No commander notes."
                : $"Related structure: {Reference.RelatedStructure}";
        }
    }

    public string LegacyDisplayText
    {
        get
        {
            var body = Reference.BodyName.StartsWith(
                    Reference.SystemName,
                    StringComparison.OrdinalIgnoreCase)
                ? Reference.BodyName[Reference.SystemName.Length..].Trim()
                : Reference.BodyName;
            var site = Reference.Kind == GuardianSiteKind.Ruins
                ? $"Ruins #{Reference.Index} - {Reference.SiteType}"
                : Reference.SiteType;
            return $"{body}: {site}";
        }
    }

    public string LegacyBlueprintLine => HasBlueprint
        ? $"\u25ba Blueprint: {BlueprintText}"
        : string.Empty;

    public bool HasLegacySurveyLine => !Visit.IsSurveyComplete;

    public string LegacySurveyLine
    {
        get
        {
            if (!HasLegacySurveyLine)
            {
                return string.Empty;
            }

            var progress = Visit.SurveyProgress > 0
                ? "Incomplete"
                : "Not started";
            return $"\u25ba Survey: {progress}";
        }
    }

    public string LegacyExtraLine => HasRamTahLogs
        ? $"\u25ba Ram Tah: {string.Join(" ", RamTahLogCodes)}"
        : string.Empty;

}

public sealed record GuardianRamTahLogViewModel(
    string LogCode,
    string LogName,
    string RequirementsText,
    bool HasArtifacts,
    string ObeliskNamesText,
    bool IsCurrentObelisk,
    bool IsTargetObelisk,
    IReadOnlyList<GuardianArtifactRequirementViewModel>? RequirementItems = null)
{
    public string ArtifactStatus => HasArtifacts ? "READY" : "MISSING";

    public bool IsMissingArtifacts => !HasArtifacts;

    public bool IsRemoteTargetObelisk => IsTargetObelisk && !IsCurrentObelisk;

    public bool IsCurrentOrTargetObelisk => IsCurrentObelisk || IsTargetObelisk;

    public IReadOnlyList<GuardianArtifactRequirementViewModel> Artifacts =>
        RequirementItems ?? [];
}

public sealed record GuardianArtifactRequirementViewModel(
    string Code,
    string DisplayName,
    bool IsMet,
    string SeparatorText);
