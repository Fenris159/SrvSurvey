using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using SrvSurvey.Core.Quests;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class QuestDeveloperViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly JsonSerializerOptions EditorJsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    private readonly QuestRuntimeCoordinator coordinator;
    private readonly SynchronizationContext? synchronizationContext;
    private readonly AsyncCommand refreshCommand;
    private readonly AsyncCommand applyCommand;
    private readonly AsyncCommand startChapterCommand;
    private readonly AsyncCommand stopChapterCommand;
    private readonly AsyncCommand runDebugCommand;
    private readonly AsyncCommand publishCommand;
    private readonly AsyncCommand reloadSavedCommand;
    private readonly AsyncCommand removeCommand;
    private readonly object watcherSync = new();
    private readonly SemaphoreSlim importLock = new(1, 1);
    private RavenQuestReference? reference;
    private QuestDevelopmentStateSnapshot? state;
    private IReadOnlyList<QuestDevelopmentViewOption> views = [];
    private QuestDevelopmentViewOption? selectedView;
    private FileSystemWatcher? watcher;
    private CancellationTokenSource? watcherReload;
    private string title = "No development quest loaded";
    private string versionLabel = string.Empty;
    private string sourceDirectory = string.Empty;
    private string editorJson = string.Empty;
    private string debugCode = string.Empty;
    private string debugResult = string.Empty;
    private string statusMessage = "Import a legacy quest development folder to begin.";
    private bool isBusy;
    private bool watchSource;
    private bool publishConfirmed;
    private bool removePending;
    private bool disposed;

    public QuestDeveloperViewModel(QuestRuntimeCoordinator coordinator)
    {
        this.coordinator = coordinator
            ?? throw new ArgumentNullException(nameof(coordinator));
        synchronizationContext = SynchronizationContext.Current;
        refreshCommand = new AsyncCommand(RefreshStateAsync, CanUseQuest);
        applyCommand = new AsyncCommand(ApplyEditorAsync, CanApplyEditor);
        startChapterCommand = new AsyncCommand(
            () => SetSelectedChapterActiveAsync(active: true),
            () => CanUseQuest() && IsChapterSelected && !IsSelectedChapterActive);
        stopChapterCommand = new AsyncCommand(
            () => SetSelectedChapterActiveAsync(active: false),
            () => CanUseQuest() && IsSelectedChapterActive);
        runDebugCommand = new AsyncCommand(
            RunDebugAsync,
            () => CanUseQuest()
                && IsSelectedChapterActive
                && !string.IsNullOrWhiteSpace(DebugCode));
        publishCommand = new AsyncCommand(
            PublishAsync,
            () => CanUseQuest() && PublishConfirmed);
        reloadSavedCommand = new AsyncCommand(ReloadSavedAsync, CanUseQuest);
        removeCommand = new AsyncCommand(RemoveAsync, CanUseQuest);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? RuntimeChanged;

    public bool HasDevelopmentQuest => reference is not null;

    public string Title
    {
        get => title;
        private set => SetField(ref title, value);
    }

    public string VersionLabel
    {
        get => versionLabel;
        private set => SetField(ref versionLabel, value);
    }

    public string SourceDirectory
    {
        get => sourceDirectory;
        private set
        {
            if (SetField(ref sourceDirectory, value))
            {
                OnPropertyChanged(nameof(CanWatchSource));
                RaiseCommandStates();
            }
        }
    }

    public bool CanWatchSource => Directory.Exists(SourceDirectory);

    public bool WatchSource
    {
        get => watchSource;
        set
        {
            if (!SetField(ref watchSource, value))
            {
                return;
            }

            if (value)
            {
                StartWatcher();
            }
            else
            {
                StopWatcher();
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
                RaiseCommandStates();
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public IReadOnlyList<QuestDevelopmentViewOption> Views
    {
        get => views;
        private set => SetField(ref views, value);
    }

    public QuestDevelopmentViewOption? SelectedView
    {
        get => selectedView;
        set
        {
            if (SetField(ref selectedView, value))
            {
                RenderEditor();
                OnPropertyChanged(nameof(IsChapterSelected));
                OnPropertyChanged(nameof(IsSelectedChapterActive));
                RaiseCommandStates();
            }
        }
    }

    public bool IsChapterSelected => SelectedView?.ChapterId is not null;

    public bool IsSelectedChapterActive => SelectedView?.ChapterId is { } id
        && state?.Chapters.FirstOrDefault(chapter => string.Equals(
            chapter.Id,
            id,
            StringComparison.Ordinal))?.IsActive == true;

    public string EditorJson
    {
        get => editorJson;
        set
        {
            if (SetField(ref editorJson, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string DebugCode
    {
        get => debugCode;
        set
        {
            if (SetField(ref debugCode, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string DebugResult
    {
        get => debugResult;
        private set => SetField(ref debugResult, value);
    }

    public bool PublishConfirmed
    {
        get => publishConfirmed;
        set
        {
            if (SetField(ref publishConfirmed, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string RemoveButtonText => removePending
        ? "Confirm removal"
        : "Remove development quest";

    public ICommand RefreshCommand => refreshCommand;

    public ICommand ApplyCommand => applyCommand;

    public ICommand StartChapterCommand => startChapterCommand;

    public ICommand StopChapterCommand => stopChapterCommand;

    public ICommand RunDebugCommand => runDebugCommand;

    public ICommand PublishCommand => publishCommand;

    public ICommand ReloadSavedCommand => reloadSavedCommand;

    public ICommand RemoveCommand => removeCommand;

    public void ApplyRuntimeSnapshots(
        IReadOnlyList<QuestRuntimeSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var development = snapshots.FirstOrDefault(snapshot =>
            snapshot.IsDevelopment);
        if (development is null)
        {
            ClearQuest();
            return;
        }

        var changed = reference is null
            || !SameQuest(reference, development.Reference)
            || reference.Version.CompareTo(development.Reference.Version) != 0;
        reference = development.Reference;
        Title = development.Title;
        VersionLabel = development.Reference.Version.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(HasDevelopmentQuest));
        if (changed)
        {
            state = null;
            Views = [];
            SelectedView = null;
            EditorJson = string.Empty;
        }

        removePending = false;
        OnPropertyChanged(nameof(RemoveButtonText));
        RaiseCommandStates();
    }

    public async Task ImportFolderAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await importLock.WaitAsync(CancellationToken.None);
        try
        {
            IsBusy = true;
            StatusMessage = "Validating and importing development quest...";
            var result = await coordinator.ImportDevelopmentQuestAsync(
                path,
                CancellationToken.None);
            reference = result.Reference;
            var restartWatcher = WatchSource
                && !string.Equals(
                    SourceDirectory,
                    result.SourceDirectory,
                    StringComparison.OrdinalIgnoreCase);
            if (restartWatcher)
            {
                WatchSource = false;
            }

            SourceDirectory = result.SourceDirectory;
            if (restartWatcher)
            {
                WatchSource = true;
            }

            ApplyRuntimeSnapshots(coordinator.Snapshot);
            await LoadStateCoreAsync();
            StatusMessage = result.Warnings.Count == 0
                ? $"Imported {result.SourceFiles.Count:N0} verified source files."
                : string.Join(Environment.NewLine, result.Warnings);
            RuntimeChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            StatusMessage = "Development quest import failed: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
            importLock.Release();
        }
    }

    public async Task RefreshStateAsync()
    {
        if (reference is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await LoadStateCoreAsync();
            StatusMessage = "Development state refreshed.";
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            StatusMessage = "Development state could not be loaded: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        StopWatcher();
    }

    private async Task LoadStateCoreAsync()
    {
        var currentReference = reference
            ?? throw new InvalidOperationException(
                "No development quest is active.");
        var selected = SelectedView;
        state = await coordinator.GetDevelopmentStateAsync(
            currentReference,
            CancellationToken.None);
        Views =
        [
            new QuestDevelopmentViewOption(
                QuestDevelopmentViewKind.Objectives,
                "Objectives",
                null),
            .. state.Chapters.Select(chapter => new QuestDevelopmentViewOption(
                QuestDevelopmentViewKind.Chapter,
                "Chapter: " + chapter.Id,
                chapter.Id)),
            new QuestDevelopmentViewOption(
                QuestDevelopmentViewKind.Messages,
                "Messages",
                null),
        ];
        SelectedView = selected is null
            ? Views.FirstOrDefault()
            : Views.FirstOrDefault(view => view.Kind == selected.Kind
                && string.Equals(
                    view.ChapterId,
                    selected.ChapterId,
                    StringComparison.Ordinal))
                ?? Views.FirstOrDefault();
        RenderEditor();
    }

    private void RenderEditor()
    {
        if (state is null || SelectedView is null)
        {
            EditorJson = string.Empty;
            return;
        }

        object value = SelectedView.Kind switch
        {
            QuestDevelopmentViewKind.Objectives => state.Objectives,
            QuestDevelopmentViewKind.Messages => state.Messages,
            QuestDevelopmentViewKind.Chapter => state.Chapters
                .First(chapter => string.Equals(
                    chapter.Id,
                    SelectedView.ChapterId,
                    StringComparison.Ordinal))
                .Variables,
            _ => throw new ArgumentOutOfRangeException(),
        };
        EditorJson = JsonSerializer.Serialize(value, EditorJsonOptions);
    }

    public async Task ApplyEditorAsync()
    {
        if (reference is null || SelectedView is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            switch (SelectedView.Kind)
            {
                case QuestDevelopmentViewKind.Objectives:
                    await coordinator.UpdateDevelopmentObjectivesAsync(
                        reference,
                        Deserialize<Dictionary<string, string>>(EditorJson),
                        CancellationToken.None);
                    break;
                case QuestDevelopmentViewKind.Chapter:
                    await coordinator.UpdateDevelopmentChapterVariablesAsync(
                        reference,
                        SelectedView.ChapterId!,
                        Deserialize<Dictionary<string, JsonElement>>(EditorJson),
                        CancellationToken.None);
                    break;
                case QuestDevelopmentViewKind.Messages:
                    await coordinator.UpdateDevelopmentMessagesAsync(
                        reference,
                        Deserialize<List<RavenQuestMessage>>(EditorJson),
                        CancellationToken.None);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            await LoadStateCoreAsync();
            StatusMessage = $"Updated {SelectedView.Label}. A verified state backup was retained.";
            RuntimeChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            StatusMessage = "Development state update failed: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SetSelectedChapterActiveAsync(bool active)
    {
        if (reference is null || SelectedView?.ChapterId is not { } chapterId)
        {
            return;
        }

        await RunActionAsync(
            () => coordinator.SetDevelopmentChapterActiveAsync(
                reference,
                chapterId,
                active,
                CancellationToken.None),
            active ? "Chapter started." : "Chapter stopped.");
    }

    public async Task RunDebugAsync()
    {
        if (reference is null || SelectedView?.ChapterId is not { } chapterId)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var result = await coordinator.RunDevelopmentDebugAsync(
                reference,
                chapterId,
                DebugCode,
                CancellationToken.None);
            DebugResult = JsonSerializer.Serialize(result, EditorJsonOptions);
            await LoadStateCoreAsync();
            StatusMessage = "Debug code completed.";
            RuntimeChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            DebugResult = "Error: " + exception.Message;
            StatusMessage = "Debug code failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task PublishAsync()
    {
        if (reference is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var status = await coordinator.PublishDevelopmentQuestAsync(
                reference,
                PublishConfirmed,
                CancellationToken.None);
            StatusMessage = "Published development quest: " + status;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            StatusMessage = "Development quest publish failed: "
                + exception.Message;
        }
        finally
        {
            PublishConfirmed = false;
            IsBusy = false;
        }
    }

    public async Task ReloadSavedAsync()
    {
        try
        {
            IsBusy = true;
            var result = await coordinator.RefreshAsync(
                CancellationToken.None);
            ApplyRuntimeSnapshots(result.Quests);
            if (reference is not null)
            {
                await LoadStateCoreAsync();
            }

            StatusMessage = result.Warnings.Count == 0
                ? "Reloaded development state from disk."
                : string.Join(Environment.NewLine, result.Warnings);
            RuntimeChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            StatusMessage = "Saved development state could not be reloaded: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RemoveAsync()
    {
        if (reference is null)
        {
            return;
        }

        if (!removePending)
        {
            removePending = true;
            OnPropertyChanged(nameof(RemoveButtonText));
            StatusMessage = "Select Confirm removal to remove local development progress. The state file is backed up first.";
            return;
        }

        var removing = reference;
        removePending = false;
        OnPropertyChanged(nameof(RemoveButtonText));
        try
        {
            IsBusy = true;
            await coordinator.RemoveQuestAsync(
                removing,
                CancellationToken.None);
            ClearQuest();
            StatusMessage = "Development quest removed.";
            RuntimeChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            StatusMessage = "Development quest removal failed: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunActionAsync(Func<Task> action, string success)
    {
        try
        {
            IsBusy = true;
            await action();
            await LoadStateCoreAsync();
            StatusMessage = success;
            RuntimeChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            StatusMessage = "Development quest action failed: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void StartWatcher()
    {
        if (!CanWatchSource)
        {
            watchSource = false;
            OnPropertyChanged(nameof(WatchSource));
            StatusMessage = "Import a valid source folder before enabling watch mode.";
            return;
        }

        StopWatcher(clearToggle: false);
        watcher = new FileSystemWatcher(SourceDirectory)
        {
            Filter = "*",
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size,
        };
        watcher.Changed += SourceChanged;
        watcher.Created += SourceChanged;
        watcher.Deleted += SourceChanged;
        watcher.Renamed += SourceChanged;
        watcher.Error += WatcherError;
        watcher.EnableRaisingEvents = true;
        StatusMessage = "Watching development source folder for changes.";
    }

    private void StopWatcher(bool clearToggle = true)
    {
        lock (watcherSync)
        {
            watcherReload?.Cancel();
            watcherReload?.Dispose();
            watcherReload = null;
        }

        if (watcher is not null)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= SourceChanged;
            watcher.Created -= SourceChanged;
            watcher.Deleted -= SourceChanged;
            watcher.Renamed -= SourceChanged;
            watcher.Error -= WatcherError;
            watcher.Dispose();
            watcher = null;
        }

        if (clearToggle && watchSource)
        {
            watchSource = false;
            OnPropertyChanged(nameof(WatchSource));
        }
    }

    private void SourceChanged(object sender, FileSystemEventArgs eventArgs)
    {
        if (!IsRelevantSource(eventArgs.FullPath))
        {
            return;
        }

        CancellationTokenSource reload;
        lock (watcherSync)
        {
            watcherReload?.Cancel();
            watcherReload?.Dispose();
            watcherReload = new CancellationTokenSource();
            reload = watcherReload;
        }

        _ = ReloadWatchedSourceAsync(reload.Token);
    }

    private async Task ReloadWatchedSourceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            await PostAsync(() => ImportFolderAsync(SourceDirectory));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void WatcherError(object sender, ErrorEventArgs eventArgs)
    {
        Post(() =>
        {
            WatchSource = false;
            StatusMessage = "Source folder watch stopped: "
                + eventArgs.GetException().Message;
        });
    }

    private void ClearQuest()
    {
        WatchSource = false;
        reference = null;
        state = null;
        Title = "No development quest loaded";
        VersionLabel = string.Empty;
        Views = [];
        SelectedView = null;
        EditorJson = string.Empty;
        DebugResult = string.Empty;
        PublishConfirmed = false;
        removePending = false;
        OnPropertyChanged(nameof(RemoveButtonText));
        OnPropertyChanged(nameof(HasDevelopmentQuest));
        RaiseCommandStates();
    }

    private bool CanUseQuest() => !IsBusy && reference is not null;

    private bool CanApplyEditor() => CanUseQuest()
        && SelectedView is not null
        && !string.IsNullOrWhiteSpace(EditorJson);

    private void RaiseCommandStates()
    {
        refreshCommand.RaiseCanExecuteChanged();
        applyCommand.RaiseCanExecuteChanged();
        startChapterCommand.RaiseCanExecuteChanged();
        stopChapterCommand.RaiseCanExecuteChanged();
        runDebugCommand.RaiseCanExecuteChanged();
        publishCommand.RaiseCanExecuteChanged();
        reloadSavedCommand.RaiseCanExecuteChanged();
        removeCommand.RaiseCanExecuteChanged();
    }

    private void Post(Action action)
    {
        if (synchronizationContext is null)
        {
            action();
        }
        else
        {
            synchronizationContext.Post(_ => action(), null);
        }
    }

    private Task PostAsync(Func<Task> action)
    {
        if (synchronizationContext is null)
        {
            return action();
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        synchronizationContext.Post(
            async _ =>
            {
                try
                {
                    await action();
                    completion.SetResult();
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            },
            null);
        return completion.Task;
    }

    private static T Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, EditorJsonOptions)
                ?? throw new InvalidDataException("The editor contains JSON null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The editor does not contain valid JSON for this view.",
                exception);
        }
    }

    private static bool IsRelevantSource(string path)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(path);
        return string.Equals(name, "quest.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "strings.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".lua", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameQuest(
        RavenQuestReference left,
        RavenQuestReference right)
    {
        return string.Equals(
                left.Publisher,
                right.Publisher,
                StringComparison.Ordinal)
            && string.Equals(left.Id, right.Id, StringComparison.Ordinal);
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is not OperationCanceledException;

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

public sealed record QuestDevelopmentViewOption(
    QuestDevelopmentViewKind Kind,
    string Label,
    string? ChapterId);

public enum QuestDevelopmentViewKind
{
    Objectives,
    Chapter,
    Messages,
}
