using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class SystemSurveyViewModel : INotifyPropertyChanged
{
    private const int MaximumDisplayedFssBodies = 8;
    private const string OrganicCodexCategory =
        "$Codex_SubCategory_Organic_Structures;";
    private static readonly GalacticCoordinate Sol = new(0, 0, 0);

    private readonly SystemSurveySettingsStore settingsStore;
    private readonly SystemScanState state;
    private readonly ExobiologyReferenceCatalog biologyCatalog;
    private readonly Func<DateTimeOffset> utcNow;
    private EliteStatus? status;
    private ExobiologySnapshot exobiology = ExobiologySnapshot.Empty;
    private BiologyDiscoveryContext biologyDiscoveryContext =
        BiologyDiscoveryContext.Unavailable;
    private SystemScanSnapshot snapshot = SystemScanSnapshot.Empty;
    private IReadOnlyList<FssBodyRowViewModel> fssBodies = [];
    private IReadOnlyList<SurveyBodyReferenceViewModel> dssBodies = [];
    private IReadOnlyList<SurveyBodyReferenceViewModel> biologicalBodies = [];
    private IReadOnlySet<int> canonnBiologyBodyIds = new HashSet<int>();
    private BodyInformationViewModel? bodyInformation;
    private BiologySurveyViewModel? biologySurvey;
    private BiologyStatusViewModel? biologyStatus;
    private BiologyCodexNotificationViewModel? biologyCodexNotification;
    private long? latestBiologyEntryId;
    private bool autoShowBodyInfo;
    private bool showBodyInfoInSystemMap;
    private bool showBodyInfoInOrbit;
    private bool showBodyInfoAtSurface;
    private bool hideBodyInfoInBubble;
    private int bodyInfoBubbleSizeLy;
    private bool hideBodyInfoMaterials;
    private bool autoShowFlightWarnings;
    private double highGravityWarningLevel;
    private bool useExternalData;
    private bool useExternalBioData;
    private bool autoShowBioSystem;
    private bool autoShowBioStatus;
    private bool autoHideBioPlotOnRepeat;
    private bool keepBioPlottersVisibleAfterDss;
    private int bioPlotterDssDurationSeconds;
    private bool autoShowPriorScans;
    private bool skipPriorScansLowValue;
    private int priorScanMinimumValue;
    private bool hideOwnCanonnSignals;
    private bool showCanonnSignalsOnRadar;
    private bool useSmallCanonnRadarCircles;
    private bool autoShowSurfaceRadar;
    private bool autoShowMiniTrack;
    private int surfaceRadarSize;
    private bool autoHideSurfaceRadarWithoutLandingGear;
    private bool autoRemoveTrackerOnSampling;
    private bool autoRemoveTrackerOnFinalSample;
    private bool autoTrackCompositionScans;
    private bool skipAnalyzedCompositionScans;
    private bool drawBodyBiosOnlyWhenNear;
    private bool highlightRegionalFirsts;
    private bool dimAnalyzedOrganisms;
    private bool hideGeoCountInBioSystem;
    private bool disableBioPredictions;
    private bool showTemperatureRangeDebug;
    private bool autoShowLastFssBody;
    private bool autoShowFssInfo;
    private bool showFssInfoInSystemMap;
    private bool showFssInfoInNavigationPanel;
    private bool autoShowSystemStatus;
    private bool hideGeoCount;
    private int fssBodyValueFloor;
    private bool highlightDssCandidates;
    private int dssValueFloor;
    private bool skipDistantDssCandidates;
    private int dssDistanceLimitLs;
    private bool skipGasGiantsForDss;
    private bool skipRingsForDss;
    private bool showNonBodySignals;
    private FssTuningDetectorSettings fssTuningDetector =
        FssTuningDetectorSettings.Default;
    private bool forceShowFssInfo;
    private bool manuallyHideFssInfo;
    private bool forceShowBodyInfo;
    private bool manuallyHideBodyInfo;
    private bool suppressBiologyOverlaysForRepeatVisit;
    private bool fsdJumping;
    private int? timedBiologyBodyId;
    private DateTimeOffset timedBiologyStartedAt;
    private DateTimeOffset timedBiologyExpiresAt;
    private DateTimeOffset lastDssCompletedAt;
    private bool dssVisibilityWindowWasActive;
    private string settingsStatus = string.Empty;
    private FssTuningDetectionState fssTuningState;
    private DateTimeOffset lastFssTuningScanAt;
    private long fssTuningRevision;
    private string fssTuningDetectorStatus = string.Empty;

    public SystemSurveyViewModel(
        SystemSurveySettingsStore settingsStore,
        SystemScanState? state = null,
        ExobiologyReferenceCatalog? biologyCatalog = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        this.state = state ?? new SystemScanState();
        this.biologyCatalog = biologyCatalog
            ?? ExobiologyReferenceCatalog.LoadEmbedded();
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        var preferences = settingsStore.Load();
        autoShowBodyInfo = preferences.AutoShowBodyInfo;
        showBodyInfoInSystemMap = preferences.ShowBodyInfoInSystemMap;
        showBodyInfoInOrbit = preferences.ShowBodyInfoInOrbit;
        showBodyInfoAtSurface = preferences.ShowBodyInfoAtSurface;
        hideBodyInfoInBubble = preferences.HideBodyInfoInBubble;
        bodyInfoBubbleSizeLy = preferences.BodyInfoBubbleSizeLy;
        hideBodyInfoMaterials = preferences.HideBodyInfoMaterials;
        autoShowFlightWarnings = preferences.AutoShowFlightWarnings;
        highGravityWarningLevel = preferences.HighGravityWarningLevel;
        useExternalData = preferences.UseExternalData;
        useExternalBioData = preferences.UseExternalBioData;
        autoShowBioSystem = preferences.AutoShowBioSystem;
        autoShowBioStatus = preferences.AutoShowBioStatus;
        autoHideBioPlotOnRepeat = preferences.AutoHideBioPlotOnRepeat;
        keepBioPlottersVisibleAfterDss =
            preferences.KeepBioPlottersVisibleAfterDss;
        bioPlotterDssDurationSeconds =
            preferences.BioPlotterDssDurationSeconds;
        autoShowPriorScans = preferences.AutoShowPriorScans;
        skipPriorScansLowValue = preferences.SkipPriorScansLowValue;
        priorScanMinimumValue = preferences.PriorScanMinimumValue;
        hideOwnCanonnSignals = preferences.HideOwnCanonnSignals;
        showCanonnSignalsOnRadar = preferences.ShowCanonnSignalsOnRadar;
        useSmallCanonnRadarCircles = preferences.UseSmallCanonnRadarCircles;
        autoShowSurfaceRadar = preferences.AutoShowSurfaceRadar;
        autoShowMiniTrack = preferences.AutoShowMiniTrack;
        surfaceRadarSize = preferences.SurfaceRadarSize;
        autoHideSurfaceRadarWithoutLandingGear =
            preferences.AutoHideSurfaceRadarWithoutLandingGear;
        autoRemoveTrackerOnSampling = preferences.AutoRemoveTrackerOnSampling;
        autoRemoveTrackerOnFinalSample =
            preferences.AutoRemoveTrackerOnFinalSample;
        autoTrackCompositionScans = preferences.AutoTrackCompositionScans;
        skipAnalyzedCompositionScans =
            preferences.SkipAnalyzedCompositionScans;
        drawBodyBiosOnlyWhenNear = preferences.DrawBodyBiosOnlyWhenNear;
        highlightRegionalFirsts = preferences.HighlightRegionalFirsts;
        dimAnalyzedOrganisms = preferences.DimAnalyzedOrganisms;
        hideGeoCountInBioSystem = preferences.HideGeoCountInBioSystem;
        disableBioPredictions = preferences.DisableBioPredictions;
        showTemperatureRangeDebug = preferences.ShowTemperatureRangeDebug;
        autoShowLastFssBody = preferences.AutoShowLastFssBody;
        autoShowFssInfo = preferences.AutoShowFssInfo;
        showFssInfoInSystemMap = preferences.ShowFssInfoInSystemMap;
        showFssInfoInNavigationPanel = preferences.ShowFssInfoInNavigationPanel;
        autoShowSystemStatus = preferences.AutoShowSystemStatus;
        hideGeoCount = preferences.HideGeoCount;
        fssBodyValueFloor = preferences.FssBodyValueFloor;
        highlightDssCandidates = preferences.HighlightDssCandidates;
        dssValueFloor = preferences.DssValueFloor;
        skipDistantDssCandidates = preferences.SkipDistantDssCandidates;
        dssDistanceLimitLs = preferences.DssDistanceLimitLs;
        skipGasGiantsForDss = preferences.SkipGasGiantsForDss;
        skipRingsForDss = preferences.SkipRingsForDss;
        showNonBodySignals = preferences.ShowNonBodySignals;
        fssTuningDetector = preferences.FssTuningDetector;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool AutoShowBodyInfo
    {
        get => autoShowBodyInfo;
        set => SetPreference(ref autoShowBodyInfo, value);
    }

    public bool ShowBodyInfoInSystemMap
    {
        get => showBodyInfoInSystemMap;
        set => SetPreference(ref showBodyInfoInSystemMap, value);
    }

    public bool ShowBodyInfoInOrbit
    {
        get => showBodyInfoInOrbit;
        set => SetPreference(ref showBodyInfoInOrbit, value);
    }

    public bool ShowBodyInfoAtSurface
    {
        get => showBodyInfoAtSurface;
        set => SetPreference(ref showBodyInfoAtSurface, value);
    }

    public bool HideBodyInfoInBubble
    {
        get => hideBodyInfoInBubble;
        set => SetPreference(ref hideBodyInfoInBubble, value);
    }

    public int BodyInfoBubbleSizeLy
    {
        get => bodyInfoBubbleSizeLy;
        set => SetPreference(ref bodyInfoBubbleSizeLy, Math.Max(0, value));
    }

    public bool HideBodyInfoMaterials
    {
        get => hideBodyInfoMaterials;
        set
        {
            if (SetPreference(ref hideBodyInfoMaterials, value))
            {
                RefreshDisplay();
            }
        }
    }

    public bool AutoShowFlightWarnings
    {
        get => autoShowFlightWarnings;
        set => SetPreference(ref autoShowFlightWarnings, value);
    }

    public double HighGravityWarningLevel
    {
        get => highGravityWarningLevel;
        set
        {
            if (SetPreference(
                    ref highGravityWarningLevel,
                    double.IsFinite(value) ? Math.Clamp(value, 0, 50) : 1))
            {
                RefreshDisplay();
            }
        }
    }

    public bool UseExternalData
    {
        get => useExternalData;
        set
        {
            if (SetPreference(ref useExternalData, value))
            {
                OnPropertyChanged(nameof(HasCanonnBiologyHint));
            }
        }
    }

    public bool UseExternalBioData
    {
        get => useExternalBioData;
        set => SetPreference(ref useExternalBioData, value);
    }

    public bool AutoShowBioSystem
    {
        get => autoShowBioSystem;
        set => SetPreference(ref autoShowBioSystem, value);
    }

    public bool AutoShowBioStatus
    {
        get => autoShowBioStatus;
        set => SetPreference(ref autoShowBioStatus, value);
    }

    public bool AutoHideBioPlotOnRepeat
    {
        get => autoHideBioPlotOnRepeat;
        set => SetPreference(ref autoHideBioPlotOnRepeat, value);
    }

    public bool KeepBioPlottersVisibleAfterDss
    {
        get => keepBioPlottersVisibleAfterDss;
        set
        {
            if (SetPreference(ref keepBioPlottersVisibleAfterDss, value))
            {
                dssVisibilityWindowWasActive =
                    IsWithinPostDssBiologyWindow;
                OnPropertyChanged(nameof(IsWithinPostDssBiologyWindow));
            }
        }
    }

    public int BioPlotterDssDurationSeconds
    {
        get => bioPlotterDssDurationSeconds;
        set
        {
            if (SetPreference(
                    ref bioPlotterDssDurationSeconds,
                    Math.Clamp(value, 0, 600)))
            {
                dssVisibilityWindowWasActive =
                    IsWithinPostDssBiologyWindow;
                OnPropertyChanged(nameof(IsWithinPostDssBiologyWindow));
            }
        }
    }

    public bool IsWithinPostDssBiologyWindow =>
        KeepBioPlottersVisibleAfterDss
        && BioPlotterDssDurationSeconds > 0
        && lastDssCompletedAt != default
        && (utcNow() - lastDssCompletedAt).TotalSeconds
            < BioPlotterDssDurationSeconds;

    public bool AreBiologyOverlaysSuppressedForRepeatVisit =>
        AutoHideBioPlotOnRepeat && suppressBiologyOverlaysForRepeatVisit;

    public bool AutoShowPriorScans
    {
        get => autoShowPriorScans;
        set
        {
            if (SetPreference(ref autoShowPriorScans, value))
            {
                OnPropertyChanged(nameof(HasCanonnBiologyHint));
            }
        }
    }

    public bool SkipPriorScansLowValue
    {
        get => skipPriorScansLowValue;
        set => SetPreference(ref skipPriorScansLowValue, value);
    }

    public int PriorScanMinimumValue
    {
        get => priorScanMinimumValue;
        set => SetPreference(ref priorScanMinimumValue, Math.Max(0, value));
    }

    public bool HideOwnCanonnSignals
    {
        get => hideOwnCanonnSignals;
        set => SetPreference(ref hideOwnCanonnSignals, value);
    }

    public bool ShowCanonnSignalsOnRadar
    {
        get => showCanonnSignalsOnRadar;
        set => SetPreference(ref showCanonnSignalsOnRadar, value);
    }

    public bool UseSmallCanonnRadarCircles
    {
        get => useSmallCanonnRadarCircles;
        set => SetPreference(ref useSmallCanonnRadarCircles, value);
    }

    public bool AutoShowSurfaceRadar
    {
        get => autoShowSurfaceRadar;
        set => SetPreference(ref autoShowSurfaceRadar, value);
    }

    public bool AutoShowMiniTrack
    {
        get => autoShowMiniTrack;
        set => SetPreference(ref autoShowMiniTrack, value);
    }

    public int SurfaceRadarSize
    {
        get => surfaceRadarSize;
        set => SetPreference(ref surfaceRadarSize, Math.Clamp(value, 0, 4));
    }

    public bool AutoHideSurfaceRadarWithoutLandingGear
    {
        get => autoHideSurfaceRadarWithoutLandingGear;
        set => SetPreference(ref autoHideSurfaceRadarWithoutLandingGear, value);
    }

    public bool AutoRemoveTrackerOnSampling
    {
        get => autoRemoveTrackerOnSampling;
        set => SetPreference(ref autoRemoveTrackerOnSampling, value);
    }

    public bool AutoRemoveTrackerOnFinalSample
    {
        get => autoRemoveTrackerOnFinalSample;
        set => SetPreference(ref autoRemoveTrackerOnFinalSample, value);
    }

    public bool AutoTrackCompositionScans
    {
        get => autoTrackCompositionScans;
        set => SetPreference(ref autoTrackCompositionScans, value);
    }

    public bool SkipAnalyzedCompositionScans
    {
        get => skipAnalyzedCompositionScans;
        set => SetPreference(ref skipAnalyzedCompositionScans, value);
    }

    public bool DrawBodyBiosOnlyWhenNear
    {
        get => drawBodyBiosOnlyWhenNear;
        set
        {
            if (SetPreference(ref drawBodyBiosOnlyWhenNear, value))
            {
                RefreshDisplay();
            }
        }
    }

    public bool HighlightRegionalFirsts
    {
        get => highlightRegionalFirsts;
        set
        {
            if (SetPreference(ref highlightRegionalFirsts, value))
            {
                RefreshDisplay();
            }
        }
    }

    public bool DimAnalyzedOrganisms
    {
        get => dimAnalyzedOrganisms;
        set
        {
            if (SetPreference(ref dimAnalyzedOrganisms, value))
            {
                RefreshDisplay();
            }
        }
    }

    public bool HideGeoCountInBioSystem
    {
        get => hideGeoCountInBioSystem;
        set
        {
            if (SetPreference(ref hideGeoCountInBioSystem, value))
            {
                RefreshDisplay();
            }
        }
    }

    public bool DisableBioPredictions
    {
        get => disableBioPredictions;
        set
        {
            if (SetPreference(ref disableBioPredictions, value))
            {
                RefreshDisplay();
            }
        }
    }

    public bool ShowTemperatureRangeDebug
    {
        get => showTemperatureRangeDebug;
        set
        {
            if (SetPreference(ref showTemperatureRangeDebug, value))
            {
                RefreshDisplay();
            }
        }
    }

    public bool AutoShowLastFssBody
    {
        get => autoShowLastFssBody;
        set => SetPreference(ref autoShowLastFssBody, value);
    }

    public bool AutoShowFssInfo
    {
        get => autoShowFssInfo;
        set => SetPreference(ref autoShowFssInfo, value);
    }

    public bool ShowFssInfoInSystemMap
    {
        get => showFssInfoInSystemMap;
        set => SetPreference(ref showFssInfoInSystemMap, value);
    }

    public bool ShowFssInfoInNavigationPanel
    {
        get => showFssInfoInNavigationPanel;
        set => SetPreference(ref showFssInfoInNavigationPanel, value);
    }

    public bool AutoShowSystemStatus
    {
        get => autoShowSystemStatus;
        set => SetPreference(ref autoShowSystemStatus, value);
    }

    public bool HideGeoCount
    {
        get => hideGeoCount;
        set
        {
            if (SetPreference(ref hideGeoCount, value))
            {
                RefreshDisplay();
            }
        }
    }

    public int FssBodyValueFloor
    {
        get => fssBodyValueFloor;
        set
        {
            var normalized = Math.Max(0, value);
            if (SetPreference(ref fssBodyValueFloor, normalized))
            {
                RefreshDisplay();
            }
        }
    }

    public bool HighlightDssCandidates
    {
        get => highlightDssCandidates;
        set
        {
            if (SetPreference(ref highlightDssCandidates, value))
            {
                RefreshDisplay();
            }
        }
    }

    public int DssValueFloor
    {
        get => dssValueFloor;
        set
        {
            var normalized = Math.Max(0, value);
            if (SetPreference(ref dssValueFloor, normalized))
            {
                RefreshDisplay();
            }
        }
    }

    public bool SkipDistantDssCandidates
    {
        get => skipDistantDssCandidates;
        set
        {
            if (SetPreference(ref skipDistantDssCandidates, value))
            {
                RefreshDisplay();
            }
        }
    }

    public int DssDistanceLimitLs
    {
        get => dssDistanceLimitLs;
        set
        {
            var normalized = Math.Max(0, value);
            if (SetPreference(ref dssDistanceLimitLs, normalized))
            {
                RefreshDisplay();
            }
        }
    }

    public bool SkipGasGiantsForDss
    {
        get => skipGasGiantsForDss;
        set
        {
            if (SetPreference(ref skipGasGiantsForDss, value))
            {
                RefreshDisplay();
            }
        }
    }

    public bool SkipRingsForDss
    {
        get => skipRingsForDss;
        set
        {
            if (SetPreference(ref skipRingsForDss, value))
            {
                RefreshDisplay();
            }
        }
    }

    public bool ShowNonBodySignals
    {
        get => showNonBodySignals;
        set
        {
            if (SetPreference(ref showNonBodySignals, value))
            {
                OnPropertyChanged(nameof(HasNonBodySignals));
                OnPropertyChanged(nameof(NonBodySignalsText));
            }
        }
    }

    public FssTuningDetectorSettings FssTuningDetector => fssTuningDetector;

    public bool FssTuningDetectorEnabled
    {
        get => FssTuningDetector.Enabled;
        set => SetFssTuningDetectorPreference(
            FssTuningDetector with { Enabled = value });
    }

    public bool SaveFssTuningDiagnosticImages
    {
        get => FssTuningDetector.SaveDiagnosticImages;
        set => SetFssTuningDetectorPreference(
            FssTuningDetector with { SaveDiagnosticImages = value });
    }

    public FssTuningDetectionState FssTuningState => fssTuningState;

    public bool IsFssTuningDetectionPending => FssTuningDetectorEnabled
        && FssTuningState is FssTuningDetectionState.Waiting
            or FssTuningDetectionState.Skipped;

    public string FssTuningIndicator
    {
        get
        {
            if (!FssTuningDetectorEnabled)
            {
                return string.Empty;
            }

            var elapsed = utcNow() - lastFssTuningScanAt;
            if (FssTuningState == FssTuningDetectionState.Waiting
                || lastFssTuningScanAt != default
                    && elapsed.TotalMilliseconds < 250)
            {
                return "⏳";
            }

            return FssTuningState switch
            {
                FssTuningDetectionState.Skipped => "✋",
                FssTuningDetectionState.Yellow => "📡",
                _ => string.Empty,
            };
        }
    }

    public bool HasFssTuningIndicator =>
        !string.IsNullOrEmpty(FssTuningIndicator);

    public string FssTuningDetectorStatus
    {
        get => fssTuningDetectorStatus;
        private set
        {
            if (SetField(ref fssTuningDetectorStatus, value))
            {
                OnPropertyChanged(nameof(HasFssTuningDetectorStatus));
            }
        }
    }

    public bool HasFssTuningDetectorStatus => FssTuningDetectorEnabled
        && !string.IsNullOrWhiteSpace(FssTuningDetectorStatus);

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

    public SystemScanSnapshot Snapshot => snapshot;

    public bool IsFsdJumping => fsdJumping;

    public EliteStatus? CurrentStatus => status;

    public ExobiologySnapshot CurrentExobiology => exobiology;

    public BiologyDiscoveryContext CurrentBiologyDiscoveryContext =>
        biologyDiscoveryContext;

    public BodyInformationViewModel? BodyInformation
    {
        get => bodyInformation;
        private set
        {
            if (SetField(ref bodyInformation, value))
            {
                OnPropertyChanged(nameof(HasBodyInformation));
            }
        }
    }

    public bool HasBodyInformation => BodyInformation is not null;

    public BiologySurveyViewModel? BiologySurvey
    {
        get => biologySurvey;
        private set
        {
            if (SetField(ref biologySurvey, value))
            {
                OnPropertyChanged(nameof(HasBiologySurvey));
            }
        }
    }

    public bool HasBiologySurvey => BiologySurvey is not null;

    public bool HasCanonnBiologyHint
    {
        get
        {
            var selectedBodyId = BiologySurvey?.SelectedBodyId;
            var currentBodyId = !string.IsNullOrWhiteSpace(status?.BodyName)
                ? snapshot.Bodies.FirstOrDefault(body => string.Equals(
                    body.Name,
                    status.BodyName,
                    StringComparison.OrdinalIgnoreCase))?.BodyId
                : snapshot.CurrentBodyId;
            return UseExternalData
                && AutoShowPriorScans
                && selectedBodyId is not null
                && selectedBodyId != currentBodyId
                && canonnBiologyBodyIds.Contains(selectedBodyId.Value);
        }
    }

    public string CanonnBiologyHint =>
        "Canonn has known biological signals for this body.";

    public bool HasTimedBiologySelection => timedBiologyBodyId is not null;

    public double TimedBiologySelectionProgressPercent
    {
        get
        {
            if (timedBiologyBodyId is null
                || timedBiologyExpiresAt <= timedBiologyStartedAt)
            {
                return 0;
            }

            var remaining = timedBiologyExpiresAt - utcNow();
            var total = timedBiologyExpiresAt - timedBiologyStartedAt;
            return Math.Clamp(remaining.TotalMilliseconds
                / total.TotalMilliseconds * 100d, 0d, 100d);
        }
    }

    public BiologyStatusViewModel? BiologyStatus
    {
        get => biologyStatus;
        private set
        {
            if (SetField(ref biologyStatus, value))
            {
                OnPropertyChanged(nameof(HasBiologyStatus));
            }
        }
    }

    public bool HasBiologyStatus => BiologyStatus is not null;

    public long? LatestBiologyEntryId => latestBiologyEntryId;

    public bool IsBodyInfoForced => forceShowBodyInfo;

    public bool IsWithinBodyInfoBubble => snapshot.StarPosition is { } position
        && position.DistanceTo(Sol) < BodyInfoBubbleSizeLy;

    public string SystemTitle
    {
        get
        {
            if (string.IsNullOrWhiteSpace(snapshot.SystemName))
            {
                return "WAITING FOR SYSTEM";
            }

            var mainStar = snapshot.Bodies.FirstOrDefault(body =>
                body.Kind == SystemBodyKind.Star
                && (body.BodyId == 0
                    || body.Name.EndsWith(" A", StringComparison.Ordinal)));
            var prefix = mainStar?.WasDiscovered == false ? "⚑ " : string.Empty;
            var suffix = snapshot.AllBodiesFound ? "  ✓" : string.Empty;
            return prefix + snapshot.SystemName + suffix;
        }
    }

    public string ScanSummary
    {
        get
        {
            var scannedCount = snapshot.Bodies.Count(body =>
                body.IsScanned && body.Kind != SystemBodyKind.Asteroid);
            var prefix = snapshot.AllBodiesFound
                ? $"Scanned all {scannedCount:N0} bodies"
                : $"Scanned {scannedCount:N0} bodies";
            return $"{prefix} · {FormatCredits(snapshot.CurrentScanValue)}";
        }
    }

    public string FssFilterDescription =>
        $"Showing bodies worth at least {FormatCredits(FssBodyValueFloor)}, "
        + "plus terraformable and signal-bearing bodies.";

    public IReadOnlyList<FssBodyRowViewModel> FssBodies
    {
        get => fssBodies;
        private set
        {
            if (SetField(ref fssBodies, value))
            {
                OnPropertyChanged(nameof(HasFssBodies));
                OnPropertyChanged(nameof(DisplayedFssBodies));
                OnPropertyChanged(nameof(HasMoreFssBodies));
                OnPropertyChanged(nameof(MoreFssBodiesText));
            }
        }
    }

    public bool HasFssBodies => FssBodies.Count > 0;

    public IReadOnlyList<FssBodyRowViewModel> DisplayedFssBodies => FssBodies
        .Take(MaximumDisplayedFssBodies)
        .ToArray();

    public bool HasMoreFssBodies => FssBodies.Count > MaximumDisplayedFssBodies;

    public string MoreFssBodiesText =>
        $"+ {FssBodies.Count - MaximumDisplayedFssBodies:N0} more qualifying bodies";

    public string FssEmptyText => "Scan a body in the FSS to populate this list.";

    public SystemScanBodySnapshot? LastFssBody => snapshot.LastDetailedBodyId is { } id
        ? snapshot.Bodies.FirstOrDefault(body => body.BodyId == id)
        : null;

    public bool HasLastFssBody => LastFssBody is not null;

    public string LastFssBodyName => LastFssBody is { } body
        ? (body.WasDiscovered ? string.Empty : "⚑ ") + body.Name
        : "Waiting for a detailed body scan";

    public string LastFssBodyClass => LastFssBody is { } body
        ? body.PlanetClass ?? "Unknown body"
        : "Tune the FSS to a planet";

    public string LastFssBodyDistance => LastFssBody is { } body
        ? $"{body.DistanceFromArrivalLs:N0} LS"
        : string.Empty;

    public string LastFssScanValue => LastFssBody is { } body
        ? FormatCredits(body.ScanValue)
        : "—";

    public string LastFssMappedValue => LastFssBody is { } body
        ? FormatCredits(body.EstimatedMappedValue)
        : "—";

    public string LastFssMarkers
    {
        get
        {
            if (LastFssBody is not { } body)
            {
                return string.Empty;
            }

            var markers = new List<string>();
            if (body.IsTerraformable || body.IsEarthLike)
            {
                markers.Add("TERRAFORMABLE");
            }

            if (body.IsLandable)
            {
                markers.Add("LANDABLE");
            }

            return string.Join(" · ", markers);
        }
    }

    public bool HasLastFssMarkers => !string.IsNullOrWhiteSpace(LastFssMarkers);

    public string LastFssSignalsText => LastFssBody is
    { BiologicalSignalCount: > 0 } body
            ? body.BiologicalSignalCount == 1
                ? "1 biological signal"
                : $"{body.BiologicalSignalCount:N0} biological signals"
            : string.Empty;

    public bool HasLastFssSignals => !string.IsNullOrWhiteSpace(
        LastFssSignalsText);

    public string SystemStatusText
    {
        get
        {
            if (!snapshot.HasDiscoveryScan)
            {
                return "FSS not started";
            }

            if (snapshot.IsFssComplete)
            {
                return DssBodies.Count == 0 ? "DSS survey: None" : "DSS survey";
            }

            var percent = snapshot.ExpectedBodyCount <= 0
                ? 0
                : Math.Clamp(
                    (int)(100d * snapshot.FssBodyCount / snapshot.ExpectedBodyCount),
                    0,
                    100);
            return DssBodies.Count == 0
                ? $"FSS {percent:N0}% complete"
                : $"FSS {percent:N0}%";
        }
    }

    public IReadOnlyList<SurveyBodyReferenceViewModel> DssBodies
    {
        get => dssBodies;
        private set
        {
            if (SetField(ref dssBodies, value))
            {
                OnPropertyChanged(nameof(HasDssBodies));
                OnPropertyChanged(nameof(DssHeading));
            }
        }
    }

    public bool HasDssBodies => DssBodies.Count > 0;

    public string DssHeading => DssBodies.Count == 1
        ? "1 body remaining"
        : $"{DssBodies.Count:N0} bodies remaining";

    public IReadOnlyList<SurveyBodyReferenceViewModel> BiologicalBodies
    {
        get => biologicalBodies;
        private set
        {
            if (SetField(ref biologicalBodies, value))
            {
                OnPropertyChanged(nameof(HasBiologicalBodies));
                OnPropertyChanged(nameof(BiologicalHeading));
            }
        }
    }

    public bool HasBiologicalBodies => BiologicalBodies.Count > 0;

    public string BiologicalHeading => snapshot.BiologicalSignalsRemaining == 1
        ? "1 biological signal remaining"
        : $"{snapshot.BiologicalSignalsRemaining:N0} biological signals remaining";

    public bool HasNonBodySignals => ShowNonBodySignals
        && snapshot.NonBodySignalCount > 0;

    public string NonBodySignalsText => snapshot.NonBodySignalCount == 1
        ? "1 non-body signal"
        : $"{snapshot.NonBodySignalCount:N0} non-body signals";

    public bool IsFssInfoForced => forceShowFssInfo;

    public bool ShouldShowFssInfo
    {
        get
        {
            if (!AutoShowFssInfo
                || snapshot.SystemAddress is null
                || manuallyHideFssInfo)
            {
                return false;
            }

            var automatic = status?.GuiFocus == GuiFocus.Fss
                || ShowFssInfoInSystemMap
                    && status?.GuiFocus == GuiFocus.SystemMap
                || ShowFssInfoInNavigationPanel
                    && status?.GuiFocus == GuiFocus.ExternalPanel;
            var forced = forceShowFssInfo && !fsdJumping;
            return automatic || forced;
        }
    }

    public bool ShouldShowLastFssBody => AutoShowLastFssBody
        && snapshot.SystemAddress is not null
        && status?.GuiFocus == GuiFocus.Fss;

    public bool ShouldShowBodyInfo
    {
        get
        {
            if (!AutoShowBodyInfo
                || BodyInformation is null
                || manuallyHideBodyInfo
                || fsdJumping
                || HideBodyInfoInBubble && IsWithinBodyInfoBubble)
            {
                return false;
            }

            if (forceShowBodyInfo)
            {
                return true;
            }

            if (status is null)
            {
                return false;
            }

            var inSystemMap = status.GuiFocus is GuiFocus.SystemMap
                or GuiFocus.Orrery;
            var inOrbit = status.HasLatitudeLongitude
                && (status.Flags.HasFlag(StatusFlags.Supercruise)
                    || status.GlideMode);
            var atSurface = status.HasLatitudeLongitude
                && !status.Flags.HasFlag(StatusFlags.Supercruise)
                && !status.GlideMode
                && status.HudInAnalysisMode
                && (status.InMainShip || status.Landed || status.InSrv);
            return status.GuiFocus == GuiFocus.Saa
                || inSystemMap
                    && ShowBodyInfoInSystemMap
                    && !ShowFssInfoInSystemMap
                || inOrbit && ShowBodyInfoInOrbit
                || atSurface && ShowBodyInfoAtSurface;
        }
    }

    public bool ShouldShowSystemStatus
    {
        get
        {
            if (!AutoShowSystemStatus
                || status is null
                || status.InTaxi
                || snapshot.SystemAddress is null
                || !snapshot.HasDiscoveryScan)
            {
                return false;
            }

            return status.Flags.HasFlag(StatusFlags.Supercruise)
                || status.GuiFocus is GuiFocus.Saa
                    or GuiFocus.Fss
                    or GuiFocus.ExternalPanel
                    or GuiFocus.Orrery
                    or GuiFocus.SystemMap;
        }
    }

    public bool ShouldShowFlightWarning
    {
        get
        {
            var body = ResolveBodyInfoTarget(preferDestination: false)?.Body;
            if (!AutoShowFlightWarnings
                || status is null
                || status.GuiFocus != GuiFocus.NoFocus
                || body?.IsLandable != true
                || body.SurfaceGravity / 10d < HighGravityWarningLevel)
            {
                return false;
            }

            return status.Landed
                || status.Flags.HasFlag(StatusFlags.Supercruise)
                || status.GlideMode
                || status.InSrv
                || status.InFighter
                || status.InMainShip && !status.Docked && !status.InTaxi;
        }
    }

    public string FlightWarningText
    {
        get
        {
            var body = ResolveBodyInfoTarget(preferDestination: false)?.Body;
            return body is null
                ? "HIGH-GRAVITY BODY"
                : $"WARNING: SURFACE GRAVITY {body.SurfaceGravity / 10d:N2} g";
        }
    }

    public bool ShouldShowBioSystem
    {
        get
        {
            if (!AutoShowBioSystem
                || AreBiologyOverlaysSuppressedForRepeatVisit
                || BiologySurvey is null
                || status is null
                || status.InTaxi
                || fsdJumping)
            {
                return false;
            }

            var overviewMode = status.Flags.HasFlag(StatusFlags.Supercruise)
                || status.GuiFocus is GuiFocus.Saa
                    or GuiFocus.Fss
                    or GuiFocus.ExternalPanel
                    or GuiFocus.Orrery
                    or GuiFocus.SystemMap;
            var localBodyMode = BiologySurvey.IsBodyDetail
                && (status.GlideMode
                    || status.InMainShip
                    || status.Landed
                    || status.InSrv
                    || status.OnFoot
                    || status.GuiFocus is GuiFocus.CommsPanel
                        or GuiFocus.RolePanel
                        or GuiFocus.Codex);
            return overviewMode || localBodyMode;
        }
    }

    public bool ShouldShowBioStatus
    {
        get
        {
            if ((!AutoShowBioStatus && !IsWithinPostDssBiologyWindow)
                || BiologyStatus is null
                || status is null
                || status.Docked
                || status.InTaxi
                || status.FsdChargingJump
                || fsdJumping)
            {
                return false;
            }

            var allowedFocus = status.GuiFocus is GuiFocus.NoFocus
                or GuiFocus.CommsPanel
                or GuiFocus.Saa
                or GuiFocus.Codex;
            var allowedMode = status.Flags.HasFlag(StatusFlags.Supercruise)
                || status.InMainShip
                || status.Landed
                || status.InSrv
                || status.OnFoot
                || status.GlideMode;
            return allowedFocus && allowedMode;
        }
    }

    public bool ShouldLoadPriorScans
    {
        get
        {
            if (!UseExternalData
                || !AutoShowPriorScans
                || AreBiologyOverlaysSuppressedForRepeatVisit
                || status is null
                || !status.HasLatitudeLongitude
                || status.PlanetRadius <= 0
                || string.IsNullOrWhiteSpace(status.BodyName)
                || string.IsNullOrWhiteSpace(snapshot.SystemName)
                || status.Docked
                || status.InTaxi
                || status.FsdChargingJump
                || fsdJumping)
            {
                return false;
            }

            var allowedFocus = status.GuiFocus is GuiFocus.NoFocus
                or GuiFocus.CommsPanel
                or GuiFocus.Saa
                or GuiFocus.Codex;
            var allowedMode = status.Flags.HasFlag(StatusFlags.Supercruise)
                || status.InMainShip
                || status.Landed
                || status.InSrv
                || status.OnFoot
                || status.GlideMode
                || status.InFighter;
            return allowedFocus && allowedMode;
        }
    }

    public void ApplyUpdate(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        EliteStatus? nextStatus,
        ExobiologySnapshot? nextExobiology = null)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        var previousAddress = snapshot.SystemAddress;
        var previousStatus = status;
        foreach (var journalEvent in journalEvents)
        {
            if (status?.GuiFocus == GuiFocus.Fss
                && FssTuningDetectorEnabled
                && journalEvent.EventName == "Scan"
                && !HasRingParent(journalEvent.Payload))
            {
                ApplyFssTuningScan();
            }

            state.Apply(journalEvent);
            switch (journalEvent.EventName)
            {
                case "StartJump" when GetString(
                    journalEvent.Payload,
                    "JumpType") == "Hyperspace":
                    fsdJumping = true;
                    break;

                case "FSDJump":
                case "CarrierJump":
                    fsdJumping = false;
                    break;

                case "SAAScanComplete":
                    lastDssCompletedAt = journalEvent.Timestamp ?? utcNow();
                    dssVisibilityWindowWasActive =
                        IsWithinPostDssBiologyWindow;
                    OnPropertyChanged(nameof(IsWithinPostDssBiologyWindow));
                    break;
            }
        }

        if (nextStatus is not null)
        {
            if (status is not null && status.GuiFocus != nextStatus.GuiFocus)
            {
                manuallyHideFssInfo = false;
                manuallyHideBodyInfo = false;
            }

            if (status is not null
                && (!string.Equals(
                        status.BodyName,
                        nextStatus.BodyName,
                        StringComparison.OrdinalIgnoreCase)
                    || status.Destination?.System
                        != nextStatus.Destination?.System
                    || status.Destination?.Body
                        != nextStatus.Destination?.Body))
            {
                manuallyHideBodyInfo = false;
            }

            status = nextStatus;
            if (previousStatus?.GuiFocus == GuiFocus.Fss
                && nextStatus.GuiFocus != GuiFocus.Fss)
            {
                ResetFssTuningDetection();
            }
        }

        if (nextExobiology is not null)
        {
            exobiology = nextExobiology;
        }

        snapshot = state.CreateSnapshot();
        if (snapshot.SystemAddress != previousAddress)
        {
            suppressBiologyOverlaysForRepeatVisit = false;
            ClearTimedBiologySelection(refreshDisplay: false);
            biologyDiscoveryContext = BiologyDiscoveryContext.Unavailable;
            canonnBiologyBodyIds = new HashSet<int>();
            biologyCodexNotification = null;
            forceShowFssInfo = false;
            manuallyHideFssInfo = false;
            forceShowBodyInfo = false;
            manuallyHideBodyInfo = false;
            ResetFssTuningDetection();
            OnPropertyChanged(nameof(IsBodyInfoForced));
            OnPropertyChanged(
                nameof(AreBiologyOverlaysSuppressedForRepeatVisit));
        }

        UpdateTimedBiologySelection(previousStatus, nextStatus);

        foreach (var journalEvent in journalEvents)
        {
            ApplyBiologyCodexCue(journalEvent);
        }

        RefreshDisplay();
        RaiseVisibilityProperties();
    }

    public void UpdateCommanderCodexContext(
        CommanderCodexData? global,
        CommanderCodexData? regional)
    {
        biologyDiscoveryContext = snapshot.SystemAddress is { } systemAddress
            ? new BiologyDiscoveryContext(systemAddress, global, regional)
            : BiologyDiscoveryContext.Unavailable;
        OnPropertyChanged(nameof(CurrentBiologyDiscoveryContext));
        RefreshDisplay();
    }

    public void SetRepeatVisitBiologySuppression(bool suppress)
    {
        if (!SetField(
                ref suppressBiologyOverlaysForRepeatVisit,
                suppress,
                nameof(AreBiologyOverlaysSuppressedForRepeatVisit)))
        {
            return;
        }

        RaiseVisibilityProperties();
    }

    public void UpdateCanonnSystemPoi(CanonnSystemPoiResult? result)
    {
        if (result is null
            || string.IsNullOrWhiteSpace(snapshot.SystemName)
            || !string.Equals(
                result.SystemName,
                snapshot.SystemName,
                StringComparison.OrdinalIgnoreCase))
        {
            canonnBiologyBodyIds = new HashSet<int>();
        }
        else
        {
            canonnBiologyBodyIds = snapshot.Bodies
                .Where(body => result.Signals.Any(signal =>
                    IsMatchingCanonnBody(body, signal.BodyName)))
                .Select(body => body.BodyId)
                .ToHashSet();
        }

        OnPropertyChanged(nameof(HasCanonnBiologyHint));
        OnPropertyChanged(nameof(CanonnBiologyHint));
    }

    public bool RefreshTransientState()
    {
        var changed = false;
        if (dssVisibilityWindowWasActive
            && !IsWithinPostDssBiologyWindow)
        {
            dssVisibilityWindowWasActive = false;
            OnPropertyChanged(nameof(IsWithinPostDssBiologyWindow));
            RaiseVisibilityProperties();
            changed = true;
        }

        if (timedBiologyBodyId is not null)
        {
            if (!IsBiologyMapMode(status) || utcNow() >= timedBiologyExpiresAt)
            {
                ClearTimedBiologySelection(refreshDisplay: true);
                RaiseVisibilityProperties();
                changed = true;
            }
            else
            {
                OnPropertyChanged(nameof(TimedBiologySelectionProgressPercent));
            }
        }

        if (FssTuningState != FssTuningDetectionState.None)
        {
            OnPropertyChanged(nameof(FssTuningIndicator));
            OnPropertyChanged(nameof(HasFssTuningIndicator));
        }

        return changed;
    }

    public FssTuningCaptureRequest? CreateFssTuningCaptureRequest()
    {
        if (!IsFssTuningDetectionPending
            || !ShouldShowLastFssBody
            || !snapshot.HasDiscoveryScan && snapshot.IsFssComplete)
        {
            return null;
        }

        return new FssTuningCaptureRequest(
            fssTuningRevision,
            FssTuningState,
            FssTuningDetector);
    }

    public void ApplyFssTuningAnalysis(
        long revision,
        FssTuningAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        if (revision != fssTuningRevision || !IsFssTuningDetectionPending)
        {
            return;
        }

        if (fssTuningState != analysis.State)
        {
            fssTuningState = analysis.State;
            RaiseFssTuningProperties();
        }
    }

    public void UpdateFssTuningDetectorStatus(string? value)
    {
        FssTuningDetectorStatus = value?.Trim() ?? string.Empty;
    }

    public bool ToggleFssInfoVisibility()
    {
        if (snapshot.SystemAddress is null || !AutoShowFssInfo)
        {
            return false;
        }

        if (!ShouldShowFssInfo)
        {
            manuallyHideFssInfo = false;
            forceShowFssInfo = true;
        }
        else if (forceShowFssInfo)
        {
            forceShowFssInfo = false;
            manuallyHideFssInfo = true;
        }
        else
        {
            manuallyHideFssInfo = true;
        }

        OnPropertyChanged(nameof(IsFssInfoForced));
        RaiseVisibilityProperties();
        return true;
    }

    public bool ToggleBodyInfoVisibility()
    {
        if (!AutoShowBodyInfo
            || ResolveBodyInfoTarget(preferDestination: true) is null)
        {
            forceShowBodyInfo = false;
            manuallyHideBodyInfo = false;
            OnPropertyChanged(nameof(IsBodyInfoForced));
            RaiseVisibilityProperties();
            return false;
        }

        if (!ShouldShowBodyInfo)
        {
            manuallyHideBodyInfo = false;
            forceShowBodyInfo = true;
        }
        else if (forceShowBodyInfo)
        {
            forceShowBodyInfo = false;
            manuallyHideBodyInfo = true;
        }
        else
        {
            manuallyHideBodyInfo = true;
        }

        RefreshDisplay();
        OnPropertyChanged(nameof(IsBodyInfoForced));
        RaiseVisibilityProperties();
        return true;
    }

    private void RefreshDisplay()
    {
        BiologySurvey = timedBiologyBodyId is { } selectedBodyId
            && IsBiologyMapMode(status)
            && utcNow() < timedBiologyExpiresAt
                ? BiologySurveyViewModel.CreateBodyDetail(
                    snapshot,
                    selectedBodyId,
                    exobiology,
                    HighlightRegionalFirsts,
                    DimAnalyzedOrganisms,
                    HideGeoCountInBioSystem,
                    DisableBioPredictions,
                    biologyDiscoveryContext)
                : BiologySurveyViewModel.Create(
                    snapshot,
                    status,
                    exobiology,
                    DrawBodyBiosOnlyWhenNear,
                    HighlightRegionalFirsts,
                    DimAnalyzedOrganisms,
                    HideGeoCountInBioSystem,
                    DisableBioPredictions,
                    biologyDiscoveryContext);
        BiologyStatus = BiologyStatusViewModel.Create(
            snapshot,
            status,
            exobiology,
            HideGeoCountInBioSystem,
            biologyCodexNotification,
            ShowTemperatureRangeDebug);
        BodyInformation = CreateBodyInformation(
            ResolveBodyInfoTarget(forceShowBodyInfo
                || status?.GuiFocus is GuiFocus.SystemMap or GuiFocus.Orrery));
        FssBodies = snapshot.Bodies
            .Where(IsInterestingFssBody)
            .OrderByDescending(body => body.ScanSequence)
            .ThenBy(body => body.BodyId)
            .Select(CreateFssBodyRow)
            .ToArray();

        var destination = GetDestinationShortName();
        DssBodies = CreateDssCandidates()
            .Select(name => CreateBodyReference(name, destination))
            .ToArray();
        BiologicalBodies = snapshot.Bodies
            .Where(body => body.AnalyzedBiologicalSignalCount
                < body.BiologicalSignalCount)
            .OrderBy(body => body.BodyId)
            .Select(body => CreateBodyReference(body.ShortName, destination))
            .ToArray();

        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(LatestBiologyEntryId));
        OnPropertyChanged(nameof(SystemTitle));
        OnPropertyChanged(nameof(ScanSummary));
        OnPropertyChanged(nameof(FssFilterDescription));
        OnPropertyChanged(nameof(FssEmptyText));
        OnPropertyChanged(nameof(LastFssBody));
        OnPropertyChanged(nameof(HasLastFssBody));
        OnPropertyChanged(nameof(LastFssBodyName));
        OnPropertyChanged(nameof(LastFssBodyClass));
        OnPropertyChanged(nameof(LastFssBodyDistance));
        OnPropertyChanged(nameof(LastFssScanValue));
        OnPropertyChanged(nameof(LastFssMappedValue));
        OnPropertyChanged(nameof(LastFssMarkers));
        OnPropertyChanged(nameof(HasLastFssMarkers));
        OnPropertyChanged(nameof(LastFssSignalsText));
        OnPropertyChanged(nameof(HasLastFssSignals));
        OnPropertyChanged(nameof(SystemStatusText));
        OnPropertyChanged(nameof(BiologicalHeading));
        OnPropertyChanged(nameof(HasNonBodySignals));
        OnPropertyChanged(nameof(NonBodySignalsText));
        OnPropertyChanged(nameof(IsWithinBodyInfoBubble));
        OnPropertyChanged(nameof(ShouldShowFlightWarning));
        OnPropertyChanged(nameof(FlightWarningText));
        OnPropertyChanged(nameof(HasTimedBiologySelection));
        OnPropertyChanged(nameof(TimedBiologySelectionProgressPercent));
        OnPropertyChanged(nameof(HasCanonnBiologyHint));
        OnPropertyChanged(nameof(CanonnBiologyHint));
    }

    private static bool IsMatchingCanonnBody(
        SystemScanBodySnapshot body,
        string bodyName)
    {
        var normalized = bodyName.Trim();
        return normalized.Length > 0
            && (string.Equals(
                    body.ShortName,
                    normalized,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    body.Name,
                    normalized,
                    StringComparison.OrdinalIgnoreCase)
                || body.Name.EndsWith(
                    " " + normalized,
                    StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateTimedBiologySelection(
        EliteStatus? previousStatus,
        EliteStatus? nextStatus)
    {
        if (!IsBiologyMapMode(nextStatus))
        {
            ClearTimedBiologySelection(refreshDisplay: false);
            return;
        }

        if (previousStatus is null
            || string.Equals(
                previousStatus.Destination?.Name,
                nextStatus?.Destination?.Name,
                StringComparison.Ordinal))
        {
            return;
        }

        var destination = nextStatus?.Destination;
        var body = destination is not null
            && destination.System == snapshot.SystemAddress
                ? snapshot.Bodies.FirstOrDefault(candidate =>
                    candidate.BodyId == destination.Body
                    && candidate.BiologicalSignalCount > 0)
                : null;
        if (body is null)
        {
            ClearTimedBiologySelection(refreshDisplay: false);
            return;
        }

        var details = BiologySurveyViewModel.CreateBodyDetail(
            snapshot,
            body.BodyId,
            exobiology,
            HighlightRegionalFirsts,
            DimAnalyzedOrganisms,
            HideGeoCountInBioSystem,
            DisableBioPredictions,
            biologyDiscoveryContext);
        var signalCount = Math.Max(
            1,
            details?.Organisms.Count ?? body.BiologicalSignalCount);
        timedBiologyBodyId = body.BodyId;
        timedBiologyStartedAt = utcNow();
        timedBiologyExpiresAt = timedBiologyStartedAt
            + TimeSpan.FromSeconds(2 * signalCount);
    }

    private void ClearTimedBiologySelection(bool refreshDisplay)
    {
        if (timedBiologyBodyId is null)
        {
            return;
        }

        timedBiologyBodyId = null;
        timedBiologyStartedAt = default;
        timedBiologyExpiresAt = default;
        if (refreshDisplay)
        {
            RefreshDisplay();
        }
        else
        {
            OnPropertyChanged(nameof(HasTimedBiologySelection));
            OnPropertyChanged(nameof(TimedBiologySelectionProgressPercent));
        }
    }

    private static bool IsBiologyMapMode(EliteStatus? value)
    {
        return value?.GuiFocus is GuiFocus.ExternalPanel
            or GuiFocus.SystemMap
            or GuiFocus.Orrery;
    }

    private void ApplyBiologyCodexCue(JournalEventEnvelope journalEvent)
    {
        var root = journalEvent.Payload;
        if (journalEvent.EventName == "ScanOrganic")
        {
            biologyCodexNotification = null;
            if (biologyCatalog.FindByVariant(GetString(root, "Variant"))
                is { IsBiology: true } scanned)
            {
                latestBiologyEntryId = scanned.EntryId;
            }

            return;
        }

        if (journalEvent.EventName != "CodexEntry"
            || !string.Equals(
                GetString(root, "SubCategory"),
                OrganicCodexCategory,
                StringComparison.Ordinal)
            || GetInt64(root, "EntryID") is not { } entryId
            || biologyCatalog.FindByEntryId(entryId)
                is not { IsBiology: true } reference)
        {
            return;
        }

        latestBiologyEntryId = reference.EntryId;
        if (status?.HasLatitudeLongitude != true)
        {
            return;
        }

        var bodyId = GetInt64(root, "BodyID") is { } parsedBodyId
            && parsedBodyId is >= int.MinValue and <= int.MaxValue
                ? (int)parsedBodyId
                : snapshot.CurrentBodyId;
        if (bodyId is null)
        {
            return;
        }

        var body = snapshot.Bodies.FirstOrDefault(candidate =>
            candidate.BodyId == bodyId);
        var isFirstFootfall = body?.IsFirstFootfall == true;
        var reward = isFirstFootfall
            ? reference.Reward * 5
            : reference.Reward;
        biologyCodexNotification = new BiologyCodexNotificationViewModel(
            reference.EntryId,
            bodyId.Value,
            GetString(root, "Name_Localised")
                ?? reference.DisplayName
                ?? reference.VariantName,
            reward,
            isFirstFootfall,
            !string.IsNullOrWhiteSpace(reference.ImageUrl));
    }

    private BodyInformationViewModel? CreateBodyInformation(
        BodyInfoTarget? target)
    {
        if (target is null)
        {
            return null;
        }

        var body = target.Body;
        if (body is null || body.Kind is SystemBodyKind.Unknown
            or SystemBodyKind.Barycentre)
        {
            return BodyInformationViewModel.ScanRequired(
                target.BodyId,
                target.Name);
        }

        var planetish = body.Kind is not SystemBodyKind.Star
            and not SystemBodyKind.Asteroid
            and not SystemBodyKind.Ring;
        var markers = new List<string>();
        if (body.IsTerraformable || body.IsEarthLike)
        {
            markers.Add("TERRAFORMABLE");
        }

        if (!body.WasDiscovered && !body.WasMapped)
        {
            markers.Add("UNDISCOVERED");
        }
        else if (!body.WasMapped && body.IsDssComplete)
        {
            markers.Add("FIRST MAPPED");
        }
        else if (!body.WasMapped && !IsWithinBodyInfoBubble)
        {
            markers.Add("UNMAPPED");
        }

        var atmosphere = FormatAtmosphere(body);
        var atmosphereComposition = body.AtmosphereComposition
            .OrderByDescending(entry => entry.Value)
            .Select(entry => new BodyCompositionRowViewModel(
                FormatIdentifier(entry.Key),
                $"{entry.Value:N2}%",
                false))
            .ToArray();
        var materials = HideBodyInfoMaterials
            ? []
            : body.Materials
                .OrderByDescending(entry => entry.Value)
                .Select(entry => new BodyCompositionRowViewModel(
                    FormatIdentifier(entry.Key),
                    $"{entry.Value:N2}%",
                    IsRareMaterial(entry.Key)))
                .ToArray();
        var rings = body.Rings
            .Select(ring => new BodyRingRowViewModel(
                GetRingName(body.Name, ring.Name),
                FormatRingClass(ring.RingClass)))
            .ToArray();
        var gravity = body.SurfaceGravity / 10d;
        var biologyReward = body.BiologicalSignalCount > 0
            ? BiologySurveyViewModel.CreateBodyDetail(
                    snapshot,
                    body.BodyId,
                    exobiology,
                    HighlightRegionalFirsts,
                    DimAnalyzedOrganisms,
                    HideGeoCountInBioSystem,
                    DisableBioPredictions,
                    biologyDiscoveryContext)
                ?.RewardSummary ?? string.Empty
            : string.Empty;

        return new BodyInformationViewModel(
            body.BodyId,
            body.WasDiscovered ? body.Name : "⚑ " + body.Name,
            body.Kind == SystemBodyKind.Star
                ? $"{body.StarClass ?? "Unknown"} star"
                : body.PlanetClass ?? "Unknown body",
            $"{body.DistanceFromArrivalLs:N0} LS",
            string.Join(" · ", markers),
            body.IsDssComplete
                ? "✓ " + FormatCredits(body.CurrentScanValue)
                : FormatCredits(body.ScanValue),
            !body.IsDssComplete && planetish
                ? FormatCredits(body.EstimatedMappedValue)
                : string.Empty,
            $"{body.SurfaceTemperature:N0} K",
            $"{gravity:N3} g",
            gravity >= HighGravityWarningLevel,
            HighlightDssCandidates
                && (body.IsDssComplete
                    ? body.CurrentScanValue
                    : planetish
                        ? body.EstimatedMappedValue
                        : body.ScanValue) > DssValueFloor,
            body.SurfacePressure <= 0
                ? "None"
                : $"{body.SurfacePressure / 100_000d:N4} bar",
            planetish,
            FormatSignalCount(body.BiologicalSignalCount, "biological"),
            biologyReward,
            FormatSignalCount(body.GeologicalSignalCount, "geological"),
            planetish && body.Kind != SystemBodyKind.GasGiant
                ? FormatVolcanism(body.Volcanism)
                : string.Empty,
            atmosphere,
            atmosphereComposition,
            materials,
            rings,
            false);
    }

    private BodyInfoTarget? ResolveBodyInfoTarget(bool preferDestination)
    {
        if (snapshot.SystemAddress is null)
        {
            return null;
        }

        if (preferDestination
            && status?.Destination is { } destination
            && destination.System == snapshot.SystemAddress)
        {
            var body = snapshot.Bodies.FirstOrDefault(candidate =>
                candidate.BodyId == destination.Body);
            var name = body?.Name
                ?? destination.NameLocalised
                ?? destination.Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return new BodyInfoTarget(destination.Body, name, body);
            }
        }

        var currentBody = !string.IsNullOrWhiteSpace(status?.BodyName)
            ? snapshot.Bodies.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Name,
                    status.BodyName,
                    StringComparison.OrdinalIgnoreCase))
            : null;
        currentBody ??= snapshot.CurrentBodyId is { } currentBodyId
            ? snapshot.Bodies.FirstOrDefault(candidate =>
                candidate.BodyId == currentBodyId)
            : null;
        if (currentBody is not null)
        {
            return new BodyInfoTarget(
                currentBody.BodyId,
                currentBody.Name,
                currentBody);
        }

        if (!string.IsNullOrWhiteSpace(status?.BodyName))
        {
            return new BodyInfoTarget(-1, status.BodyName, null);
        }

        return !preferDestination
            ? ResolveBodyInfoTarget(preferDestination: true)
            : null;
    }

    private static string FormatAtmosphere(SystemScanBodySnapshot body)
    {
        if (string.Equals(
                body.AtmosphereType,
                "EarthLike",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Earth-like";
        }

        if (string.IsNullOrWhiteSpace(body.Atmosphere)
            || string.Equals(
                body.Atmosphere,
                "No atmosphere",
                StringComparison.OrdinalIgnoreCase))
        {
            return body.IsLandable ? "None" : string.Empty;
        }

        return FormatIdentifier(body.Atmosphere.Replace(
            " atmosphere",
            string.Empty,
            StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatVolcanism(string? volcanism)
    {
        return string.IsNullOrWhiteSpace(volcanism)
            || string.Equals(
                volcanism,
                "No volcanism",
                StringComparison.OrdinalIgnoreCase)
                    ? "None"
                    : FormatIdentifier(volcanism.Replace(
                        " volcanism",
                        string.Empty,
                        StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Replace('_', ' ').Trim();
        var output = new System.Text.StringBuilder(normalized.Length + 8);
        for (var index = 0; index < normalized.Length; index++)
        {
            var character = normalized[index];
            if (index > 0
                && char.IsUpper(character)
                && char.IsLower(normalized[index - 1]))
            {
                output.Append(' ');
            }

            output.Append(character);
        }

        var result = output.ToString();
        return char.ToUpperInvariant(result[0]) + result[1..];
    }

    private static string FormatSignalCount(int count, string kind)
    {
        return count switch
        {
            <= 0 => string.Empty,
            1 => $"1 {kind} signal",
            _ => $"{count:N0} {kind} signals",
        };
    }

    private static string GetRingName(string bodyName, string ringName)
    {
        var suffix = ringName.StartsWith(
            bodyName,
            StringComparison.OrdinalIgnoreCase)
                ? ringName[bodyName.Length..].Trim()
                : ringName;
        return string.IsNullOrWhiteSpace(suffix)
            ? "Ring"
            : suffix.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
    }

    private static string FormatRingClass(string? ringClass)
    {
        if (string.IsNullOrWhiteSpace(ringClass))
        {
            return "Unknown";
        }

        return FormatIdentifier(ringClass.Replace(
            "eRingClass_",
            string.Empty,
            StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRareMaterial(string name)
    {
        return name.ToLowerInvariant() is "antimony"
            or "cadmium"
            or "mercury"
            or "molybdenum"
            or "niobium"
            or "polonium"
            or "ruthenium"
            or "technetium"
            or "tellurium"
            or "tin"
            or "tungsten"
            or "yttrium";
    }

    private bool IsInterestingFssBody(SystemScanBodySnapshot body)
    {
        if (!body.IsScanned
            || body.Kind is SystemBodyKind.Asteroid
                or SystemBodyKind.Ring
                or SystemBodyKind.Barycentre
                or SystemBodyKind.Unknown)
        {
            return false;
        }

        var valuableClass = body.IsTerraformable
            || body.PlanetClass?.StartsWith("Water ", StringComparison.Ordinal) == true
            || body.PlanetClass?.StartsWith("Ammonia ", StringComparison.Ordinal) == true
            || body.IsEarthLike;
        return valuableClass
            || body.BiologicalSignalCount > 0
            || !HideGeoCount && body.GeologicalSignalCount > 0
            || Math.Max(body.ScanValue, body.EstimatedMappedValue)
                >= FssBodyValueFloor;
    }

    private FssBodyRowViewModel CreateFssBodyRow(SystemScanBodySnapshot body)
    {
        var dssWorthy = HighlightDssCandidates
            && body.EstimatedMappedValue > DssValueFloor
            && !(SkipDistantDssCandidates
                && body.DistanceFromArrivalLs > DssDistanceLimitLs)
            && !(SkipGasGiantsForDss && body.Kind == SystemBodyKind.GasGiant)
            && body.Kind != SystemBodyKind.Star;
        var className = body.Kind == SystemBodyKind.Star
            ? $"{body.StarClass ?? "Unknown"} star"
            : (body.PlanetClass ?? "Unknown body")
                .Replace("Sudarsky class", "Class", StringComparison.Ordinal);
        var markers = new List<string>();
        if (body.IsTerraformable || body.IsEarthLike)
        {
            markers.Add("TERRAFORMABLE");
        }

        if (body.IsLandable)
        {
            markers.Add("LANDABLE");
        }

        if (body.IsFirstFootfall)
        {
            markers.Add("FIRST FOOTFALL");
        }

        return new FssBodyRowViewModel(
            body.WasDiscovered ? body.ShortName : "⚑ " + body.ShortName,
            className,
            string.Join(" · ", markers),
            body.IsDssComplete
                ? $"✓ {FormatCredits(body.CurrentScanValue)}"
                : FormatCredits(body.ScanValue),
            body.Kind != SystemBodyKind.Star && !body.IsDssComplete
                ? FormatCredits(body.EstimatedMappedValue)
                : string.Empty,
            body.BiologicalSignalCount,
            body.AnalyzedBiologicalSignalCount,
            HideGeoCount ? 0 : body.GeologicalSignalCount,
            HideGeoCount ? 0 : body.AnalyzedGeologicalSignalCount,
            dssWorthy || body.BiologicalSignalCount > 0,
            dssWorthy);
    }

    private IEnumerable<string> CreateDssCandidates()
    {
        var knownRingBodies = snapshot.Bodies.ToDictionary(
            body => body.Name,
            StringComparer.Ordinal);
        foreach (var body in snapshot.Bodies.OrderBy(body => body.BodyId))
        {
            if (body.IsDssComplete || !body.IsMappable)
            {
                continue;
            }

            if (!SkipRingsForDss)
            {
                for (var index = 0; index < body.Rings.Count; index++)
                {
                    var ring = body.Rings[index];
                    if (!knownRingBodies.TryGetValue(ring.Name, out var ringBody)
                        || !ringBody.IsDssComplete)
                    {
                        yield return body.ShortName + "r" + (char)('A' + index);
                    }
                }
            }

            if (SkipGasGiantsForDss && body.Kind == SystemBodyKind.GasGiant)
            {
                continue;
            }

            if (HighlightDssCandidates
                && body.EstimatedMappedValue < DssValueFloor)
            {
                continue;
            }

            if (SkipDistantDssCandidates
                && body.DistanceFromArrivalLs > DssDistanceLimitLs)
            {
                continue;
            }

            yield return body.ShortName;
        }
    }

    private string? GetDestinationShortName()
    {
        var name = status?.Destination?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.SystemName))
        {
            name = name.Replace(
                snapshot.SystemName,
                string.Empty,
                StringComparison.Ordinal);
        }

        return name.Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static SurveyBodyReferenceViewModel CreateBodyReference(
        string name,
        string? destination)
    {
        return new SurveyBodyReferenceViewModel(
            name,
            string.Equals(name, destination, StringComparison.Ordinal),
            string.IsNullOrWhiteSpace(destination)
                || name.Length > 0
                    && destination.Length > 0
                    && name[0] == destination[0]);
    }

    private bool SetPreference<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (!SetField(ref field, value, propertyName))
        {
            return false;
        }

        SavePreferences();
        RaiseVisibilityProperties();
        return true;
    }

    private void SavePreferences()
    {
        try
        {
            settingsStore.Save(new SystemSurveyPreferences(
                AutoShowBodyInfo,
                ShowBodyInfoInSystemMap,
                ShowBodyInfoInOrbit,
                ShowBodyInfoAtSurface,
                HideBodyInfoInBubble,
                BodyInfoBubbleSizeLy,
                HideBodyInfoMaterials,
                AutoShowFlightWarnings,
                HighGravityWarningLevel,
                UseExternalData,
                UseExternalBioData,
                AutoShowBioSystem,
                AutoShowBioStatus,
                AutoHideBioPlotOnRepeat,
                KeepBioPlottersVisibleAfterDss,
                BioPlotterDssDurationSeconds,
                AutoShowPriorScans,
                SkipPriorScansLowValue,
                PriorScanMinimumValue,
                HideOwnCanonnSignals,
                ShowCanonnSignalsOnRadar,
                UseSmallCanonnRadarCircles,
                AutoShowSurfaceRadar,
                AutoShowMiniTrack,
                SurfaceRadarSize,
                AutoHideSurfaceRadarWithoutLandingGear,
                AutoRemoveTrackerOnSampling,
                AutoRemoveTrackerOnFinalSample,
                AutoTrackCompositionScans,
                SkipAnalyzedCompositionScans,
                DrawBodyBiosOnlyWhenNear,
                HighlightRegionalFirsts,
                DimAnalyzedOrganisms,
                HideGeoCountInBioSystem,
                DisableBioPredictions,
                ShowTemperatureRangeDebug,
                AutoShowLastFssBody,
                AutoShowFssInfo,
                ShowFssInfoInSystemMap,
                ShowFssInfoInNavigationPanel,
                AutoShowSystemStatus,
                HideGeoCount,
                FssBodyValueFloor,
                HighlightDssCandidates,
                DssValueFloor,
                SkipDistantDssCandidates,
                DssDistanceLimitLs,
                SkipGasGiantsForDss,
                SkipRingsForDss,
                ShowNonBodySignals,
                FssTuningDetector));
            SettingsStatus = string.Empty;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            SettingsStatus = "The system-survey preference changed for this "
                + "session but could not be saved: "
                + exception.Message;
        }
    }

    private void SetFssTuningDetectorPreference(
        FssTuningDetectorSettings value)
    {
        if (fssTuningDetector == value)
        {
            return;
        }

        fssTuningDetector = value;
        OnPropertyChanged(nameof(FssTuningDetector));
        OnPropertyChanged(nameof(FssTuningDetectorEnabled));
        OnPropertyChanged(nameof(SaveFssTuningDiagnosticImages));
        OnPropertyChanged(nameof(HasFssTuningDetectorStatus));
        if (!value.Enabled)
        {
            ResetFssTuningDetection();
        }

        SavePreferences();
        RaiseVisibilityProperties();
    }

    private void ApplyFssTuningScan()
    {
        lastFssTuningScanAt = utcNow();
        fssTuningState = FssTuningState switch
        {
            FssTuningDetectionState.Waiting =>
                FssTuningDetectionState.Skipped,
            FssTuningDetectionState.Skipped =>
                FssTuningDetectionState.Skipped,
            _ => FssTuningDetectionState.Waiting,
        };
        fssTuningRevision++;
        RaiseFssTuningProperties();
    }

    private void ResetFssTuningDetection()
    {
        if (fssTuningState == FssTuningDetectionState.None
            && lastFssTuningScanAt == default)
        {
            return;
        }

        fssTuningState = FssTuningDetectionState.None;
        lastFssTuningScanAt = default;
        fssTuningRevision++;
        RaiseFssTuningProperties();
    }

    private void RaiseFssTuningProperties()
    {
        OnPropertyChanged(nameof(FssTuningState));
        OnPropertyChanged(nameof(IsFssTuningDetectionPending));
        OnPropertyChanged(nameof(FssTuningIndicator));
        OnPropertyChanged(nameof(HasFssTuningIndicator));
    }

    private void RaiseVisibilityProperties()
    {
        OnPropertyChanged(nameof(ShouldShowFssInfo));
        OnPropertyChanged(nameof(ShouldShowLastFssBody));
        OnPropertyChanged(nameof(ShouldShowBodyInfo));
        OnPropertyChanged(nameof(ShouldShowBioSystem));
        OnPropertyChanged(nameof(ShouldShowBioStatus));
        OnPropertyChanged(nameof(ShouldLoadPriorScans));
        OnPropertyChanged(nameof(ShouldShowSystemStatus));
        OnPropertyChanged(nameof(ShouldShowFlightWarning));
    }

    private static string FormatCredits(long value)
    {
        return value switch
        {
            >= 1_000_000 => $"{value / 1_000_000d:N2} M CR",
            >= 1_000 => $"{value / 1_000d:N1} K CR",
            _ => $"{value:N0} CR",
        };
    }

    private static string? GetString(
        System.Text.Json.JsonElement root,
        string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static bool HasRingParent(System.Text.Json.JsonElement root)
    {
        if (!root.TryGetProperty("Parents", out var parents)
            || parents.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return false;
        }

        foreach (var parent in parents.EnumerateArray())
        {
            if (parent.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in parent.EnumerateObject())
            {
                return string.Equals(
                    property.Name,
                    "Ring",
                    StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        return false;
    }

    private static long? GetInt64(
        System.Text.Json.JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == System.Text.Json.JsonValueKind.Number
            && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == System.Text.Json.JsonValueKind.String
            && long.TryParse(value.GetString(), out number)
                ? number
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

public sealed record FssTuningCaptureRequest(
    long Revision,
    FssTuningDetectionState State,
    FssTuningDetectorSettings Settings);

public sealed record BodyInformationViewModel(
    int BodyId,
    string Name,
    string BodyClass,
    string Distance,
    string Markers,
    string ScanValue,
    string MappedValue,
    string Temperature,
    string Gravity,
    bool IsHighGravity,
    bool IsHighValue,
    string Pressure,
    bool IsPlanet,
    string BiologicalSignals,
    string BiologicalReward,
    string GeologicalSignals,
    string Volcanism,
    string Atmosphere,
    IReadOnlyList<BodyCompositionRowViewModel> AtmosphereComposition,
    IReadOnlyList<BodyCompositionRowViewModel> Materials,
    IReadOnlyList<BodyRingRowViewModel> Rings,
    bool IsScanRequired)
{
    public bool HasMarkers => !string.IsNullOrWhiteSpace(Markers);

    public bool HasMappedValue => !string.IsNullOrWhiteSpace(MappedValue);

    public bool HasBiologicalSignals => !string.IsNullOrWhiteSpace(
        BiologicalSignals);

    public bool HasBiologicalReward => !string.IsNullOrWhiteSpace(
        BiologicalReward);

    public bool HasGeologicalSignals => !string.IsNullOrWhiteSpace(
        GeologicalSignals);

    public bool HasVolcanism => !string.IsNullOrWhiteSpace(Volcanism);

    public bool HasAtmosphere => !string.IsNullOrWhiteSpace(Atmosphere);

    public bool HasAtmosphereComposition => AtmosphereComposition.Count > 0;

    public bool HasMaterials => Materials.Count > 0;

    public bool HasRings => Rings.Count > 0;

    public static BodyInformationViewModel ScanRequired(int bodyId, string name)
    {
        return new BodyInformationViewModel(
            bodyId,
            name,
            "Detailed scan required",
            string.Empty,
            string.Empty,
            "—",
            string.Empty,
            string.Empty,
            string.Empty,
            false,
            false,
            string.Empty,
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            [],
            [],
            [],
            true);
    }
}

public sealed record BodyCompositionRowViewModel(
    string Name,
    string Value,
    bool IsRare);

public sealed record BodyRingRowViewModel(string Name, string RingClass)
{
    public string DisplayText => $"{Name} · {RingClass}";
}

internal sealed record BodyInfoTarget(
    int BodyId,
    string Name,
    SystemScanBodySnapshot? Body);

public sealed record FssBodyRowViewModel(
    string Name,
    string BodyClass,
    string Markers,
    string ScanValue,
    string DssValue,
    int BiologicalSignalCount,
    int AnalyzedBiologicalSignalCount,
    int GeologicalSignalCount,
    int AnalyzedGeologicalSignalCount,
    bool IsHighlighted,
    bool IsDssCandidate)
{
    public bool HasMarkers => !string.IsNullOrWhiteSpace(Markers);

    public bool HasDssValue => !string.IsNullOrWhiteSpace(DssValue);

    public bool HasBiologicalSignals => BiologicalSignalCount > 0;

    public bool HasGeologicalSignals => GeologicalSignalCount > 0;

    public bool AreBiologicalSignalsComplete => BiologicalSignalCount > 0
        && AnalyzedBiologicalSignalCount >= BiologicalSignalCount;

    public bool AreGeologicalSignalsComplete => GeologicalSignalCount > 0
        && AnalyzedGeologicalSignalCount >= GeologicalSignalCount;

    public string BiologicalSignalsText => BiologicalSignalCount == 1
        ? "1 GENUS"
        : $"{BiologicalSignalCount:N0} GENERA";

    public string GeologicalSignalsText => GeologicalSignalCount == 1
        ? "1 GEO"
        : $"{GeologicalSignalCount:N0} GEO";
}

public sealed record SurveyBodyReferenceViewModel(
    string Name,
    bool IsDestination,
    bool IsLocalGroup);
