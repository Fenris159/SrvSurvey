using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class VisitedStarsCacheViewModel : INotifyPropertyChanged
{
    private readonly CommanderProfileCatalog commanderCatalog;
    private readonly IVisitedStarsCacheService cacheService;
    private readonly Func<string, string?> targetResolver;
    private readonly Func<bool> isGameRunning;
    private readonly AsyncCommand swapCommand;
    private readonly AsyncCommand restoreCommand;
    private readonly AsyncCommand refreshCommand;
    private IReadOnlyList<VisitedStarsCommanderOptionViewModel> commanders = [];
    private VisitedStarsCommanderOptionViewModel? selectedCommander;
    private string? currentFrontierId;
    private string? lastResolvedTarget;
    private string systemName = string.Empty;
    private string targetPath = string.Empty;
    private string statusMessage = "Select Refresh to scan commander profiles and the game state.";
    private bool gameIsRunning;
    private bool isBusy;
    private bool swapPending;
    private bool restorePending;

    public VisitedStarsCacheViewModel(
        CommanderProfileCatalog commanderCatalog,
        IVisitedStarsCacheService cacheService,
        Func<string, string?> targetResolver,
        Func<bool> isGameRunning)
    {
        this.commanderCatalog = commanderCatalog;
        this.cacheService = cacheService;
        this.targetResolver = targetResolver;
        this.isGameRunning = isGameRunning;
        swapCommand = new AsyncCommand(SwapAsync, CanSwap);
        restoreCommand = new AsyncCommand(RestoreAsync, CanRestore);
        refreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy);
        SwapCommand = swapCommand;
        RestoreCommand = restoreCommand;
        RefreshCommand = refreshCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<VisitedStarsCommanderOptionViewModel> Commanders
    {
        get => commanders;
        private set => SetField(ref commanders, value);
    }

    public VisitedStarsCommanderOptionViewModel? SelectedCommander
    {
        get => selectedCommander;
        set
        {
            if (!SetField(ref selectedCommander, value))
            {
                return;
            }

            ResolveSelectedTarget();
            ResetConfirmations();
            RaiseCommandStates();
        }
    }

    public string SystemName
    {
        get => systemName;
        set
        {
            if (SetField(ref systemName, value?.Trim() ?? string.Empty))
            {
                ResetConfirmations();
                RaiseCommandStates();
            }
        }
    }

    public string TargetPath
    {
        get => targetPath;
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (SetField(ref targetPath, normalized))
            {
                if (!string.Equals(
                        normalized,
                        lastResolvedTarget,
                        PathComparison))
                {
                    lastResolvedTarget = null;
                }

                ResetConfirmations();
                OnPropertyChanged(nameof(BackupPath));
                OnPropertyChanged(nameof(HasBackup));
                RaiseCommandStates();
            }
        }
    }

    public string BackupPath
    {
        get
        {
            if (!IsValidCachePath(TargetPath))
            {
                return string.Empty;
            }

            try
            {
                return VisitedStarsCacheService.GetBackupPath(TargetPath);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or NotSupportedException
                    or PathTooLongException)
            {
                return string.Empty;
            }
        }
    }

    public bool HasBackup => !string.IsNullOrWhiteSpace(BackupPath)
        && File.Exists(BackupPath);

    public bool GameIsRunning
    {
        get => gameIsRunning;
        private set
        {
            if (SetField(ref gameIsRunning, value))
            {
                OnPropertyChanged(nameof(GameStateMessage));
                RaiseCommandStates();
            }
        }
    }

    public string GameStateMessage => GameIsRunning
        ? "Close Elite Dangerous before swapping or restoring this file."
        : "Elite Dangerous is not running; file operations are available.";

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                RaiseCommandStates();
                refreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public string SwapButtonText => swapPending ? "Confirm swap" : "Back up and swap";

    public string RestoreButtonText => restorePending
        ? "Confirm restore"
        : "Restore original";

    public ICommand SwapCommand { get; }

    public ICommand RestoreCommand { get; }

    public ICommand RefreshCommand { get; }

    public void UpdateContext(
        string? frontierId,
        string? commanderName,
        string? currentSystemName)
    {
        if (!string.IsNullOrWhiteSpace(frontierId))
        {
            currentFrontierId = frontierId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(currentSystemName))
        {
            SystemName = currentSystemName;
        }

        SelectCurrentCommander();
    }

    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            GameIsRunning = isGameRunning();
            var result = await commanderCatalog.LoadAsync();
            var previous = SelectedCommander?.FrontierId;
            Commanders = result.Profiles
                .Select(profile => new VisitedStarsCommanderOptionViewModel(profile))
                .ToArray();
            SelectedCommander = Commanders.FirstOrDefault(option => string.Equals(
                    option.FrontierId,
                    previous,
                    StringComparison.OrdinalIgnoreCase))
                ?? Commanders.FirstOrDefault(option => string.Equals(
                    option.FrontierId,
                    currentFrontierId,
                    StringComparison.OrdinalIgnoreCase))
                ?? (Commanders.Count > 0 ? Commanders[0] : null);
            StatusMessage = result.Warnings.Count > 0
                ? string.Join(" ", result.Warnings)
                : Commanders.Count == 0
                    ? "No commander profile is available. Import the original profile or start Elite once."
                    : "Choose a commander, reference system, and VisitedStarsCache.dat file.";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "Visited-stars setup could not be refreshed: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasBackup));
        }
    }

    public async Task SwapAsync()
    {
        if (!CanSwap())
        {
            return;
        }

        if (!swapPending)
        {
            swapPending = true;
            restorePending = false;
            OnPropertyChanged(nameof(SwapButtonText));
            OnPropertyChanged(nameof(RestoreButtonText));
            StatusMessage = "Review the commander, system, and target path, then select Confirm swap.";
            return;
        }

        try
        {
            IsBusy = true;
            ResetConfirmations();
            var result = await cacheService.SwapAsync(SystemName, TargetPath);
            StatusMessage = "Swap complete. Restart Elite Dangerous when ready. "
                + $"Original backup: {result.BackupPath}";
            OnPropertyChanged(nameof(HasBackup));
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            StatusMessage = "Visited-stars swap failed without replacing the original cache: "
                + exception.Message;
        }
        finally
        {
            GameIsRunning = isGameRunning();
            IsBusy = false;
        }
    }

    public async Task RestoreAsync()
    {
        if (!CanRestore())
        {
            return;
        }

        if (!restorePending)
        {
            restorePending = true;
            swapPending = false;
            OnPropertyChanged(nameof(SwapButtonText));
            OnPropertyChanged(nameof(RestoreButtonText));
            StatusMessage = "Review the target and select Confirm restore. The verified backup will be retained.";
            return;
        }

        try
        {
            IsBusy = true;
            ResetConfirmations();
            var result = await cacheService.RestoreAsync(TargetPath);
            StatusMessage = "Original cache restored and verified. Backup retained at "
                + result.BackupPath;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            StatusMessage = "Visited-stars restore failed without discarding the current cache: "
                + exception.Message;
        }
        finally
        {
            GameIsRunning = isGameRunning();
            IsBusy = false;
        }
    }

    private bool CanSwap()
    {
        return !IsBusy
            && !GameIsRunning
            && SelectedCommander is not null
            && !string.IsNullOrWhiteSpace(SystemName)
            && IsValidCachePath(TargetPath)
            && File.Exists(TargetPath);
    }

    private bool CanRestore()
    {
        return !IsBusy
            && !GameIsRunning
            && IsValidCachePath(TargetPath)
            && HasBackup;
    }

    private void SelectCurrentCommander()
    {
        if (currentFrontierId is null)
        {
            return;
        }

        SelectedCommander = Commanders.FirstOrDefault(option => string.Equals(
                option.FrontierId,
                currentFrontierId,
                StringComparison.OrdinalIgnoreCase))
            ?? SelectedCommander;
    }

    private void ResolveSelectedTarget()
    {
        if (SelectedCommander is null)
        {
            return;
        }

        var resolved = targetResolver(SelectedCommander.FrontierId);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(TargetPath)
            || string.Equals(TargetPath, lastResolvedTarget, PathComparison))
        {
            lastResolvedTarget = Path.GetFullPath(resolved);
            TargetPath = lastResolvedTarget;
        }
    }

    private void ResetConfirmations()
    {
        var changed = swapPending || restorePending;
        swapPending = false;
        restorePending = false;
        if (changed)
        {
            OnPropertyChanged(nameof(SwapButtonText));
            OnPropertyChanged(nameof(RestoreButtonText));
        }
    }

    private void RaiseCommandStates()
    {
        swapCommand.RaiseCanExecuteChanged();
        restoreCommand.RaiseCanExecuteChanged();
    }

    private static bool IsValidCachePath(string path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && string.Equals(
                Path.GetFileName(path),
                VisitedStarsCacheService.CacheFileName,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRecoverable(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or HttpRequestException
            or WebException;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

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

        public bool CanExecute(object? parameter) => canExecute();

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

public sealed class VisitedStarsCommanderOptionViewModel(
    CommanderProfileIdentity identity)
{
    public string FrontierId { get; } = identity.FrontierId;

    public string DisplayName => $"{identity.CommanderName} ({identity.FrontierId})";
}
