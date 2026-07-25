using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class CommanderInstancesViewModel : INotifyPropertyChanged
{
    private readonly CommanderProfileCatalog catalog;
    private readonly ICommanderInstanceLauncher launcher;
    private readonly string journalDirectory;
    private readonly AsyncCommand launchCommand;
    private readonly AsyncCommand refreshCommand;
    private IReadOnlyList<CommanderProfileIdentity> catalogProfiles = [];
    private IReadOnlyList<CommanderInstanceOptionViewModel> commanders = [];
    private CommanderInstanceOptionViewModel? selectedCommander;
    private string? currentFrontierId;
    private string? currentCommanderName;
    private string statusMessage = "Commander profiles have not been scanned yet.";
    private bool isBusy;

    public CommanderInstancesViewModel(
        CommanderProfileCatalog catalog,
        ICommanderInstanceLauncher launcher,
        string journalDirectory,
        string? currentFrontierId = null)
    {
        this.catalog = catalog;
        this.launcher = launcher;
        this.journalDirectory = journalDirectory;
        this.currentFrontierId = currentFrontierId;
        launchCommand = new AsyncCommand(LaunchSelectedAsync, CanLaunchSelected);
        refreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy);
        LaunchCommand = launchCommand;
        RefreshCommand = refreshCommand;
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

    public string CurrentCommander => string.IsNullOrWhiteSpace(currentCommanderName)
        ? currentFrontierId ?? "Waiting for journal identity"
        : string.IsNullOrWhiteSpace(currentFrontierId)
            ? currentCommanderName
            : $"{currentCommanderName} ({currentFrontierId})";

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
            StatusMessage = result.Warnings.Count > 0
                ? $"Found {result.Profiles.Count:N0} commander profile(s). "
                    + string.Join(" ", result.Warnings)
                : !Directory.Exists(journalDirectory)
                    ? "The Elite journal folder is unavailable; configure it before launching another commander instance."
                : result.Profiles.Count == 0
                    ? "No saved commander profiles were found. Import the original profile or start Elite once."
                    : Commanders.Count == 0
                        ? "Only the current commander profile is available."
                        : $"Choose one of {Commanders.Count:N0} other commander profile(s) to launch.";
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
            ?? Commanders.FirstOrDefault();
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
