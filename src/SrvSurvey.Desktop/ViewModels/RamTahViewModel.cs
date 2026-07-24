using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class RamTahViewModel : INotifyPropertyChanged
{
    private readonly CommanderProfileStore profileStore;
    private readonly RamTahState state = new();
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private readonly RelayCommand requestAncientRuinsResetCommand;
    private readonly RelayCommand cancelAncientRuinsResetCommand;
    private readonly AsyncCommand confirmAncientRuinsResetCommand;
    private readonly RelayCommand requestGuardianLogsResetCommand;
    private readonly RelayCommand cancelGuardianLogsResetCommand;
    private readonly AsyncCommand confirmGuardianLogsResetCommand;
    private string? frontierId;
    private string? commanderName;
    private bool isOdyssey = true;
    private string statusMessage = "Waiting for a commander profile.";
    private bool isAncientRuinsResetPending;
    private bool isGuardianLogsResetPending;

    public RamTahViewModel(CommanderProfileStore profileStore)
    {
        this.profileStore = profileStore
            ?? throw new ArgumentNullException(nameof(profileStore));
        AncientRuinsGroups =
        [
            CreateGroup("Biology", RamTahMission.AncientRuins, 'B', 19),
            CreateGroup("Culture", RamTahMission.AncientRuins, 'C', 20),
            CreateGroup("History", RamTahMission.AncientRuins, 'H', 21),
            CreateGroup("Language", RamTahMission.AncientRuins, 'L', 21),
            CreateGroup("Technology", RamTahMission.AncientRuins, 'T', 20),
        ];
        GuardianLogsGroups =
        [
            CreateGroup("Thargoids", RamTahMission.GuardianLogs, 1, 5),
            CreateGroup("Civil war", RamTahMission.GuardianLogs, 6, 10),
            CreateGroup("Technology", RamTahMission.GuardianLogs, 11, 23),
            CreateGroup("Language", RamTahMission.GuardianLogs, 24, 24),
            CreateGroup("Body Protectorate", RamTahMission.GuardianLogs, 25, 28),
        ];
        requestAncientRuinsResetCommand = new RelayCommand(
            RequestAncientRuinsReset,
            () => state.AncientRuinsLogs.Count > 0
                && !IsAncientRuinsResetPending);
        cancelAncientRuinsResetCommand = new RelayCommand(
            CancelAncientRuinsReset,
            () => IsAncientRuinsResetPending);
        confirmAncientRuinsResetCommand = new AsyncCommand(
            ConfirmAncientRuinsResetAsync,
            () => IsAncientRuinsResetPending);
        requestGuardianLogsResetCommand = new RelayCommand(
            RequestGuardianLogsReset,
            () => state.GuardianLogs.Count > 0
                && !IsGuardianLogsResetPending);
        cancelGuardianLogsResetCommand = new RelayCommand(
            CancelGuardianLogsReset,
            () => IsGuardianLogsResetPending);
        confirmGuardianLogsResetCommand = new AsyncCommand(
            ConfirmGuardianLogsResetAsync,
            () => IsGuardianLogsResetPending);
        RequestAncientRuinsResetCommand = requestAncientRuinsResetCommand;
        CancelAncientRuinsResetCommand = cancelAncientRuinsResetCommand;
        ConfirmAncientRuinsResetCommand = confirmAncientRuinsResetCommand;
        RequestGuardianLogsResetCommand = requestGuardianLogsResetCommand;
        CancelGuardianLogsResetCommand = cancelGuardianLogsResetCommand;
        ConfirmGuardianLogsResetCommand = confirmGuardianLogsResetCommand;
        UpdateDisplay();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<RamTahLogGroupViewModel> AncientRuinsGroups { get; }

    public IReadOnlyList<RamTahLogGroupViewModel> GuardianLogsGroups { get; }

    public ICommand RequestAncientRuinsResetCommand { get; }

    public ICommand CancelAncientRuinsResetCommand { get; }

    public ICommand ConfirmAncientRuinsResetCommand { get; }

    public ICommand RequestGuardianLogsResetCommand { get; }

    public ICommand CancelGuardianLogsResetCommand { get; }

    public ICommand ConfirmGuardianLogsResetCommand { get; }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public bool IsAncientRuinsResetPending
    {
        get => isAncientRuinsResetPending;
        private set
        {
            if (SetField(ref isAncientRuinsResetPending, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsGuardianLogsResetPending
    {
        get => isGuardianLogsResetPending;
        private set
        {
            if (SetField(ref isGuardianLogsResetPending, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string AncientRuinsMissionStatus =>
        state.AncientRuinsMissionStatus.ToString();

    public string GuardianLogsMissionStatus =>
        state.GuardianLogsMissionStatus.ToString();

    public string AncientRuinsProgressText =>
        $"{state.AncientRuinsLogs.Count:N0} of {RamTahState.AncientRuinsLogCount:N0} logs • "
        + $"{state.AncientRuinsProgress:N0}%";

    public string GuardianLogsProgressText =>
        $"{state.GuardianLogs.Count:N0} of {RamTahState.GuardianLogsCount:N0} logs • "
        + $"{state.GuardianLogsProgress:N0}%";

    public double AncientRuinsProgress => state.AncientRuinsProgress;

    public double GuardianLogsProgress => state.GuardianLogsProgress;

    public bool IsAnyMissionActive => state.IsAnyMissionActive;

    public void LoadProfile(
        string profileFrontierId,
        string? profileCommanderName,
        bool profileIsOdyssey,
        RamTahSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileFrontierId);
        ArgumentNullException.ThrowIfNull(snapshot);
        frontierId = profileFrontierId;
        commanderName = profileCommanderName;
        isOdyssey = profileIsOdyssey;
        state.Reset(snapshot);
        IsAncientRuinsResetPending = false;
        IsGuardianLogsResetPending = false;
        StatusMessage = "Loaded legacy-compatible Ram Tah mission progress.";
        UpdateDisplay();
    }

    public void SetProfileError(string message)
    {
        frontierId = null;
        state.Reset();
        IsAncientRuinsResetPending = false;
        IsGuardianLogsResetPending = false;
        StatusMessage = message;
        UpdateDisplay();
    }

    public async Task ApplyJournalEventsAsync(
        IEnumerable<JournalEventEnvelope> journalEvents)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        await operationLock.WaitAsync();
        try
        {
            var changed = false;
            foreach (var journalEvent in journalEvents)
            {
                changed |= state.Apply(journalEvent);
            }

            if (changed)
            {
                UpdateDisplay();
                await SaveAsync("Ram Tah mission status updated from the journal.");
            }
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task ToggleLogAsync(RamTahMission mission, string code)
    {
        if (frontierId is null)
        {
            StatusMessage = "A commander profile is required to change mission progress.";
            return;
        }

        await operationLock.WaitAsync();
        try
        {
            state.ToggleLog(mission, code);
            UpdateDisplay();
            await SaveAsync($"Saved {code} Ram Tah progress.");
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<bool> SetLogCompletedAsync(
        RamTahMission mission,
        string code,
        bool completed)
    {
        if (frontierId is null)
        {
            StatusMessage = "A commander profile is required to change mission progress.";
            return false;
        }

        await operationLock.WaitAsync();
        try
        {
            var changed = state.SetLog(mission, code, completed);
            if (changed)
            {
                UpdateDisplay();
                await SaveAsync($"Saved {code} Ram Tah progress.");
            }

            return changed;
        }
        finally
        {
            operationLock.Release();
        }
    }

    public void RequestAncientRuinsReset()
    {
        if (state.AncientRuinsLogs.Count > 0)
        {
            IsAncientRuinsResetPending = true;
        }
    }

    public void CancelAncientRuinsReset()
    {
        IsAncientRuinsResetPending = false;
    }

    public void RequestGuardianLogsReset()
    {
        if (state.GuardianLogs.Count > 0)
        {
            IsGuardianLogsResetPending = true;
        }
    }

    public void CancelGuardianLogsReset()
    {
        IsGuardianLogsResetPending = false;
    }

    public Task ConfirmAncientRuinsResetAsync()
    {
        return ConfirmResetAsync(RamTahMission.AncientRuins);
    }

    public Task ConfirmGuardianLogsResetAsync()
    {
        return ConfirmResetAsync(RamTahMission.GuardianLogs);
    }

    public bool IsLogCompleted(RamTahMission mission, string code)
    {
        return mission == RamTahMission.AncientRuins
            ? state.AncientRuinsLogs.Contains(code)
            : state.GuardianLogs.Contains(code);
    }

    public void ReportGuideLaunchFailure(string message)
    {
        StatusMessage = message;
    }

    private RamTahLogGroupViewModel CreateGroup(
        string name,
        RamTahMission mission,
        char category,
        int count)
    {
        return new RamTahLogGroupViewModel(
            name,
            Enumerable.Range(1, count)
                .Select(index => CreateLog(mission, $"{category}{index}"))
                .ToArray());
    }

    private RamTahLogGroupViewModel CreateGroup(
        string name,
        RamTahMission mission,
        int first,
        int last)
    {
        return new RamTahLogGroupViewModel(
            name,
            Enumerable.Range(first, last - first + 1)
                .Select(index => CreateLog(mission, $"#{index}"))
                .ToArray());
    }

    private RamTahLogViewModel CreateLog(RamTahMission mission, string code)
    {
        return new RamTahLogViewModel(
            code,
            () => ToggleLogAsync(mission, code));
    }

    private async Task ConfirmResetAsync(RamTahMission mission)
    {
        await operationLock.WaitAsync();
        try
        {
            var changed = state.Clear(mission);
            if (mission == RamTahMission.AncientRuins)
            {
                IsAncientRuinsResetPending = false;
            }
            else
            {
                IsGuardianLogsResetPending = false;
            }

            if (changed)
            {
                UpdateDisplay();
                await SaveAsync("Cleared the selected Ram Tah mission progress.");
            }
        }
        finally
        {
            operationLock.Release();
        }
    }

    private async Task SaveAsync(string successMessage)
    {
        if (frontierId is null)
        {
            return;
        }

        try
        {
            await profileStore.SaveRamTahAsync(
                frontierId,
                commanderName,
                isOdyssey,
                state.CreateSnapshot());
            StatusMessage = successMessage;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            StatusMessage = "Ram Tah progress changed for this session but could not be saved: "
                + exception.Message;
        }
    }

    private void UpdateDisplay()
    {
        foreach (var group in AncientRuinsGroups.Concat(GuardianLogsGroups))
        {
            foreach (var log in group.Logs)
            {
                log.Update(state.AncientRuinsLogs.Contains(log.Code)
                    || state.GuardianLogs.Contains(log.Code));
            }
        }

        OnPropertyChanged(nameof(AncientRuinsMissionStatus));
        OnPropertyChanged(nameof(GuardianLogsMissionStatus));
        OnPropertyChanged(nameof(AncientRuinsProgressText));
        OnPropertyChanged(nameof(GuardianLogsProgressText));
        OnPropertyChanged(nameof(AncientRuinsProgress));
        OnPropertyChanged(nameof(GuardianLogsProgress));
        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        requestAncientRuinsResetCommand.RaiseCanExecuteChanged();
        cancelAncientRuinsResetCommand.RaiseCanExecuteChanged();
        confirmAncientRuinsResetCommand.RaiseCanExecuteChanged();
        requestGuardianLogsResetCommand.RaiseCanExecuteChanged();
        cancelGuardianLogsResetCommand.RaiseCanExecuteChanged();
        confirmGuardianLogsResetCommand.RaiseCanExecuteChanged();
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

    private sealed class RelayCommand(Action execute, Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return canExecute();
        }

        public void Execute(object? parameter)
        {
            if (CanExecute(parameter))
            {
                execute();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
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

public sealed record RamTahLogGroupViewModel(
    string Name,
    IReadOnlyList<RamTahLogViewModel> Logs);

public sealed class RamTahLogViewModel : INotifyPropertyChanged
{
    private bool isCompleted;

    public RamTahLogViewModel(string code, Func<Task> toggle)
    {
        Code = code;
        ToggleCommand = new AsyncCommand(toggle);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Code { get; }

    public bool IsCompleted
    {
        get => isCompleted;
        private set
        {
            if (isCompleted == value)
            {
                return;
            }

            isCompleted = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(IsCompleted)));
        }
    }

    public ICommand ToggleCommand { get; }

    internal void Update(bool completed)
    {
        IsCompleted = completed;
    }

    private sealed class AsyncCommand(Func<Task> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter)
        {
            return true;
        }

        public async void Execute(object? parameter)
        {
            await execute();
        }
    }
}
