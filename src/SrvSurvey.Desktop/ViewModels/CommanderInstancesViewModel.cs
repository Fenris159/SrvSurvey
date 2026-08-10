using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class CommanderInstancesViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly CommanderProfileCatalog catalog;
    private readonly ICommanderInstanceLauncher launcher;
    private readonly string journalDirectory;
    private readonly IGameWindowSwitcher gameWindowSwitcher;
    private readonly AsyncCommand launchCommand;
    private readonly AsyncCommand refreshCommand;
    private IReadOnlyList<CommanderProfileIdentity> catalogProfiles = [];
    private IReadOnlyList<CommanderInstanceOptionViewModel> commanders = [];
    private CommanderInstanceOptionViewModel? selectedCommander;
    private string? currentFrontierId;
    private string? currentCommanderName;
    private string statusMessage = "Commander profiles have not been scanned yet.";
    private bool isBusy;
    private int availableGameWindowCount;

    public CommanderInstancesViewModel(
        CommanderProfileCatalog catalog,
        ICommanderInstanceLauncher launcher,
        string journalDirectory,
        string? currentFrontierId = null,
        IGameWindowSwitcher? gameWindowSwitcher = null)
    {
        this.catalog = catalog;
        this.launcher = launcher;
        this.journalDirectory = journalDirectory;
        this.gameWindowSwitcher = gameWindowSwitcher
            ?? GameWindowSwitcher.CreateCurrent();
        this.currentFrontierId = currentFrontierId;
        launchCommand = new AsyncCommand(LaunchSelectedAsync, CanLaunchSelected);
        refreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy);
        LaunchCommand = launchCommand;
        RefreshCommand = refreshCommand;
        SwitchWindowCommand = new RelayCommand(SwitchToNextGameWindow);
        RefreshGameWindowCount();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<CommanderInstanceOptionViewModel> Commanders
    {
        get => commanders;
        private set => SetField(ref commanders, value);
    }

    public CommanderInstanceOptionViewModel? SelectedCommander
    {
        get => selectedCommander;
        set
        {
            if (SetField(ref selectedCommander, value))
            {
                launchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string CurrentCommander
    {
        get
        {
            if (string.IsNullOrWhiteSpace(currentCommanderName))
            {
                return currentFrontierId ?? "Waiting for journal identity";
            }

            return string.IsNullOrWhiteSpace(currentFrontierId)
                ? currentCommanderName
                : $"{currentCommanderName} ({currentFrontierId})";
        }
    }

    public string MultiGameOverlayLabel =>
        $"~ {(!string.IsNullOrWhiteSpace(currentCommanderName)
            ? currentCommanderName
            : currentFrontierId ?? "?")} ~";

    public int AvailableGameWindowCount
    {
        get => availableGameWindowCount;
        private set
        {
            if (SetField(ref availableGameWindowCount, value))
            {
                OnPropertyChanged(nameof(HasMultipleGameWindows));
            }
        }
    }

    public bool HasMultipleGameWindows => AvailableGameWindowCount > 1;

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                launchCommand.RaiseCanExecuteChanged();
                refreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand LaunchCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand SwitchWindowCommand { get; }

    public bool SwitchToNextGameWindow()
    {
        var switched = gameWindowSwitcher.TryActivateNext();
        RefreshGameWindowCount();
        StatusMessage = switched
            ? "Focused the next Elite Dangerous window; overlays will follow it."
            : "No available Elite Dangerous window could be focused.";
        return switched;
    }

    public void RefreshGameWindowCount()
    {
        AvailableGameWindowCount = Math.Max(
            0,
            gameWindowSwitcher.GetAvailableWindowCount());
    }

    public void Dispose()
    {
        gameWindowSwitcher.Dispose();
    }

    public void UpdateCurrent(string? frontierId, string? commanderName)
    {
        currentFrontierId = string.IsNullOrWhiteSpace(frontierId)
            ? currentFrontierId
            : frontierId.Trim();
        currentCommanderName = string.IsNullOrWhiteSpace(commanderName)
            ? currentCommanderName
            : commanderName.Trim();
        RebuildOptions();
        OnPropertyChanged(nameof(CurrentCommander));
        OnPropertyChanged(nameof(MultiGameOverlayLabel));
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
            var result = await catalog.LoadAsync();
            catalogProfiles = result.Profiles;
            RebuildOptions();
            if (result.Warnings.Count > 0)
            {
                StatusMessage = $"Found {result.Profiles.Count:N0} commander profile(s). "
                    + string.Join(" ", result.Warnings);
            }
            else if (Directory.Exists(journalDirectory))
            {
                if (result.Profiles.Count == 0)
                {
                    StatusMessage =
                        "No saved commander profiles were found. Import the original profile or start Elite once.";
                }
                else if (Commanders.Count == 0)
                {
                    StatusMessage = "Only the current commander profile is available.";
                }
                else
                {
                    StatusMessage =
                        $"Choose one of {Commanders.Count:N0} other commander profile(s) to launch.";
                }
            }
            else
            {
                StatusMessage = "The Elite journal folder is unavailable; configure it before launching another commander instance.";
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "Commander profiles could not be scanned: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LaunchSelectedAsync()
    {
        if (!CanLaunchSelected() || SelectedCommander is not { } selected)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await launcher.LaunchAsync(selected.FrontierId, journalDirectory);
            StatusMessage = $"Started another SrvSurvey instance for {selected.DisplayName}.";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or System.ComponentModel.Win32Exception)
        {
            StatusMessage = "The additional SrvSurvey instance could not start: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanLaunchSelected()
    {
        return !IsBusy
            && SelectedCommander is not null
            && Directory.Exists(journalDirectory)
            && !string.Equals(
                SelectedCommander.FrontierId,
                currentFrontierId,
                StringComparison.OrdinalIgnoreCase);
    }

    private void RebuildOptions()
    {
        var selectedFrontierId = SelectedCommander?.FrontierId;
        Commanders = catalogProfiles
            .Where(profile => !string.Equals(
                profile.FrontierId,
                currentFrontierId,
                StringComparison.OrdinalIgnoreCase))
            .Select(profile => new CommanderInstanceOptionViewModel(profile))
            .ToArray();
        SelectedCommander = Commanders.FirstOrDefault(option => string.Equals(
                option.FrontierId,
                selectedFrontierId,
                StringComparison.OrdinalIgnoreCase))
            ?? (Commanders.Count > 0 ? Commanders[0] : null);
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

    private sealed class RelayCommand(Func<bool> execute) : ICommand
    {
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            _ = execute();
        }
    }
}

public sealed class CommanderInstanceOptionViewModel(
    CommanderProfileIdentity identity)
{
    public string FrontierId { get; } = identity.FrontierId;

    public string CommanderName { get; } = identity.CommanderName;

    public string Modes => (identity.HasLiveProfile, identity.HasLegacyProfile) switch
    {
        (true, true) => "Odyssey and legacy",
        (true, false) => "Odyssey",
        _ => "Legacy",
    };

    public string DisplayName => $"{CommanderName} ({FrontierId})";
}
