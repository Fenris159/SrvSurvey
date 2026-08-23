using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Guardian;

namespace SrvSurvey.Desktop.ViewModels;

public sealed record GuardianSurveyEditorLoadContext(
    string? FrontierId,
    bool IsOdyssey,
    GuardianCommanderSiteSurvey? Survey,
    GuardianSiteTemplate? Template)
{
    public bool ShowComponentMaterials { get; init; }

    public GuardianSiteTemplateCatalog? TemplateCatalog { get; init; }

    public GuardianSiteMapProjection? ReferenceProjection { get; init; }

    public GuardianSiteReference? SiteReference { get; init; }
}

public sealed class GuardianSurveyEditorViewModel : INotifyPropertyChanged
{
    private readonly GuardianCommanderSurveyStore store;
    private readonly Func<
        GuardianCommanderSiteSurvey,
        GuardianCommanderSiteSurvey,
        Task> surveySaved;
    private readonly AsyncCommand saveCommand;
    private readonly AsyncCommand addRawPointCommand;
    private readonly AsyncCommand removeRawPointCommand;
    private readonly AsyncCommand addActiveObeliskCommand;
    private readonly AsyncCommand removeActiveObeliskCommand;
    private string? frontierId;
    private bool isOdyssey = true;
    private bool showComponentMaterials;
    private GuardianSiteTemplateCatalog templates =
        new GuardianSiteTemplateCatalog([]);
    private GuardianSiteSelectionKey? selectionContext;
    private GuardianSiteMapProjection? referenceProjection;
    private GuardianCommanderSiteSurvey? originalSurvey;
    private bool isAvailable;
    private bool isBusy;
    private bool isLoading;
    private string siteType = "Unknown";
    private decimal siteHeading = -1;
    private decimal relicTowerHeading = -1;
    private decimal? surfaceLatitude;
    private decimal? surfaceLongitude;
    private string notes = string.Empty;
    private IReadOnlyList<GuardianSurveyPoiViewModel> points = [];
    private IReadOnlyList<GuardianSurveyPoiViewModel> selectableMapPoints = [];
    private IReadOnlyList<GuardianObeliskGroupViewModel> obeliskGroups = [];
    private IReadOnlyList<GuardianActiveObeliskViewModel> activeObelisks = [];
    private GuardianSurveyPoiViewModel? selectedPoint;
    private string? selectedPointName;
    private GuardianActiveObeliskViewModel? selectedActiveObelisk;
    private GuardianPoiType newRawPointType = GuardianPoiType.Unknown;
    private GuardianSurveyMeasurement? liveMeasurement;
    private string statusMessage =
        "Visit the selected site before editing its commander survey.";

    public GuardianSurveyEditorViewModel(
        GuardianCommanderSurveyStore store,
        Func<
            GuardianCommanderSiteSurvey,
            GuardianCommanderSiteSurvey,
            Task> surveySaved)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.surveySaved = surveySaved
            ?? throw new ArgumentNullException(nameof(surveySaved));
        saveCommand = new AsyncCommand(SaveAsync, () => IsAvailable && !IsBusy);
        addRawPointCommand = new AsyncCommand(
            AddRawPointAsync,
            () => IsAvailable && !IsBusy && liveMeasurement is not null);
        removeRawPointCommand = new AsyncCommand(
            RemoveSelectedRawPointAsync,
            () => IsAvailable
                && !IsBusy
                && SelectedPoint is { IsRaw: true, IsReferenceOnly: false });
        addActiveObeliskCommand = new AsyncCommand(
            AddActiveObeliskAsync,
            () => IsAvailable && !IsBusy);
        removeActiveObeliskCommand = new AsyncCommand(
            RemoveSelectedActiveObeliskAsync,
            () => IsAvailable && !IsBusy && SelectedActiveObelisk is not null);
        SaveCommand = saveCommand;
        AddRawPointCommand = addRawPointCommand;
        RemoveRawPointCommand = removeRawPointCommand;
        AddActiveObeliskCommand = addActiveObeliskCommand;
        RemoveActiveObeliskCommand = removeActiveObeliskCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand SaveCommand { get; }

    public ICommand AddRawPointCommand { get; }

    public ICommand RemoveRawPointCommand { get; }

    public ICommand AddActiveObeliskCommand { get; }

    public ICommand RemoveActiveObeliskCommand { get; }

    public IReadOnlyList<GuardianPoiType> RawPointTypes { get; } =
        Enum.GetValues<GuardianPoiType>()
            .Where(type => type != GuardianPoiType.EmptyPuddle)
            .ToArray();

    public IReadOnlyList<string> SiteTypeOptions { get; private set; } = [];

