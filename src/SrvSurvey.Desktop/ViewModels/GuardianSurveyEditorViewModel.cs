using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Guardian;

namespace SrvSurvey.Desktop.ViewModels;

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
    private string? frontierId;
    private bool isOdyssey = true;
    private GuardianCommanderSiteSurvey? originalSurvey;
    private bool isAvailable;
    private bool isBusy;
    private decimal siteHeading = -1;
    private decimal relicTowerHeading = -1;
    private string notes = string.Empty;
    private IReadOnlyList<GuardianSurveyPoiViewModel> points = [];
    private IReadOnlyList<GuardianObeliskGroupViewModel> obeliskGroups = [];
    private GuardianSurveyPoiViewModel? selectedPoint;
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
            () => IsAvailable && !IsBusy && SelectedPoint?.IsRaw == true);
        SaveCommand = saveCommand;
        AddRawPointCommand = addRawPointCommand;
        RemoveRawPointCommand = removeRawPointCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand SaveCommand { get; }

    public ICommand AddRawPointCommand { get; }

    public ICommand RemoveRawPointCommand { get; }

    public IReadOnlyList<GuardianPoiType> RawPointTypes { get; } =
        Enum.GetValues<GuardianPoiType>()
            .Where(type => type != GuardianPoiType.EmptyPuddle)
            .ToArray();

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
                OnPropertyChanged(nameof(AvailabilityMessage));
            }
        }
    }

    public string AvailabilityMessage => IsAvailable
        ? "Editing the selected commander survey. Save writes the legacy-compatible file atomically."
        : "Reference map only. Visit the selected site before editing its commander survey.";

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
                OnPropertyChanged(nameof(SaveButtonText));
            }
        }
    }

    public string SaveButtonText => IsBusy ? "Saving..." : "Save survey";

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

    public GuardianSurveyPoiViewModel? SelectedPoint
    {
        get => selectedPoint;
        set
        {
            if (SetField(ref selectedPoint, value))
            {
                OnPropertyChanged(nameof(HasSelectedPoint));
                OnPropertyChanged(nameof(HasSelectedRawPoint));
                removeRawPointCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasSelectedPoint => SelectedPoint is not null;

    public bool HasSelectedRawPoint => SelectedPoint?.IsRaw == true;

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

    public void Load(
        string? frontierId,
        bool isOdyssey,
        GuardianCommanderSiteSurvey? survey,
        GuardianSiteTemplate? template)
    {
        this.frontierId = frontierId;
        this.isOdyssey = isOdyssey;
        originalSurvey = survey;
        IsAvailable = frontierId is not null
            && survey is not null
            && template is not null;
        if (!IsAvailable || survey is null || template is null)
        {
            SiteHeading = -1;
            RelicTowerHeading = -1;
            Notes = string.Empty;
            Points = [];
            ObeliskGroups = [];
            SelectedPoint = null;
            UpdateLiveMeasurement(null);
            StatusMessage = AvailabilityMessage;
            return;
        }

        SiteHeading = survey.Survey.SiteHeading;
        RelicTowerHeading = survey.Survey.RelicTowerHeading;
        Notes = survey.Notes;
        var rawPoints = survey.Survey.RawPointsOfInterest ?? [];
        Points = template.SurveyPoints
            .Select(point => new GuardianSurveyPoiViewModel(
                point,
                survey.Survey.PoiStatuses.GetValueOrDefault(point.Name),
                survey.Survey.RelicHeadings.GetValueOrDefault(
                    point.Name,
                    -1),
                isRaw: false))
            .Concat(rawPoints.Select(point => new GuardianSurveyPoiViewModel(
                point,
                survey.Survey.PoiStatuses.GetValueOrDefault(
                    point.Name,
                    GuardianPoiStatus.Present),
                point.Type == GuardianPoiType.Relic
                    ? (int)point.Rotation
                    : -1,
                isRaw: true)))
            .OrderBy(point => point.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ObeliskGroups = template.ObeliskGroupNameLocations.Keys
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name[0])
            .Distinct()
            .Order()
            .Select(group => new GuardianObeliskGroupViewModel(
                group,
                survey.ObeliskGroups.Contains(group)))
            .ToArray();
        SelectedPoint = Points.FirstOrDefault();
        StatusMessage = $"Loaded {Points.Count:N0} surveyable point(s) from "
            + $"{Path.GetFileName(survey.Path)}.";
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
            isRaw: true);
        Points = Points
            .Append(row)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        SelectedPoint = row;
        StatusMessage = $"Added {name} as a local raw {NewRawPointType} point. Save the survey to persist it.";
        return Task.CompletedTask;
    }

    public Task RemoveSelectedRawPointAsync()
    {
        if (SelectedPoint is not { IsRaw: true } selected)
        {
            StatusMessage = "Only commander-specific raw points can be removed.";
            return Task.CompletedTask;
        }

        Points = Points.Where(point => !ReferenceEquals(point, selected)).ToArray();
        SelectedPoint = Points.FirstOrDefault();
        StatusMessage = $"Removed local raw point {selected.Name}. Save the survey to persist the removal.";
        return Task.CompletedTask;
    }

    public async Task SaveAsync()
    {
        if (!IsAvailable
            || frontierId is null
            || originalSurvey is null)
        {
            StatusMessage = AvailabilityMessage;
            return;
        }

        if (!TryGetHeading(SiteHeading, out var normalizedSiteHeading)
            || !TryGetHeading(
                RelicTowerHeading,
                out var normalizedRelicTowerHeading))
        {
            StatusMessage = "Headings must be -1 for unknown or a whole number from 0 through 359.";
            return;
        }

        foreach (var point in Points)
        {
            if (point.Status == GuardianPoiStatus.Empty
                && !point.SupportsEmptyStatus)
            {
                StatusMessage = $"{point.Name} ({point.Type}) cannot be marked empty.";
                return;
            }

            if (!TryGetHeading(point.RelicHeading, out _))
            {
                StatusMessage = $"The relic heading for {point.Name} must be -1 "
                    + "or a whole number from 0 through 359.";
                return;
            }
        }

        IsBusy = true;
        try
        {
            var statuses = new Dictionary<string, GuardianPoiStatus>(
                originalSurvey.Survey.PoiStatuses,
                StringComparer.Ordinal);
            var relicHeadings = new Dictionary<string, int>(
                originalSurvey.Survey.RelicHeadings,
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
            }

            foreach (var point in Points)
            {
                if (point.IsRaw)
                {
                    continue;
                }

                if (point.Status == GuardianPoiStatus.Unknown)
                {
                    statuses.Remove(point.Name);
                }
                else
                {
                    statuses[point.Name] = point.Status;
                }

                if (point.SupportsRelicHeading
                    && TryGetHeading(point.RelicHeading, out var heading)
                    && heading >= 0)
                {
                    relicHeadings[point.Name] = heading;
                }
                else if (point.SupportsRelicHeading)
                {
                    relicHeadings.Remove(point.Name);
                }
            }

            var rawPoints = Points
                .Where(point => point.IsRaw)
                .Select(point => point.Point)
                .ToArray();
            var updated = originalSurvey with
            {
                Notes = Notes,
                Survey = new GuardianSurveyData
                {
                    SiteType = originalSurvey.SiteType,
                    SiteHeading = normalizedSiteHeading,
                    RelicTowerHeading = normalizedRelicTowerHeading,
                    Location = originalSurvey.Survey.Location,
                    PoiStatuses = statuses,
                    RelicHeadings = relicHeadings,
                    RawPointsOfInterest = rawPoints.Length == 0
                        ? null
                        : rawPoints,
                },
                ObeliskGroups = ObeliskGroups
                    .Where(group => group.IsSelected)
                    .Select(group => group.Name)
                    .ToHashSet(),
            };
            var path = await store.SaveAsync(frontierId, isOdyssey, updated);
            var saved = updated with { Path = path };
            var previous = originalSurvey;
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
        for (var index = 1; ; index++)
        {
            var candidate = $"x{index}";
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
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
    private decimal relicHeading;

    public GuardianSurveyPoiViewModel(
        GuardianPointOfInterest point,
        GuardianPoiStatus status,
        int relicHeading,
        bool isRaw = false)
    {
        Point = point;
        this.status = status;
        this.relicHeading = relicHeading;
        IsRaw = isRaw;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public GuardianPointOfInterest Point { get; }

    public bool IsRaw { get; }

    public bool IsStatusEditable => !IsRaw;

    public string Name => Point.Name;

    public GuardianPoiType Type => Point.Type;

    public string TypeText => Type.ToString();

    public string PositionText => $"{Point.Distance:N1} m · {Point.Angle:N1}°";

    public bool SupportsRelicHeading => Type == GuardianPoiType.Relic;

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
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(RelicHeading)));
        }
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
