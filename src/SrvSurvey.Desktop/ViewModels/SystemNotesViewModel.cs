using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class SystemNotesViewModel : INotifyPropertyChanged
{
    private const string Unavailable = "\u2014";

    private readonly SystemNoteStore noteStore;
    private readonly SystemNotesSettingsStore settingsStore;
    private readonly JourneyService? journeyService;
    private readonly AsyncCommand openWindowCommand;
    private readonly AsyncCommand openCanonnCommand;
    private readonly AsyncCommand openSpanshCommand;
    private readonly AsyncCommand openEdsmCommand;
    private readonly AsyncCommand openImagesCommand;
    private SystemNoteContext? currentContext;
    private SystemNoteContext? loadedContext;
    private string notes = string.Empty;
    private bool isDirty;
    private bool isBusy;
    private bool alwaysOnTop;
    private string statusMessage;
    private string? imagesDirectory;
    private Func<Task<bool>>? windowOpener;
    private Func<Uri, Task<bool>>? uriLauncher;
    private Func<DirectoryInfo, Task<bool>>? directoryLauncher;
    private bool isApplyingLoad;

    public SystemNotesViewModel(
        SystemNoteStore noteStore,
        SystemNotesSettingsStore settingsStore,
        JourneyService? journeyService = null)
    {
        this.noteStore = noteStore
            ?? throw new ArgumentNullException(nameof(noteStore));
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        this.journeyService = journeyService;
        var settings = settingsStore.Load();
        alwaysOnTop = settings.Snapshot?.AlwaysOnTop ?? false;
        statusMessage = settings.IsSuccess
            ? "Open notes for the current system."
            : settings.Error ?? "The system-notes settings could not be loaded.";
        openWindowCommand = new AsyncCommand(OpenWindowAsync, CanOpenWindow);
        openCanonnCommand = new AsyncCommand(
            OpenCanonnAsync,
            HasLoadedSystem);
        openSpanshCommand = new AsyncCommand(
            OpenSpanshAsync,
            HasLoadedSystemAddress);
        openEdsmCommand = new AsyncCommand(
            OpenEdsmAsync,
            HasLoadedSystemAddress);
        openImagesCommand = new AsyncCommand(
            OpenImagesAsync,
            () => HasImagesDirectory);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool HasCurrentSystem => currentContext is not null;

    public string SystemName => loadedContext?.SystemName
        ?? currentContext?.SystemName
        ?? Unavailable;

    public string SystemAddress => (loadedContext ?? currentContext) is { } context
        ? context.SystemAddress.ToString()
        : Unavailable;

    public string Notes
    {
        get => notes;
        set
        {
            if (!SetField(ref notes, value ?? string.Empty)
                || isApplyingLoad)
            {
                return;
            }

            IsDirty = true;
        }
    }

    public bool IsDirty
    {
        get => isDirty;
        private set
        {
            if (SetField(ref isDirty, value))
            {
                OnPropertyChanged(nameof(SaveButtonText));
            }
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!SetField(ref isBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SaveButtonText));
            RaiseCommands();
        }
    }

    public bool AlwaysOnTop
    {
        get => alwaysOnTop;
        private set => SetField(ref alwaysOnTop, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public string SaveButtonText => IsBusy ? "Saving\u2026" : "Save notes";

    public bool HasImagesDirectory => !string.IsNullOrWhiteSpace(imagesDirectory)
        && Directory.Exists(imagesDirectory);

    public string? ImagesDirectory => imagesDirectory;

    public ICommand OpenWindowCommand => openWindowCommand;

    public ICommand OpenCanonnCommand => openCanonnCommand;

    public ICommand OpenSpanshCommand => openSpanshCommand;

    public ICommand OpenEdsmCommand => openEdsmCommand;

    public ICommand OpenImagesCommand => openImagesCommand;

    public void UpdateContext(
        string? frontierId,
        string? commanderName,
        string? systemName,
        long? systemAddress,
        GalacticCoordinate? starPosition)
    {
        var next = string.IsNullOrWhiteSpace(frontierId)
            || string.IsNullOrWhiteSpace(systemName)
            || systemAddress is null or <= 0
                ? null
                : new SystemNoteContext(
                    frontierId,
                    commanderName,
                    systemName,
                    systemAddress.Value,
                    starPosition);
        if (IsSameSystem(currentContext, next))
        {
            currentContext = next;
            return;
        }

        currentContext = next;
        OnPropertyChanged(nameof(HasCurrentSystem));
        OnPropertyChanged(nameof(SystemName));
        OnPropertyChanged(nameof(SystemAddress));
        openWindowCommand.RaiseCanExecuteChanged();
        if (next is null)
        {
            StatusMessage = "Waiting for the current commander and system.";
        }
        else if (loadedContext is null)
        {
            StatusMessage = $"Notes are available for {next.SystemName}.";
        }
    }

    public void SetWindowOpener(Func<Task<bool>>? opener)
    {
        windowOpener = opener;
        openWindowCommand.RaiseCanExecuteChanged();
    }

    public void SetPlatformServices(
        Func<Uri, Task<bool>>? launchUri,
        Func<DirectoryInfo, Task<bool>>? launchDirectory)
    {
        uriLauncher = launchUri;
        directoryLauncher = launchDirectory;
    }

    public async Task<bool> LoadCurrentAsync()
    {
        if (currentContext is not { } context)
        {
            StatusMessage = "Waiting for the current commander and system.";
            return false;
        }

        try
        {
            IsBusy = true;
            StatusMessage = $"Loading notes for {context.SystemName}\u2026";
            var result = await noteStore.LoadAsync(
                context.FrontierId,
                context.SystemName,
                context.SystemAddress);
            if (!result.IsSuccess)
            {
                StatusMessage = result.Error
                    ?? "The system notes could not be loaded.";
                return false;
            }

            loadedContext = context;
            isApplyingLoad = true;
            try
            {
                Notes = result.Notes ?? string.Empty;
            }
            finally
            {
                isApplyingLoad = false;
            }

            IsDirty = false;
            imagesDirectory = settingsStore.GetImagesDirectory(
                context.SystemName);
            OnPropertyChanged(nameof(SystemName));
            OnPropertyChanged(nameof(SystemAddress));
            OnPropertyChanged(nameof(ImagesDirectory));
            OnPropertyChanged(nameof(HasImagesDirectory));
            StatusMessage = result.Exists
                ? $"Loaded notes from {Path.GetFileName(result.Path)}."
                : "No saved notes exist yet; saving will create compatible system data.";
            RaiseCommands();
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException)
        {
            StatusMessage = "The system notes could not be loaded: "
                + exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> SaveAsync()
    {
        if (loadedContext is not { } context)
        {
            StatusMessage = "Open notes for a current system before saving.";
            return false;
        }

        try
        {
            IsBusy = true;
            var path = await noteStore.SaveAsync(context, Notes);
            if (journeyService is not null)
            {
                await journeyService.IncrementNoteCountAsync(
                    context.SystemAddress);
            }

            IsDirty = false;
            StatusMessage = $"Saved notes to {Path.GetFileName(path)}.";
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            StatusMessage = "The system notes were not saved: "
                + exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SetAlwaysOnTopAsync(bool value)
    {
        if (value == AlwaysOnTop)
        {
            return;
        }

        AlwaysOnTop = value;
        try
        {
            await settingsStore.SaveAlwaysOnTopAsync(value);
            StatusMessage = value
                ? "System notes will stay above other windows."
                : "Always on top is off.";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            AlwaysOnTop = !value;
            StatusMessage = "The always-on-top preference was not saved: "
                + exception.Message;
        }
    }

    public Task OpenCanonnAsync()
    {
        if (loadedContext is not { } context)
        {
            return ReportMissingSystemAsync();
        }

        var system = Uri.EscapeDataString(context.SystemName);
        return LaunchUriAsync(
            new Uri(
                "https://canonn-science.github.io/canonn-signals/?system="
                    + system),
            "Canonn Signals");
    }

    public Task OpenSpanshAsync()
    {
        return LaunchSystemAddressAsync(
            address => new Uri($"https://spansh.co.uk/system/{address}"),
            "Spansh");
    }

    public Task OpenEdsmAsync()
    {
        return LaunchSystemAddressAsync(
            address => new Uri(
                $"https://www.edsm.net/en/system?systemID64={address}"),
            "EDSM");
    }

    public async Task OpenImagesAsync()
    {
        if (!HasImagesDirectory || imagesDirectory is null)
        {
            StatusMessage = "No screenshot folder exists for this system.";
            return;
        }

        if (directoryLauncher is null)
        {
            StatusMessage = "The desktop folder launcher is not available.";
            return;
        }

        try
        {
            var launched = await directoryLauncher(new DirectoryInfo(imagesDirectory));
            StatusMessage = launched
                ? "Opened the system screenshot folder."
                : "The operating system could not open the screenshot folder.";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or NotSupportedException
                or UnauthorizedAccessException)
        {
            StatusMessage = "The screenshot folder could not be opened: "
                + exception.Message;
        }
    }

    public void CloseSession()
    {
        loadedContext = null;
        imagesDirectory = null;
        isApplyingLoad = true;
        try
        {
            Notes = string.Empty;
        }
        finally
        {
            isApplyingLoad = false;
        }

        IsDirty = false;
        OnPropertyChanged(nameof(SystemName));
        OnPropertyChanged(nameof(SystemAddress));
        OnPropertyChanged(nameof(ImagesDirectory));
        OnPropertyChanged(nameof(HasImagesDirectory));
        RaiseCommands();
    }

    private bool CanOpenWindow()
    {
        return currentContext is not null
            && windowOpener is not null
            && !IsBusy;
    }

    private async Task OpenWindowAsync()
    {
        if (windowOpener is null)
        {
            StatusMessage = "The system-notes window is not available.";
            return;
        }

        if (!await windowOpener())
        {
            StatusMessage = "System notes require a current commander and system.";
        }
    }

    private bool HasLoadedSystem()
    {
        return loadedContext is not null && !IsBusy;
    }

    private bool HasLoadedSystemAddress()
    {
        return loadedContext?.SystemAddress > 0 && !IsBusy;
    }

    private Task LaunchSystemAddressAsync(
        Func<long, Uri> createUri,
        string label)
    {
        if (loadedContext is not { SystemAddress: > 0 } context)
        {
            return ReportMissingSystemAsync();
        }

        return LaunchUriAsync(createUri(context.SystemAddress), label);
    }

    private async Task LaunchUriAsync(Uri uri, string label)
    {
        if (uriLauncher is null)
        {
            StatusMessage = "The desktop link launcher is not available.";
            return;
        }

        try
        {
            var launched = await uriLauncher(uri);
            StatusMessage = launched
                ? $"Opened {label}."
                : $"The operating system could not open {label}.";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or NotSupportedException
                or UriFormatException)
        {
            StatusMessage = $"{label} could not be opened: "
                + exception.Message;
        }
    }

    private Task ReportMissingSystemAsync()
    {
        StatusMessage = "Open notes for a current system first.";
        return Task.CompletedTask;
    }

    private void RaiseCommands()
    {
        openWindowCommand.RaiseCanExecuteChanged();
        openCanonnCommand.RaiseCanExecuteChanged();
        openSpanshCommand.RaiseCanExecuteChanged();
        openEdsmCommand.RaiseCanExecuteChanged();
        openImagesCommand.RaiseCanExecuteChanged();
    }

    private static bool IsSameSystem(
        SystemNoteContext? left,
        SystemNoteContext? right)
    {
        return left is null && right is null
            || left is not null
                && right is not null
                && left.SystemAddress == right.SystemAddress
                && string.Equals(
                    left.FrontierId,
                    right.FrontierId,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    left.SystemName,
                    right.SystemName,
                    StringComparison.OrdinalIgnoreCase);
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