    public bool IsAvailable
    {
        get => isAvailable;
        private set
        {
            if (SetField(ref isAvailable, value))
            {
                saveCommand.RaiseCanExecuteChanged();
                addRawPointCommand.RaiseCanExecuteChanged();
                removeRawPointCommand.RaiseCanExecuteChanged();
                addActiveObeliskCommand.RaiseCanExecuteChanged();
                removeActiveObeliskCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(AvailabilityMessage));
                OnPropertyChanged(nameof(CanEditSelectedPoint));
                OnPropertyChanged(nameof(IsSelectedPointReadOnly));
            }
        }
    }

    public string AvailabilityMessage
    {
        get
        {
            if (!IsAvailable)
            {
                return "Reference map only. Visit the selected site before editing its commander survey.";
            }

            return templates.Find(SiteType) is null
                ? "Choose a site type to load its map points and repair this survey."
                : "Editing the selected commander survey. Save writes the legacy-compatible file atomically.";
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                saveCommand.RaiseCanExecuteChanged();
                addRawPointCommand.RaiseCanExecuteChanged();
                removeRawPointCommand.RaiseCanExecuteChanged();
                addActiveObeliskCommand.RaiseCanExecuteChanged();
                removeActiveObeliskCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(SaveButtonText));
            }
        }
    }

    public string SaveButtonText => IsBusy ? "Saving..." : "Save survey";

    public string SiteType
    {
        get => siteType;
        set
        {
            if (!SetField(ref siteType, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsRuinsSite));
            OnPropertyChanged(nameof(AvailabilityMessage));
            if (!isLoading)
            {
                ReloadPointsForSiteType(preserveDraft: true);
                StatusMessage = templates.Find(value) is null
                    ? $"No Guardian map template is available for {value}."
                    : $"Loaded the {value} map template. Review the points, then save the survey.";
            }
        }
    }

    public bool IsRuinsSite => string.Equals(
            SiteType,
            "Alpha",
            StringComparison.OrdinalIgnoreCase)
        || string.Equals(SiteType, "Beta", StringComparison.OrdinalIgnoreCase)
        || string.Equals(SiteType, "Gamma", StringComparison.OrdinalIgnoreCase);

    public decimal SiteHeading
    {
        get => siteHeading;
        set => SetField(ref siteHeading, value);
    }

    public decimal RelicTowerHeading
    {
        get => relicTowerHeading;
        set => SetField(ref relicTowerHeading, value);
    }

    public decimal? SurfaceLatitude
    {
        get => surfaceLatitude;
        set => SetField(ref surfaceLatitude, value);
    }

    public decimal? SurfaceLongitude
    {
        get => surfaceLongitude;
        set => SetField(ref surfaceLongitude, value);
    }

    public string Notes
    {
        get => notes;
        set => SetField(ref notes, value);
    }

    public IReadOnlyList<GuardianSurveyPoiViewModel> Points
    {
        get => points;
        private set => SetField(ref points, value);
    }

    public IReadOnlyList<GuardianObeliskGroupViewModel> ObeliskGroups
    {
        get => obeliskGroups;
        private set => SetField(ref obeliskGroups, value);
    }

    public IReadOnlyList<GuardianActiveObeliskViewModel> ActiveObelisks
    {
        get => activeObelisks;
        private set => SetField(ref activeObelisks, value);
    }

    public GuardianSurveyPoiViewModel? SelectedPoint
    {
        get => selectedPoint;
        set
        {
            if (SetField(ref selectedPoint, value))
            {
                selectedPointName = value?.Name;
                NotifySelectedPointStateChanged(selectionNameChanged: true);
                if (value is not null)
                {
                    SelectedActiveObelisk = null;
                }
            }
        }
    }

    public bool HasSelectedPoint => SelectedPoint is not null;

    public bool HasSelectedRawPoint => SelectedPoint is
    { IsRaw: true, IsReferenceOnly: false };

    public bool HasSelectedMapMarker =>
        !string.IsNullOrWhiteSpace(SelectedPointName);

    public bool IsMapSummaryVisible => !HasSelectedMapMarker;

    public bool CanEditSelectedPoint => IsAvailable
        && SelectedPoint is { IsReferenceOnly: false };

    public bool IsSelectedPointReadOnly => HasSelectedMapMarker
        && !CanEditSelectedPoint;

    public string? SelectedPointName
    {
        get => selectedPointName;
        set
        {
            if (string.Equals(
                    selectedPointName,
                    value,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            selectedPointName = value;
            NotifySelectedPointStateChanged(selectionNameChanged: true);
            var point = value is null
                ? null
                : selectableMapPoints.FirstOrDefault(candidate => string.Equals(
                    candidate.Name,
                    value,
                    StringComparison.OrdinalIgnoreCase));
            if (!ReferenceEquals(selectedPoint, point))
            {
                selectedPoint = point;
                OnPropertyChanged(nameof(SelectedPoint));
                NotifySelectedPointStateChanged(selectionNameChanged: false);
            }

            SelectedActiveObelisk = value is not null
                ? ActiveObelisks.FirstOrDefault(candidate => string.Equals(
                    candidate.Name,
                    value,
                    StringComparison.OrdinalIgnoreCase))
                : null;
        }
    }

    public GuardianActiveObeliskViewModel? SelectedActiveObelisk
    {
        get => selectedActiveObelisk;
        set
        {
            if (SetField(ref selectedActiveObelisk, value))
            {
                if (value is not null
                    && !string.Equals(
                        selectedPointName,
                        value.Name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    selectedPointName = value.Name;
                    OnPropertyChanged(nameof(SelectedPointName));
                }
                OnPropertyChanged(nameof(HasSelectedActiveObelisk));
                removeActiveObeliskCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasSelectedActiveObelisk => SelectedActiveObelisk is not null;

    public GuardianPoiType NewRawPointType
    {
        get => newRawPointType;
        set => SetField(ref newRawPointType, value);
    }

    public bool HasLiveMeasurement => liveMeasurement is not null;

    public string LiveMeasurementText => liveMeasurement is { } measurement
        ? $"{measurement.Distance:N1} m from origin · "
            + $"angle {measurement.Angle:N1}° · "
            + $"rotation {measurement.Rotation:N0}°"
        : "Stand at the new point in the active selected site with valid surface coordinates.";

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public void Load(GuardianSurveyEditorLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var survey = context.Survey;
        var template = context.Template;
        var templateCatalog = context.TemplateCatalog;
        var siteReference = context.SiteReference;
        GuardianSiteSelectionKey? nextSelectionContext = siteReference is null
            ? null
            : new GuardianSiteSelectionKey(
                siteReference.Kind,
                siteReference.SystemAddress,
                siteReference.BodyId,
                siteReference.Index,
                siteReference.SiteId);
        var previousSelectionName = selectionContext == nextSelectionContext
            ? SelectedPointName
            : null;
        selectionContext = nextSelectionContext;
        frontierId = context.FrontierId;
        isOdyssey = context.IsOdyssey;
        showComponentMaterials = context.ShowComponentMaterials;
        templates = templateCatalog
            ?? (template is null
                ? new GuardianSiteTemplateCatalog([])
                : new GuardianSiteTemplateCatalog([template]));
        referenceProjection = context.ReferenceProjection;
        originalSurvey = survey;
        SelectedPointName = null;
        IsAvailable = frontierId is not null
            && survey is not null;
        if (!IsAvailable || survey is null)
        {
            isLoading = true;
            SiteTypeOptions = templates.Templates
                .Select(candidate => candidate.SiteType)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            OnPropertyChanged(nameof(SiteTypeOptions));
            SiteType = template?.SiteType ?? "Unknown";
            SiteHeading = -1;
            RelicTowerHeading = -1;
            SurfaceLatitude = null;
            SurfaceLongitude = null;
            Notes = string.Empty;
            Points = [];
            selectableMapPoints = BuildSelectableMapPoints(
                template,
                referenceProjection,
                Points);
            ObeliskGroups = [];
            ActiveObelisks = [];
            SelectedPointName = previousSelectionName;
            SelectedActiveObelisk = null;
            UpdateLiveMeasurement(null);
            StatusMessage = AvailabilityMessage;
            isLoading = false;
            return;
        }

        isLoading = true;
        var resolvedSiteType = !string.IsNullOrWhiteSpace(survey.SiteType)
            ? survey.SiteType
            : template?.SiteType ?? "Unknown";
        SiteTypeOptions = templates.Templates
            .Select(candidate => candidate.SiteType)
            .Append(resolvedSiteType)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        OnPropertyChanged(nameof(SiteTypeOptions));
        SiteType = resolvedSiteType;
        SiteHeading = survey.Survey.SiteHeading;
        RelicTowerHeading = survey.Survey.RelicTowerHeading;
        SurfaceLatitude = survey.Survey.Location is { } location
            ? (decimal)location.Latitude
            : null;
        SurfaceLongitude = survey.Survey.Location is { } longitude
            ? (decimal)longitude.Longitude
            : null;
        Notes = survey.Notes;
        ActiveObelisks = survey.ActiveObelisks
            .Select(obelisk => new GuardianActiveObeliskViewModel(obelisk))
            .OrderBy(obelisk => obelisk.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        SelectedActiveObelisk = null;
        LoadPointRows(
            templates.Find(SiteType) ?? template,
            survey.Survey,
            previousSelectionName,
            referenceProjection);
        isLoading = false;
        StatusMessage = templates.Find(SiteType) is null
            ? AvailabilityMessage
            : $"Loaded {Points.Count:N0} surveyable point(s) from "
                + $"{Path.GetFileName(survey.Path)}.";
    }

    private void LoadPointRows(
        GuardianSiteTemplate? template,
        GuardianSurveyData survey,
        string? selectedPointName,
        GuardianSiteMapProjection? referenceProjection = null)
    {
        if (template is null)
        {
            Points = [];
            selectableMapPoints = BuildSelectableMapPoints(
                template,
                referenceProjection,
                Points);
            ObeliskGroups = [];
            SelectedPoint = null;
            return;
        }

        var rawPoints = survey.RawPointsOfInterest ?? [];
        Points = template.SurveyPoints
            .Concat(showComponentMaterials
                ? template.DestructiblePanels
                : [])
            .Select(point => new GuardianSurveyPoiViewModel(
                point,
                survey.PoiStatuses.GetValueOrDefault(point.Name),
                survey.RelicHeadings.GetValueOrDefault(
                    point.Name,
                    -1),
                isRaw: false,
                survey.ComponentMaterials.GetValueOrDefault(
                    point.Name),
                showComponentMaterials))
            .Concat(rawPoints.Select(point => new GuardianSurveyPoiViewModel(
                point,
                survey.PoiStatuses.GetValueOrDefault(
                    point.Name,
                    GuardianPoiStatus.Present),
                point.Type == GuardianPoiType.Relic
                    ? (int)point.Rotation
                    : -1,
                isRaw: true,
                survey.ComponentMaterials.GetValueOrDefault(
                    point.Name),
                showComponentMaterials)))
            .OrderBy(point => point.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        selectableMapPoints = BuildSelectableMapPoints(
            template,
            referenceProjection,
            Points);
        ObeliskGroups = template.ObeliskGroupNameLocations.Keys
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name[0])
            .Distinct()
            .Order()
            .Select(group => new GuardianObeliskGroupViewModel(
                group,
                originalSurvey?.ObeliskGroups.Contains(group) == true))
            .ToArray();
        SelectedPoint = selectedPointName is null
            ? null
            : selectableMapPoints.FirstOrDefault(point => string.Equals(
                point.Name,
                selectedPointName,
                StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyList<GuardianSurveyPoiViewModel> BuildSelectableMapPoints(
        GuardianSiteTemplate? template,
        GuardianSiteMapProjection? referenceProjection,
        IReadOnlyList<GuardianSurveyPoiViewModel> editablePoints)
    {
        if (referenceProjection is null)
        {
            return editablePoints;
        }

        var editableByName = editablePoints.ToDictionary(
            point => point.Name,
            StringComparer.OrdinalIgnoreCase);
        var templatePointNames = (template?.PointsOfInterest ?? [])
            .Concat(template?.DestructiblePanels ?? [])
            .Select(point => point.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var projectedRows = referenceProjection.Points
            .Select(point => editableByName.GetValueOrDefault(point.Name)
                ?? CreateReferencePointRow(
                    point,
                    isRaw: !templatePointNames.Contains(point.Name)))
            .ToArray();
        var projectedNames = projectedRows
            .Select(point => point.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return projectedRows
            .Concat(editablePoints.Where(point => !projectedNames.Contains(
                point.Name)))
            .ToArray();
    }

    private GuardianSurveyPoiViewModel CreateReferencePointRow(
        GuardianProjectedPoint point,
        bool isRaw)
    {
        var componentMaterials = point.ComponentMaterials.Count == 0
            ? null
            : new GuardianComponentLoadout(
                point.Name,
                point.ComponentMaterials);
        return new GuardianSurveyPoiViewModel(
            new GuardianPointOfInterest(
                point.Name,
                point.Type,
                point.Angle,
                point.Distance,
                point.Rotation),
            point.Status,
            point.RelicHeading,
            isRaw,
            componentMaterials,
            showComponentMaterials,
            isReferenceOnly: true);
    }

    public void UpdateLiveMeasurement(GuardianSurveyMeasurement? measurement)
    {
        if (liveMeasurement == measurement)
        {
            return;
        }

        liveMeasurement = measurement;
        OnPropertyChanged(nameof(HasLiveMeasurement));
        OnPropertyChanged(nameof(LiveMeasurementText));
        addRawPointCommand.RaiseCanExecuteChanged();
    }

    private void ReloadPointsForSiteType(bool preserveDraft)
    {
        if (originalSurvey is null)
        {
            Points = [];
            ObeliskGroups = [];
            SelectedPoint = null;
            return;
        }

        var previousSelectionName = SelectedPointName;
        var source = originalSurvey.Survey;
        if (preserveDraft && Points.Count > 0)
        {
            var maps = BuildSurveyMutationMaps();
            var rawPoints = Points
                .Where(point => point.IsRaw)
                .Select(BuildRawPointForSave)
                .ToArray();
            source = new GuardianSurveyData
            {
                SiteType = SiteType,
                SiteHeading = (int)SiteHeading,
                RelicTowerHeading = (int)RelicTowerHeading,
                Location = BuildSurfaceLocation(),
                PoiStatuses = maps.Statuses,
                RelicHeadings = maps.RelicHeadings,
                ComponentMaterials = maps.ComponentMaterials,
                RawPointsOfInterest = rawPoints.Length == 0 ? null : rawPoints,
            };
        }

        LoadPointRows(
            templates.Find(SiteType),
            source,
            previousSelectionName,
            referenceProjection);
    }

    public Task AddActiveObeliskAsync()
    {
        if (!IsAvailable)
        {
            StatusMessage = AvailabilityMessage;
            return Task.CompletedTask;
        }

        var obelisk = new GuardianActiveObeliskViewModel(
            new GuardianObelisk(
                NextActiveObeliskName(ActiveObelisks.Select(item => item.Name)),
                string.Empty,
                false,
                []));
        ActiveObelisks = ActiveObelisks
            .Append(obelisk)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        SelectedActiveObelisk = obelisk;
        StatusMessage = "Added an active-obelisk draft. Enter its map name, log code, and artifact codes, then save.";
        return Task.CompletedTask;
    }

    public Task RemoveSelectedActiveObeliskAsync()
    {
        if (SelectedActiveObelisk is not { } selected)
        {
            return Task.CompletedTask;
        }

        ActiveObelisks = ActiveObelisks
            .Where(obelisk => !ReferenceEquals(obelisk, selected))
            .ToArray();
        if (string.Equals(
                SelectedPointName,
                selected.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            SelectedPointName = null;
        }
        else
        {
            SelectedActiveObelisk = null;
        }
        StatusMessage = $"Removed active obelisk {selected.Name}. Save the survey to persist the removal.";
        return Task.CompletedTask;
    }

    public Task AddRawPointAsync()
    {
        if (!IsAvailable || liveMeasurement is not { } measurement)
        {
            StatusMessage = "A live commander measurement at the selected active site is required.";
            return Task.CompletedTask;
        }

        var duplicate = Points.FirstOrDefault(point => IsTooClose(
            point.Point,
            NewRawPointType,
            measurement.Angle,
            measurement.Distance));
        if (duplicate is not null)
        {
            StatusMessage = $"The measured point is too close to {duplicate.Name}; no raw point was added.";
            return Task.CompletedTask;
        }

        var name = NextRawPointName(Points.Select(point => point.Name));
        var point = new GuardianPointOfInterest(
            name,
            NewRawPointType,
            measurement.Angle,
            measurement.Distance,
            NewRawPointType == GuardianPoiType.Relic
                ? -1
                : measurement.Rotation);
        var row = new GuardianSurveyPoiViewModel(
            point,
            GuardianPoiStatus.Present,
            point.Type == GuardianPoiType.Relic ? (int)point.Rotation : -1,
            isRaw: true,
            componentModeEnabled: showComponentMaterials);
        Points = Points
            .Append(row)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        selectableMapPoints = selectableMapPoints
            .Where(item => !string.Equals(
                item.Name,
                row.Name,
                StringComparison.OrdinalIgnoreCase))
            .Append(row)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        SelectedPoint = row;
        StatusMessage = $"Added {name} as a local raw {NewRawPointType} point. Save the survey to persist it.";
        return Task.CompletedTask;
    }

    public Task RemoveSelectedRawPointAsync()
    {
        if (SelectedPoint is not
            { IsRaw: true, IsReferenceOnly: false } selected)
        {
            StatusMessage = "Only commander-specific raw points can be removed.";
            return Task.CompletedTask;
        }

        Points = Points.Where(point => !ReferenceEquals(point, selected)).ToArray();
        selectableMapPoints = selectableMapPoints
            .Where(point => !ReferenceEquals(point, selected))
            .ToArray();
        SelectedPoint = Points.Count > 0 ? Points[0] : null;
        StatusMessage = $"Removed local raw point {selected.Name}. Save the survey to persist the removal.";
        return Task.CompletedTask;
    }

    public async Task SaveAsync()
    {
        if (!TryBeginSave(
                out var normalizedSiteHeading,
                out var normalizedRelicTowerHeading,
                out var surfaceLocation))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var updated = BuildSurveyForSave(
                normalizedSiteHeading,
                normalizedRelicTowerHeading,
                surfaceLocation);
            var path = await store.SaveAsync(frontierId!, isOdyssey, updated);
            var saved = updated with { Path = path };
            var previous = originalSurvey!;
            originalSurvey = saved;
            await surveySaved(previous, saved);
            StatusMessage = $"Saved Guardian survey to {Path.GetFileName(path)}.";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            StatusMessage = "The Guardian survey could not be saved: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool TryBeginSave(
        out int normalizedSiteHeading,
        out int normalizedRelicTowerHeading,
        out GuardianSurfaceLocation? surfaceLocation)
    {
        normalizedSiteHeading = -1;
        normalizedRelicTowerHeading = -1;
        surfaceLocation = null;
        if (!IsAvailable
            || frontierId is null
            || originalSurvey is null)
        {
            StatusMessage = AvailabilityMessage;
            return false;
        }

        if (!TryGetHeading(SiteHeading, out normalizedSiteHeading)
            || !TryGetHeading(
                RelicTowerHeading,
                out normalizedRelicTowerHeading))
        {
            StatusMessage = "Headings must be -1 for unknown or a whole number from 0 through 359.";
            return false;
        }

        if (templates.Find(SiteType) is null)
        {
            StatusMessage = "Choose a recognized Guardian site type before saving.";
            return false;
        }

        if (!TryBuildSurfaceLocation(out surfaceLocation))
        {
            return false;
        }

        return TryValidatePointsForSave()
            && TryValidateActiveObelisksForSave();
    }

    private GuardianCommanderSiteSurvey BuildSurveyForSave(
        int normalizedSiteHeading,
        int normalizedRelicTowerHeading,
        GuardianSurfaceLocation? surfaceLocation)
    {
        var maps = BuildSurveyMutationMaps();
        var rawPoints = Points
            .Where(point => point.IsRaw)
            .Select(BuildRawPointForSave)
            .ToArray();
        return originalSurvey! with
        {
            SiteType = SiteType,
            Notes = Notes,
            Survey = new GuardianSurveyData
            {
                SiteType = SiteType,
                SiteHeading = normalizedSiteHeading,
                RelicTowerHeading = normalizedRelicTowerHeading,
                Location = surfaceLocation,
                PoiStatuses = maps.Statuses,
                RelicHeadings = maps.RelicHeadings,
                ComponentMaterials = maps.ComponentMaterials,
                RawPointsOfInterest = rawPoints.Length == 0
                    ? null
                    : rawPoints,
            },
            ObeliskGroups = ObeliskGroups
                .Where(group => group.IsSelected)
                .Select(group => group.Name)
                .ToHashSet(),
            ActiveObelisks = ActiveObelisks
                .Select(obelisk => obelisk.ToModel())
                .OrderBy(obelisk => obelisk.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
    }

    private static GuardianPointOfInterest BuildRawPointForSave(
        GuardianSurveyPoiViewModel point)
    {
        return point.SupportsRelicHeading
            && TryGetHeading(point.RelicHeading, out var heading)
                ? point.Point with { Rotation = heading }
                : point.Point;
    }

    private bool TryValidatePointsForSave()
    {
        foreach (var point in Points)
        {
            if (point.Status == GuardianPoiStatus.Empty
                && !point.SupportsEmptyStatus)
            {
                StatusMessage = $"{point.Name} ({point.Type}) cannot be marked empty.";
                return false;
            }

            if (!TryGetHeading(point.RelicHeading, out _))
            {
                StatusMessage = $"The relic heading for {point.Name} must be -1 "
                    + "or a whole number from 0 through 359.";
                return false;
            }


            if (point.IsRaw && !point.HasValidRawGeometry)
            {
                StatusMessage = $"The raw geometry for {point.Name} must use an angle from 0 through 359.999, a non-negative distance, and a rotation from -1 through 359.999.";
                return false;
            }
        }

        return true;
    }

    private bool TryBuildSurfaceLocation(
        out GuardianSurfaceLocation? location)
    {
        location = null;
        if (SurfaceLatitude is null && SurfaceLongitude is null)
        {
            return true;
        }

        if (SurfaceLatitude is not { } latitude
            || SurfaceLongitude is not { } longitude)
        {
            StatusMessage = "Enter both latitude and longitude, or leave both blank for an unknown surface origin.";
            return false;
        }

        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            StatusMessage = "Surface latitude must be from -90 through 90 and longitude from -180 through 180.";
            return false;
        }

        location = new GuardianSurfaceLocation(
            decimal.ToDouble(latitude),
            decimal.ToDouble(longitude));
        return true;
    }

    private GuardianSurfaceLocation? BuildSurfaceLocation()
    {
        return TryBuildSurfaceLocation(out var location)
            ? location
            : originalSurvey?.Survey.Location;
    }

    private bool TryValidateActiveObelisksForSave()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var obelisk in ActiveObelisks)
        {
            if (!obelisk.IsLegacyEncodable)
            {
                StatusMessage = "Active-obelisk names, log codes, and artifact codes cannot be blank where required or contain legacy delimiters (-, !, or commas inside a code).";
                return false;
            }

            if (!names.Add(obelisk.Name.Trim()))
            {
                StatusMessage = $"Active obelisk {obelisk.Name.Trim()} is duplicated.";
                return false;
            }
        }

        return true;
    }

    private SurveyMutationMaps BuildSurveyMutationMaps()
    {
        var siteTypeChanged = !string.Equals(
            SiteType,
            originalSurvey!.SiteType,
            StringComparison.OrdinalIgnoreCase);
        var originalRawNames = (originalSurvey.Survey.RawPointsOfInterest ?? [])
            .Select(point => point.Name)
            .ToHashSet(StringComparer.Ordinal);
        var statuses = new Dictionary<string, GuardianPoiStatus>(
            originalSurvey.Survey.PoiStatuses
                .Where(pair => !siteTypeChanged
                    || originalRawNames.Contains(pair.Key)),
            StringComparer.Ordinal);
        var relicHeadings = new Dictionary<string, int>(
            originalSurvey.Survey.RelicHeadings
                .Where(pair => !siteTypeChanged
                    || originalRawNames.Contains(pair.Key)),
            StringComparer.Ordinal);
        var componentMaterials = new Dictionary<string, GuardianComponentLoadout>(
            originalSurvey.Survey.ComponentMaterials
                .Where(pair => !siteTypeChanged
                    || originalRawNames.Contains(pair.Key)),
            StringComparer.Ordinal);
        var retainedRawNames = Points
            .Where(point => point.IsRaw)
            .Select(point => point.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var removedName in (originalSurvey.Survey.RawPointsOfInterest ?? [])
                     .Select(point => point.Name)
                     .Where(name => !retainedRawNames.Contains(name)))
        {
            statuses.Remove(removedName);
            relicHeadings.Remove(removedName);
            componentMaterials.Remove(removedName);
        }

        foreach (var point in Points)
        {
            ApplyPointMutation(point, statuses, relicHeadings, componentMaterials);
        }

        return new SurveyMutationMaps(statuses, relicHeadings, componentMaterials);
    }

    private static void ApplyPointMutation(
        GuardianSurveyPoiViewModel point,
        Dictionary<string, GuardianPoiStatus> statuses,
        Dictionary<string, int> relicHeadings,
        Dictionary<string, GuardianComponentLoadout> componentMaterials)
    {
        if (point.HasComponentRecord)
        {
            componentMaterials[point.Name] = point.CreateComponentLoadout();
        }

        if (point.IsRaw)
        {
            ApplyRelicHeading(point, relicHeadings);
            return;
        }

        if (point.Status == GuardianPoiStatus.Unknown)
        {
            statuses.Remove(point.Name);
        }
        else
        {
            statuses[point.Name] = point.Status;
        }

        ApplyRelicHeading(point, relicHeadings);
    }

    private static void ApplyRelicHeading(
        GuardianSurveyPoiViewModel point,
        Dictionary<string, int> relicHeadings)
    {
        if (!point.SupportsRelicHeading)
        {
            return;
        }

        if (TryGetHeading(point.RelicHeading, out var heading) && heading >= 0)
        {
            relicHeadings[point.Name] = heading;
        }
        else
        {
            relicHeadings.Remove(point.Name);
        }
    }

    private sealed record SurveyMutationMaps(
        Dictionary<string, GuardianPoiStatus> Statuses,
        Dictionary<string, int> RelicHeadings,
        Dictionary<string, GuardianComponentLoadout> ComponentMaterials);

    private readonly record struct GuardianSiteSelectionKey(
        GuardianSiteKind Kind,
        long SystemAddress,
        int BodyId,
        int Index,
        int SiteId);

    private static bool TryGetHeading(decimal value, out int heading)
    {
        if (value != decimal.Truncate(value) || value is < -1 or > 359)
        {
            heading = -1;
            return false;
        }

        heading = decimal.ToInt32(value);
        return true;
    }

    private static bool IsTooClose(
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

    private static string NextRawPointName(IEnumerable<string> names)
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

    private static string NextActiveObeliskName(IEnumerable<string> names)
    {
        var used = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index <= 999; index++)
        {
            var candidate = $"A{index:00}";
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }

        return "NEW";
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

    private void NotifySelectedPointStateChanged(bool selectionNameChanged)
    {
        OnPropertyChanged(nameof(HasSelectedPoint));
        OnPropertyChanged(nameof(HasSelectedRawPoint));
        OnPropertyChanged(nameof(HasSelectedMapMarker));
        OnPropertyChanged(nameof(IsMapSummaryVisible));
        OnPropertyChanged(nameof(CanEditSelectedPoint));
        OnPropertyChanged(nameof(IsSelectedPointReadOnly));
        if (selectionNameChanged)
        {
            OnPropertyChanged(nameof(SelectedPointName));
        }

        removeRawPointCommand.RaiseCanExecuteChanged();
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

public sealed record GuardianSurveyMeasurement(
    double Distance,
    double Angle,
    double Rotation);

public sealed class GuardianSurveyPoiViewModel : INotifyPropertyChanged
{
    private static readonly GuardianPoiStatus[] BasicStatuses =
        Enum.GetValues<GuardianPoiStatus>();
    private static readonly GuardianPoiStatus[] StandardStatuses =
    [
        GuardianPoiStatus.Unknown,
        GuardianPoiStatus.Present,
        GuardianPoiStatus.Absent,
    ];
    private GuardianPoiStatus status;
    private GuardianPoiType type;
    private decimal rawAngle;
    private decimal rawDistance;
    private decimal rawRotation;
    private decimal relicHeading;
    private GuardianComponentMaterial topComponentMaterial;
    private GuardianComponentMaterial middleComponentMaterial;
    private GuardianComponentMaterial bottomComponentMaterial;
    private bool hasComponentRecord;
    private readonly bool componentModeEnabled;
    private readonly GuardianPointOfInterest sourcePoint;

    public GuardianSurveyPoiViewModel(
        GuardianPointOfInterest point,
        GuardianPoiStatus status,
        int relicHeading,
        bool isRaw = false,
        GuardianComponentLoadout? componentMaterials = null,
        bool componentModeEnabled = false,
        bool isReferenceOnly = false)
    {
        sourcePoint = point;
        type = point.Type;
        rawAngle = (decimal)point.Angle;
        rawDistance = (decimal)point.Distance;
        rawRotation = (decimal)point.Rotation;
        this.status = status;
        this.relicHeading = relicHeading;
        IsRaw = isRaw;
        IsReferenceOnly = isReferenceOnly;
        this.componentModeEnabled = componentModeEnabled;
        hasComponentRecord = componentMaterials is not null;
        topComponentMaterial = componentMaterials?.GetItem(0)
            ?? GuardianComponentMaterial.Unknown;
        middleComponentMaterial = componentMaterials?.GetItem(1)
            ?? GuardianComponentMaterial.Unknown;
        bottomComponentMaterial = componentMaterials?.GetItem(2)
            ?? GuardianComponentMaterial.Unknown;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public GuardianPointOfInterest Point => IsRaw
        ? sourcePoint with
        {
            Type = Type,
            Angle = decimal.ToDouble(RawAngle),
            Distance = decimal.ToDouble(RawDistance),
            Rotation = decimal.ToDouble(RawRotation),
        }
        : sourcePoint;

    public bool IsRaw { get; }

    public bool IsReferenceOnly { get; }

    public bool IsStatusEditable => !IsRaw && !IsReferenceOnly;

    public string Name => sourcePoint.Name;

    public GuardianPoiType Type
    {
        get => type;
        set
        {
            if (!IsRaw || type == value)
            {
                return;
            }

            type = value;
            if (SupportsRelicHeading)
            {
                relicHeading = rawRotation;
            }

            NotifyPointShapeChanged();
        }
    }

    public string TypeText => Type.ToString();

    public string PositionText => $"{RawDistance:N1} m · {RawAngle:N1}°";

    public decimal RawAngle
    {
        get => rawAngle;
        set
        {
            if (rawAngle == value)
            {
                return;
            }

            rawAngle = value;
            NotifyPositionChanged(nameof(RawAngle));
        }
    }

    public decimal RawDistance
    {
        get => rawDistance;
        set
        {
            if (rawDistance == value)
            {
                return;
            }

            rawDistance = value;
            NotifyPositionChanged(nameof(RawDistance));
        }
    }

    public decimal RawRotation
    {
        get => rawRotation;
        set
        {
            if (rawRotation == value)
            {
                return;
            }

            rawRotation = value;
            if (SupportsRelicHeading)
            {
                relicHeading = value;
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nameof(RelicHeading)));
            }

            NotifyPositionChanged(nameof(RawRotation));
        }
    }

    public bool HasValidRawGeometry => !IsRaw
        || RawAngle is >= 0 and < 360
            && RawDistance >= 0
            && RawRotation is >= -1 and < 360;

    public bool SupportsRelicHeading => Type == GuardianPoiType.Relic;

    public bool SupportsComponentMaterials => Type is GuardianPoiType.Component
        or GuardianPoiType.DestructiblePanel;

    public bool SupportsMultipleComponentMaterials =>
        Type == GuardianPoiType.Component;

    public bool CanEditComponentMaterials => componentModeEnabled
        && SupportsComponentMaterials;

    public IReadOnlyList<GuardianComponentMaterial> ComponentMaterialOptions { get; }
        = Enum.GetValues<GuardianComponentMaterial>();

    public IReadOnlyList<GuardianPoiType> EditableRawPointTypes { get; }
        = Enum.GetValues<GuardianPoiType>()
            .Where(type => type != GuardianPoiType.EmptyPuddle)
            .ToArray();

    public bool HasComponentRecord => hasComponentRecord;

    public GuardianComponentMaterial TopComponentMaterial
    {
        get => topComponentMaterial;
        set => SetComponentMaterial(
            ref topComponentMaterial,
            value,
            nameof(TopComponentMaterial));
    }

    public GuardianComponentMaterial MiddleComponentMaterial
    {
        get => middleComponentMaterial;
        set => SetComponentMaterial(
            ref middleComponentMaterial,
            value,
            nameof(MiddleComponentMaterial));
    }

    public GuardianComponentMaterial BottomComponentMaterial
    {
        get => bottomComponentMaterial;
        set => SetComponentMaterial(
            ref bottomComponentMaterial,
            value,
            nameof(BottomComponentMaterial));
    }

    public string ComponentMaterialSummary => !SupportsComponentMaterials
        ? string.Empty
        : (SupportsMultipleComponentMaterials) switch
        {
            true => $"Top {GetMaterialName(TopComponentMaterial)} / "
                + $"middle {GetMaterialName(MiddleComponentMaterial)} / "
                + $"bottom {GetMaterialName(BottomComponentMaterial)}",
            false => GetMaterialName(TopComponentMaterial)
        };

    public bool SupportsEmptyStatus => Type is GuardianPoiType.Unknown
        or GuardianPoiType.Orb
        or GuardianPoiType.Casket
        or GuardianPoiType.Tablet
        or GuardianPoiType.Totem
        or GuardianPoiType.Urn;

    public IReadOnlyList<GuardianPoiStatus> AllowedStatuses =>
        SupportsEmptyStatus ? BasicStatuses : StandardStatuses;

    public GuardianPoiStatus Status
    {
        get => status;
        set
        {
            if (status == value)
            {
                return;
            }

            status = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(Status)));
        }
    }

    public decimal RelicHeading
    {
        get => relicHeading;
        set
        {
            if (relicHeading == value)
            {
                return;
            }

            relicHeading = value;
            if (IsRaw && SupportsRelicHeading)
            {
                rawRotation = value;
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nameof(RawRotation)));
            }
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(RelicHeading)));
        }
    }

    public GuardianComponentLoadout CreateComponentLoadout()
    {
        if (!SupportsComponentMaterials)
        {
            throw new InvalidOperationException(
                $"{Name} does not support Guardian component materials.");
        }

        return new GuardianComponentLoadout(
            Name,
            SupportsMultipleComponentMaterials
                ?
                [
                    TopComponentMaterial,
                    MiddleComponentMaterial,
                    BottomComponentMaterial,
                ]
                : [TopComponentMaterial]);
    }

    private void SetComponentMaterial(
        ref GuardianComponentMaterial field,
        GuardianComponentMaterial value,
        string propertyName)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        hasComponentRecord = true;
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(HasComponentRecord)));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(ComponentMaterialSummary)));
    }

    private void NotifyPointShapeChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Type)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TypeText)));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(SupportsRelicHeading)));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(SupportsComponentMaterials)));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(SupportsMultipleComponentMaterials)));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(CanEditComponentMaterials)));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(SupportsEmptyStatus)));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(AllowedStatuses)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Point)));
    }

    private void NotifyPositionChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(PositionText)));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(HasValidRawGeometry)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Point)));
    }

    private static string GetMaterialName(GuardianComponentMaterial material)
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
}

