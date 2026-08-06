using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class GroundTargetViewModel : INotifyPropertyChanged
{
    private const string Unavailable = "—";

    private readonly GroundTargetSettingsStore settingsStore;
    private readonly GroundTargetState state;
    private readonly AsyncCommand useCurrentLocationCommand;
    private string targetLatitude = "0";
    private string targetLongitude = "0";
    private string statusMessage;
    private string currentCoordinates = Unavailable;
    private string distanceToTarget = Unavailable;
    private string targetBearing = Unavailable;
    private string relativeHeading = Unavailable;
    private string approachAngle = Unavailable;
    private string approachStatus = "Waiting for surface status";
    private string targetCoordinates = Unavailable;
    private string descentAngle = Unavailable;
    private double relativeBearingDegrees;
    private double attackAngleDegrees;
    private bool isStatusEligible;
    private EliteStatus? status;
    private string? musicTrack;
    private GroundTargetApproach? approach;

    public GroundTargetViewModel(GroundTargetSettingsStore settingsStore)
    {
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        var loadResult = settingsStore.Load();
        state = new GroundTargetState(
            loadResult.Snapshot ?? GroundTargetSnapshot.Empty);
        if (loadResult.Snapshot is not null)
        {
            UpdateTargetInputs(loadResult.Snapshot.Target);
        }

        statusMessage = loadResult.Error
            ?? (loadResult.Exists
                ? $"Loaded the legacy ground target from "
                    + System.IO.Path.GetFileName(loadResult.Path)
                    + "."
                : "No saved ground target is active.");
        SetTargetCommand = new AsyncCommand(SetTargetAsync, () => true);
        ClearTargetCommand = new AsyncCommand(ClearTargetAsync, () => state.IsActive);
        useCurrentLocationCommand = new AsyncCommand(
            UseCurrentLocationAsync,
            () => state.CurrentLocation is not null);
        UseCurrentLocationCommand = useCurrentLocationCommand;
        UpdateDisplay();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string TargetLatitude
    {
        get => targetLatitude;
        set => SetField(ref targetLatitude, value);
    }

    public string TargetLongitude
    {
        get => targetLongitude;
        set => SetField(ref targetLongitude, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public string CurrentCoordinates
    {
        get => currentCoordinates;
        private set => SetField(ref currentCoordinates, value);
    }

    public string DistanceToTarget
    {
        get => distanceToTarget;
        private set => SetField(ref distanceToTarget, value);
    }

    public string TargetBearing
    {
        get => targetBearing;
        private set => SetField(ref targetBearing, value);
    }

    public string RelativeHeading
    {
        get => relativeHeading;
        private set => SetField(ref relativeHeading, value);
    }

    public string ApproachAngle
    {
        get => approachAngle;
        private set => SetField(ref approachAngle, value);
    }

    public string ApproachStatus
    {
        get => approachStatus;
        private set => SetField(ref approachStatus, value);
    }

    public string TargetCoordinates
    {
        get => targetCoordinates;
        private set => SetField(ref targetCoordinates, value);
    }

    public string DescentAngle
    {
        get => descentAngle;
        private set => SetField(ref descentAngle, value);
    }

    public double RelativeBearingDegrees
    {
        get => relativeBearingDegrees;
        private set => SetField(ref relativeBearingDegrees, value);
    }

    public double AttackAngleDegrees
    {
        get => attackAngleDegrees;
        private set => SetField(ref attackAngleDegrees, value);
    }

    public bool IsTargetActive => state.IsActive;

    public SurfaceCoordinate Target => state.Target;

    public string TargetStatusLabel => state.IsActive ? "ACTIVE" : "INACTIVE";

    public bool ShouldShow => state.IsActive
        && state.Solution is not null
        && isStatusEligible;

    public bool HasLevelApproach => approach == GroundTargetApproach.Level;

    public bool HasShallowApproach => approach == GroundTargetApproach.Shallow;

    public bool HasIdealApproach => approach == GroundTargetApproach.Ideal;

    public bool HasSteepApproach => approach == GroundTargetApproach.Steep;

    public bool HasTooSteepApproach => approach == GroundTargetApproach.TooSteep;

    public ICommand SetTargetCommand { get; }

    public ICommand ClearTargetCommand { get; }

    public ICommand UseCurrentLocationCommand { get; }

    public void UpdateStatus(EliteStatus status)
    {
        this.status = status;
        state.UpdateStatus(status);
        isStatusEligible = IsOverlayStatusEligible(status, musicTrack);
        useCurrentLocationCommand.RaiseCanExecuteChanged();
        UpdateDisplay();
    }

    public void UpdateMusicTrack(string? value)
    {
        if (string.Equals(musicTrack, value, StringComparison.Ordinal))
        {
            return;
        }

        musicTrack = value;
        isStatusEligible = status is not null
            && IsOverlayStatusEligible(status, musicTrack);
        OnPropertyChanged(nameof(ShouldShow));
    }

    public async Task SetTargetAsync()
    {
        if (!state.TrySetTarget(
                TargetLatitude,
                TargetLongitude,
                out var error))
        {
            StatusMessage = error ?? "The ground target is invalid.";
            return;
        }

        UpdateTargetInputs(state.Target);
        await SaveAsync("Ground target saved.");
    }

    public async Task SetTargetAsync(
        SurfaceCoordinate target,
        string successMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(successMessage);
        state.SetTarget(target);
        UpdateTargetInputs(state.Target);
        await SaveAsync(successMessage);
    }

    public async Task ApplyPastedTextAsync(string? text)
    {
        if (!state.TrySetTarget(text ?? string.Empty, out var error))
        {
            StatusMessage = error ?? "The clipboard does not contain coordinates.";
            return;
        }

        UpdateTargetInputs(state.Target);
        await SaveAsync("Pasted coordinates saved as the active target.");
    }

    public async Task UseCurrentLocationAsync()
    {
        if (!state.TryUseCurrentLocation(out var error))
        {
            StatusMessage = error ?? "Current coordinates are unavailable.";
            return;
        }

        UpdateTargetInputs(state.Target);
        await SaveAsync("The current surface location is now the active target.");
    }

    public async Task<bool> SetActiveAsync(bool value)
    {
        if (!state.SetActive(value))
        {
            return false;
        }

        await SaveAsync(value
            ? "Ground-target guidance enabled."
            : "Ground-target guidance hidden; the saved coordinates were retained.");
        return true;
    }

    public async Task<int> ApplyJournalEventsAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        bool allowCommands)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        if (!allowCommands)
        {
            return 0;
        }

        var applied = 0;
        foreach (var journalEvent in journalEvents)
        {
            applied += await TryApplyTargetCommandAsync(journalEvent);
        }

        return applied;
    }

    private async Task<int> TryApplyTargetCommandAsync(
        JournalEventEnvelope journalEvent)
    {
        if (journalEvent.EventName != "SendText"
            || !journalEvent.Payload.TryGetProperty("Message", out var value)
            || value.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            return 0;
        }

        var message = value.GetString()?.Trim().ToLowerInvariant();
        switch (message)
        {
            case ".target here":
            case "@":
                var before = state.Version;
                await UseCurrentLocationAsync();
                return state.Version != before ? 1 : 0;
            case ".target off":
                return await SetActiveAsync(false) ? 1 : 0;
            case ".target on":
                return await SetActiveAsync(true) ? 1 : 0;
            default:
                return 0;
        }
    }

    public async Task ClearTargetAsync()
    {
        state.Clear();
        UpdateTargetInputs(state.Target);
        await SaveAsync("Ground target cleared.");
    }

    private async Task SaveAsync(string successMessage)
    {
        UpdateDisplay();
        try
        {
            await settingsStore.SaveAsync(state.CreateSnapshot());
            StatusMessage = successMessage;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException)
        {
            StatusMessage = "The target changed for this session but could not be saved: "
                + exception.Message;
        }
    }

    private void UpdateTargetInputs(SurfaceCoordinate target)
    {
        TargetLatitude = target.Latitude.ToString("G", CultureInfo.CurrentCulture);
        TargetLongitude = target.Longitude.ToString("G", CultureInfo.CurrentCulture);
    }

    private void UpdateDisplay()
    {
        CurrentCoordinates = state.CurrentLocation is SurfaceCoordinate current
            ? $"{current.Latitude:F6}, {current.Longitude:F6}"
            : Unavailable;
        TargetCoordinates = state.IsActive
            ? $"{state.Target.Latitude:F6}, {state.Target.Longitude:F6}"
            : Unavailable;
        OnPropertyChanged(nameof(IsTargetActive));
        OnPropertyChanged(nameof(TargetStatusLabel));
        OnPropertyChanged(nameof(ShouldShow));
        ((AsyncCommand)ClearTargetCommand).RaiseCanExecuteChanged();

        var solution = state.Solution;
        if (solution is null)
        {
            DistanceToTarget = Unavailable;
            TargetBearing = Unavailable;
            RelativeHeading = Unavailable;
            ApproachAngle = Unavailable;
            DescentAngle = Unavailable;
            RelativeBearingDegrees = 0;
            AttackAngleDegrees = 0;
            approach = null;
            ApproachStatus = state.IsActive
                ? "Move to a body surface to begin guidance"
                : "Set a target to begin guidance";
            RaiseApproachPropertiesChanged();
            return;
        }

        DistanceToTarget = FormatDistance(solution.Distance);
        TargetBearing = $"{solution.Bearing:N0}°";
        RelativeHeading = $"{solution.RelativeBearing:N0}° relative";
        ApproachAngle = $"{solution.AttackAngle:N0}°";
        DescentAngle = $"-{solution.AttackAngle:N0}°";
        RelativeBearingDegrees = solution.RelativeBearing;
        AttackAngleDegrees = solution.AttackAngle;
        approach = solution.Approach;
        ApproachStatus = solution.Approach switch
        {
            GroundTargetApproach.Level => "Level with target",
            GroundTargetApproach.Shallow => "Shallow approach",
            GroundTargetApproach.Ideal => "Ideal approach",
            GroundTargetApproach.Steep => "Steep approach",
            GroundTargetApproach.TooSteep => "Too steep",
            _ => solution.Approach.ToString(),
        };
        RaiseApproachPropertiesChanged();
    }

    private void RaiseApproachPropertiesChanged()
    {
        OnPropertyChanged(nameof(HasLevelApproach));
        OnPropertyChanged(nameof(HasShallowApproach));
        OnPropertyChanged(nameof(HasIdealApproach));
        OnPropertyChanged(nameof(HasSteepApproach));
        OnPropertyChanged(nameof(HasTooSteepApproach));
    }

    private static bool IsOverlayStatusEligible(
        EliteStatus status,
        string? musicTrack)
    {
        if (!status.HasLatitudeLongitude
            || status.PlanetRadius <= 0
            || status.InTaxi)
        {
            return false;
        }

        var mode = OverlayGameModeResolver.Resolve(
            status,
            musicTrack: musicTrack);
        return mode is OverlayGameMode.CommsPanel
            or OverlayGameMode.SuperCruising
            or OverlayGameMode.Flying
            or OverlayGameMode.Landed
            or OverlayGameMode.InSrv
            or OverlayGameMode.OnFoot
            or OverlayGameMode.GlideMode
            or OverlayGameMode.InFighter;
    }

    private static string FormatDistance(double distance)
    {
        return distance >= 1_000
            ? $"{distance / 1_000:N2} km"
            : $"{distance:N0} m";
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
