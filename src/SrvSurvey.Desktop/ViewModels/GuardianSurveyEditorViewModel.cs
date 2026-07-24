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
        SaveCommand = saveCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand SaveCommand { get; }

    public bool IsAvailable
    {
        get => isAvailable;
        private set
        {
            if (SetField(ref isAvailable, value))
            {
                saveCommand.RaiseCanExecuteChanged();
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
            }
        }
    }

    public bool HasSelectedPoint => SelectedPoint is not null;

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
            StatusMessage = AvailabilityMessage;
            return;
        }

        SiteHeading = survey.Survey.SiteHeading;
        RelicTowerHeading = survey.Survey.RelicTowerHeading;
        Notes = survey.Notes;
        Points = template.SurveyPoints
            .Concat(survey.Survey.RawPointsOfInterest ?? [])
            .Select(point => new GuardianSurveyPoiViewModel(
                point,
                survey.Survey.PoiStatuses.GetValueOrDefault(point.Name),
                survey.Survey.RelicHeadings.GetValueOrDefault(
                    point.Name,
                    -1)))
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
            foreach (var point in Points)
            {
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
                    RawPointsOfInterest = originalSurvey.Survey.RawPointsOfInterest,
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
        int relicHeading)
    {
        Point = point;
        this.status = status;
        this.relicHeading = relicHeading;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public GuardianPointOfInterest Point { get; }

    public string Name => Point.Name;

    public GuardianPoiType Type => Point.Type;

    public string TypeText => Type.ToString();

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