public sealed class GuardianObeliskGroupViewModel(
    char name,
    bool isSelected) : INotifyPropertyChanged
{
    private bool isSelected = isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public char Name { get; } = name;

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
            {
                return;
            }

            isSelected = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}

public sealed class GuardianActiveObeliskViewModel : INotifyPropertyChanged
{
    private string name;
    private string logCode;
    private string artifactCodes;
    private bool scanned;

    public GuardianActiveObeliskViewModel(GuardianObelisk obelisk)
    {
        ArgumentNullException.ThrowIfNull(obelisk);
        name = obelisk.Name;
        logCode = obelisk.LogCode;
        artifactCodes = string.Join(',', obelisk.ItemCodes);
        scanned = obelisk.Scanned;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => name;
        set => SetField(ref name, value);
    }

    public string LogCode
    {
        get => logCode;
        set => SetField(ref logCode, value);
    }

    public string ArtifactCodes
    {
        get => artifactCodes;
        set => SetField(ref artifactCodes, value);
    }

    public bool Scanned
    {
        get => scanned;
        set => SetField(ref scanned, value);
    }

    public bool IsLegacyEncodable
    {
        get
        {
            var trimmedName = Name.Trim();
            var trimmedLog = LogCode.Trim();
            return trimmedName.Length > 0
                && trimmedName.IndexOfAny(['-', '!', ',']) < 0
                && trimmedLog.IndexOfAny(['-', '!', ',']) < 0
                && ParseArtifactCodes().All(code =>
                    code.IndexOfAny(['-', '!', ',']) < 0);
        }
    }

    public GuardianObelisk ToModel()
    {
        return new GuardianObelisk(
            Name.Trim(),
            LogCode.Trim(),
            Scanned,
            ParseArtifactCodes());
    }

    private string[] ParseArtifactCodes()
    {
        return ArtifactCodes.Split(
            ',',
            StringSplitOptions.TrimEntries
                | StringSplitOptions.RemoveEmptyEntries);
    }

    private void SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(IsLegacyEncodable)));
    }
}
