using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class SystemSurveyViewModel : INotifyPropertyChanged
{
    private const int MaximumDisplayedFssBodies = 8;

    private readonly SystemSurveySettingsStore settingsStore;
    private readonly SystemScanState state;
    private EliteStatus? status;
    private SystemScanSnapshot snapshot = SystemScanSnapshot.Empty;
    private IReadOnlyList<FssBodyRowViewModel> fssBodies = [];
    private IReadOnlyList<SurveyBodyReferenceViewModel> dssBodies = [];
    private IReadOnlyList<SurveyBodyReferenceViewModel> biologicalBodies = [];
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
    private bool forceShowFssInfo;
    private bool manuallyHideFssInfo;
    private bool fsdJumping;
    private string settingsStatus = string.Empty;

    public SystemSurveyViewModel(
        SystemSurveySettingsStore settingsStore,
        SystemScanState? state = null)
    {
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        this.state = state ?? new SystemScanState();
        var preferences = settingsStore.Load();
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
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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

    public void ApplyUpdate(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        EliteStatus? nextStatus)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        var previousAddress = snapshot.SystemAddress;
        foreach (var journalEvent in journalEvents)
        {
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
            }
        }

        if (nextStatus is not null)
        {
            status = nextStatus;
        }

        snapshot = state.CreateSnapshot();
        if (snapshot.SystemAddress != previousAddress)
        {
            forceShowFssInfo = false;
            manuallyHideFssInfo = false;
        }

        RefreshDisplay();
        RaiseVisibilityProperties();
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

    private void RefreshDisplay()
    {
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
                ShowNonBodySignals));
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

    private void RaiseVisibilityProperties()
    {
        OnPropertyChanged(nameof(ShouldShowFssInfo));
        OnPropertyChanged(nameof(ShouldShowLastFssBody));
        OnPropertyChanged(nameof(ShouldShowSystemStatus));
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
