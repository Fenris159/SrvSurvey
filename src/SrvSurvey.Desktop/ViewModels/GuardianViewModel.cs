using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class GuardianViewModel : INotifyPropertyChanged
{
    private const string AllKinds = "All sites";
    private const string AllVisits = "All visits";
    private const string AllTypes = "All types";

    private readonly GuardianSiteCatalog references;
    private readonly GuardianPublishedSiteCatalog publishedSites;
    private readonly GuardianSiteTemplateCatalog templates;
    private readonly GuardianSurveyCompletionCalculator completionCalculator;
    private readonly GuardianSiteMapProjector mapProjector = new();
    private readonly GuardianSiteProximityEvaluator proximityEvaluator = new();
    private readonly GuardianArtifactInventoryState artifactInventory = new();
    private readonly GuardianCommanderDataReader commanderDataReader;
    private readonly GuardianCommanderSurveyStore commanderSurveyStore;
    private readonly RamTahViewModel? ramTah;
    private readonly AsyncCommand refreshCommand;
    private readonly AsyncCommand toggleCurrentObeliskScannedCommand;
    private GuardianLiveSiteState liveSiteState;
    private GuardianCommanderDataReadResult commanderData =
        GuardianCommanderDataReadResult.Empty;
    private GuardianSiteVisitCatalog visits;
    private IReadOnlyList<GuardianSiteRowViewModel> rows = [];
    private GuardianSiteMapProjection? mapProjection;
    private GuardianSiteProximitySnapshot? proximity;
    private EliteStatus? currentStatus;
    private string filterText = string.Empty;
    private string selectedKindFilter = AllKinds;
    private string selectedVisitFilter = AllVisits;
    private string selectedSiteTypeFilter = AllTypes;
    private GuardianSiteRowViewModel? selectedSite;
    private GalacticCoordinate? currentPosition;
    private string? currentSystemName;
    private string? activeFrontierId;
    private bool activeIsOdyssey = true;
    private bool isBusy;
    private string statusMessage;
    private string summary = string.Empty;
    private Func<string, Task>? clipboardWriter;

    public GuardianViewModel(
        string dataDirectory,
        GuardianSiteCatalog? references = null,
        GuardianPublishedSiteCatalog? publishedSites = null,
        GuardianSiteTemplateCatalog? templates = null,
        RamTahViewModel? ramTah = null)
    {
        this.references = references ?? GuardianSiteCatalog.LoadEmbedded();
        this.publishedSites = publishedSites
            ?? GuardianPublishedSiteCatalog.LoadEmbedded();
        this.templates = templates ?? GuardianSiteTemplateCatalog.LoadEmbedded();
        this.ramTah = ramTah;
        if (this.ramTah is not null)
        {
            this.ramTah.PropertyChanged += (_, _) => NotifyCurrentObeliskChanged();
        }
        completionCalculator = new GuardianSurveyCompletionCalculator(this.templates);
        commanderDataReader = new GuardianCommanderDataReader(dataDirectory);
        commanderSurveyStore = new GuardianCommanderSurveyStore(dataDirectory);
        SurveyEditor = new GuardianSurveyEditorViewModel(
            commanderSurveyStore,
            OnSurveySavedAsync);
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
        RefreshCommand = refreshCommand;
        ToggleCurrentObeliskScannedCommand = toggleCurrentObeliskScannedCommand;
        ApplyFilters();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<string> KindFilters { get; }

    public IReadOnlyList<string> VisitFilters { get; }

    public IReadOnlyList<string> SiteTypeFilters { get; }

    public ICommand RefreshCommand { get; }

    public ICommand ToggleCurrentObeliskScannedCommand { get; }

    public GuardianSurveyEditorViewModel SurveyEditor { get; }

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
                UpdateMapProjection();
                UpdateSurveyEditor();
            }
        }
    }

    public bool HasSelectedSite => SelectedSite is not null;

    public GuardianSiteMapProjection? MapProjection
    {
        get => mapProjection;
        private set => SetField(ref mapProjection, value);
    }

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

    public string ActiveSiteTitle => ActiveSite is { } site
        ? string.IsNullOrWhiteSpace(site.LocalizedName)
            ? site.Kind == GuardianSiteKind.Ruins
                ? $"Ancient Ruins ({site.Index})"
                : "Guardian Structure"
            : site.LocalizedName
        : "No live Guardian site detected";

    public string ActiveSiteDescription => ActiveSite is { } site
        ? $"{site.SiteType} {site.Kind.ToString().ToLowerInvariant()} on "
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

    public GuardianObelisk? CurrentObelisk => Proximity?.CurrentObelisk;

    public bool HasCurrentObelisk => CurrentObelisk is not null;

    public string SiteDistanceText => Proximity is { } value
        ? $"{value.DistanceFromSite:N1} m from survey origin"
        : HasActiveSite
            ? "Waiting for surface position, body radius, and site heading."
            : "No live Guardian site detected.";

    public string NearbyPointText => Proximity?.NearestPoint is { } nearby
        ? $"Nearest: {nearby.Point.Name} Â· {nearby.Point.Type} Â· "
            + $"{nearby.Distance:N1} m"
        : HasActiveSite
            ? "No selectable mapped object is available."
            : "Approach a Guardian site to begin proximity tracking.";

    public string CurrentObeliskTitle => CurrentObelisk is { } obelisk
        ? $"{obelisk.Name} Â· active obelisk"
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

    public string OriginStatus => currentPosition is null
        ? "Distances unavailable until a journal supplies galactic coordinates."
        : $"Distances from {currentSystemName ?? "current system"}.";

    public void SetClipboardWriter(Func<string, Task>? writer)
    {
        clipboardWriter = writer;
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
    }

    public void UpdateStatus(EliteStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        currentStatus = status;
        UpdateProximity();
    }

    public void UpdateCargo(CargoSnapshot? cargo)
    {
        if (cargo is not null && artifactInventory.Reset(cargo))
        {
            NotifyCurrentObeliskChanged();
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
        await RefreshAsync(cancellationToken);
    }

    public async Task ApplyJournalEventsAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        string? commanderName,
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
            NotifyActiveSiteChanged();
            UpdateProximity();
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

    private void NotifyActiveSiteChanged()
    {
        OnPropertyChanged(nameof(ActiveSite));
        OnPropertyChanged(nameof(HasActiveSite));
        OnPropertyChanged(nameof(ActiveSiteTitle));
        OnPropertyChanged(nameof(ActiveSiteDescription));
        OnPropertyChanged(nameof(ActiveSiteReference));
        OnPropertyChanged(nameof(ActiveSiteLocation));
        OnPropertyChanged(nameof(ActiveSiteVisit));
    }

    private void NotifyCurrentObeliskChanged()
    {
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
        toggleCurrentObeliskScannedCommand.RaiseCanExecuteChanged();
    }

    private void UpdateProximity()
    {
        proximity = null;
        var site = ActiveSite;
        if (site is null || currentStatus is null)
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
        var template = templates.Find(siteType);
        var location = survey?.Survey.Location
            ?? published?.Location
            ?? site.Location;
        var siteHeading = survey?.Survey.SiteHeading is >= 0 and <= 359
            ? survey.Survey.SiteHeading
            : published?.SiteHeading is >= 0 and <= 359
                ? published.SiteHeading
                : reference?.SiteHeading ?? -1;
        if (template is null || location is null)
        {
            NotifyCurrentObeliskChanged();
            return;
        }

        proximity = proximityEvaluator.Evaluate(
            currentStatus,
            location.Value,
            siteHeading,
            template,
            survey?.Survey,
            GetMergedActiveObelisks(reference, survey),
            GetObeliskGroups(published, survey));
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
        var template = templates.Find(siteType)
            ?? templates.Find(row.Reference.SiteType);
        var published = publishedSites.Find(row.Reference);
        MapProjection = template is null
            ? null
            : mapProjector.Project(
                template,
                survey?.Survey,
                GetMergedActiveObelisks(row.Reference, survey),
                GetObeliskGroups(published, survey));
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
        SurveyEditor.Load(
            activeFrontierId,
            activeIsOdyssey,
            survey,
            templates.Find(siteType));
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
                currentPosition is GalacticCoordinate origin
                    ? origin.DistanceTo(visit.Reference.Position)
                    : null));
        projected = currentPosition is null
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
    }

    private static bool MatchesText(GuardianSiteVisit visit, string text)
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
                StringComparison.OrdinalIgnoreCase) == true;
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

public sealed class GuardianSiteRowViewModel(
    GuardianSiteVisit visit,
    double? distance)
{
    public GuardianSiteVisit Visit { get; } = visit;

    public GuardianSiteReference Reference => Visit.Reference;

    public double? Distance { get; } = distance;

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
}
