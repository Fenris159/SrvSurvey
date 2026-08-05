using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Quests;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class QuestWorkspaceViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly QuestRuntimeCoordinator coordinator;
    private readonly QuestSettingsStore settingsStore;
    private readonly AsyncCommand refreshCommand;
    private readonly AsyncCommand toggleEnabledCommand;
    private readonly AsyncCommand pauseQuestCommand;
    private readonly AsyncCommand removeQuestCommand;
    private readonly AsyncCommand activateQuestCommand;
    private readonly AsyncCommand resumeQuestCommand;
    private readonly AsyncParameterCommand<QuestMessageRowViewModel>
        openMessageCommand;
    private bool isEnabled;
    private bool isBusy;
    private string statusMessage;
    private IReadOnlyList<QuestCardViewModel> activeQuests = [];
    private IReadOnlyList<QuestMessageRowViewModel> messages = [];
    private IReadOnlyList<QuestCatalogRowViewModel> catalog = [];
    private IReadOnlyList<QuestHistoryRowViewModel> history = [];
    private QuestCardViewModel? selectedQuest;
    private QuestMessageRowViewModel? selectedMessage;
    private QuestCatalogRowViewModel? selectedCatalogQuest;
    private QuestHistoryRowViewModel? selectedHistoryQuest;
    private IReadOnlyList<QuestMessageActionViewModel> messageActions = [];
    private IReadOnlyList<QuestRuntimeSnapshot>? appliedRuntimeSnapshots;
    private RavenQuestReference? pendingRemoval;

    public QuestWorkspaceViewModel(
        QuestRuntimeCoordinator coordinator,
        QuestSettingsStore settingsStore)
    {
        this.coordinator = coordinator
            ?? throw new ArgumentNullException(nameof(coordinator));
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        Developer = new QuestDeveloperViewModel(coordinator);
        Developer.RuntimeChanged += Developer_RuntimeChanged;
        isEnabled = settingsStore.LoadEnabled();
        statusMessage = isEnabled
            ? "Waiting for the commander journal session."
            : "Quests are disabled.";
        refreshCommand = new AsyncCommand(
            RefreshAsync,
            () => !IsBusy);
        toggleEnabledCommand = new AsyncCommand(
            ToggleEnabledAsync,
            () => !IsBusy);
        pauseQuestCommand = new AsyncCommand(
            PauseSelectedQuestAsync,
            () => !IsBusy && SelectedQuest is { IsDevelopment: false });
        removeQuestCommand = new AsyncCommand(
            RemoveSelectedQuestAsync,
            () => !IsBusy && SelectedQuest is not null);
        activateQuestCommand = new AsyncCommand(
            ActivateSelectedQuestAsync,
            () => !IsBusy && SelectedCatalogQuest is not null);
        resumeQuestCommand = new AsyncCommand(
            ResumeSelectedQuestAsync,
            () => !IsBusy
                && SelectedHistoryQuest?.State == RavenQuestState.paused);
        openMessageCommand = new AsyncParameterCommand<QuestMessageRowViewModel>(
            OpenMessageAsync,
            message => !IsBusy && message is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public QuestDeveloperViewModel Developer { get; }

    public bool IsEnabled
    {
        get => isEnabled;
        private set
        {
            if (SetField(ref isEnabled, value))
            {
                OnPropertyChanged(nameof(ToggleEnabledButtonText));
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
                OnPropertyChanged(nameof(RefreshButtonText));
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public string ToggleEnabledButtonText => IsEnabled
        ? "Disable quests"
        : "Enable quests";

    public string RefreshButtonText => IsBusy ? "Working..." : "Refresh";

    public IReadOnlyList<QuestCardViewModel> ActiveQuests
    {
        get => activeQuests;
        private set
        {
            if (SetField(ref activeQuests, value))
            {
                OnPropertyChanged(nameof(ActiveQuestSummary));
            }
        }
    }

    public string ActiveQuestSummary => ActiveQuests.Count == 0
        ? "No active quests"
        : $"{ActiveQuests.Count:N0} active • "
            + $"{Messages.Count(message => !message.IsRead):N0} unread";

    public IReadOnlyList<QuestMessageRowViewModel> Messages
    {
        get => messages;
        private set
        {
            if (SetField(ref messages, value))
            {
                OnPropertyChanged(nameof(ActiveQuestSummary));
                OnPropertyChanged(nameof(MessageSummary));
            }
        }
    }

    public string MessageSummary => Messages.Count == 0
        ? "No messages"
        : $"{Messages.Count:N0} messages • "
            + $"{Messages.Count(message => !message.IsRead):N0} unread";

    public IReadOnlyList<QuestCatalogRowViewModel> Catalog
    {
        get => catalog;
        private set => SetField(ref catalog, value);
    }

    public IReadOnlyList<QuestHistoryRowViewModel> History
    {
        get => history;
        private set => SetField(ref history, value);
    }

    public QuestCardViewModel? SelectedQuest
    {
        get => selectedQuest;
        set
        {
            if (SetField(ref selectedQuest, value))
            {
                pendingRemoval = null;
                OnPropertyChanged(nameof(RemoveQuestButtonText));
                RaiseCommandStates();
            }
        }
    }

    public QuestMessageRowViewModel? SelectedMessage
    {
        get => selectedMessage;
        set
        {
            if (SetField(ref selectedMessage, value))
            {
                MessageActions = value?.Actions.Select(action =>
                    new QuestMessageActionViewModel(
                        action.Key,
                        action.Value,
                        new AsyncCommand(
                            () => ReplyToMessageAsync(action.Key),
                            () => !IsBusy
                                && SelectedMessage is { Replied: null })))
                    .ToArray()
                    ?? [];
            }
        }
    }

    public IReadOnlyList<QuestMessageActionViewModel> MessageActions
    {
        get => messageActions;
        private set => SetField(ref messageActions, value);
    }

    public QuestCatalogRowViewModel? SelectedCatalogQuest
    {
        get => selectedCatalogQuest;
        set
        {
            if (SetField(ref selectedCatalogQuest, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public QuestHistoryRowViewModel? SelectedHistoryQuest
    {
        get => selectedHistoryQuest;
        set
        {
            if (SetField(ref selectedHistoryQuest, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string RemoveQuestButtonText => pendingRemoval is not null
        && SelectedQuest?.Reference == pendingRemoval
            ? "Confirm removal"
            : "Remove quest";

    public ICommand RefreshCommand => refreshCommand;

    public ICommand ToggleEnabledCommand => toggleEnabledCommand;

    public ICommand PauseQuestCommand => pauseQuestCommand;

    public ICommand RemoveQuestCommand => removeQuestCommand;

    public ICommand ActivateQuestCommand => activateQuestCommand;

    public ICommand ResumeQuestCommand => resumeQuestCommand;

    public ICommand OpenMessageCommand => openMessageCommand;

    public void ApplyRuntimeResult(
        QuestRuntimeUpdateResult result,
        bool enabled)
    {
        ArgumentNullException.ThrowIfNull(result);
        IsEnabled = enabled;
        if (!ReferenceEquals(appliedRuntimeSnapshots, result.Quests))
        {
            RebuildRuntimeRows(result.Quests);
        }
        StatusMessage = result.Warnings.Count > 0
            ? string.Join(Environment.NewLine, result.Warnings)
            : !enabled
                ? "Quests are disabled."
                : ActiveQuests.Count == 0
                    ? "No active quests."
                    : ActiveQuestSummary;
    }

    public async Task RefreshAsync()
    {
        var warnings = new List<string>();
        try
        {
            IsBusy = true;
            StatusMessage = "Refreshing quest communications...";
            var runtime = await coordinator.RefreshAsync();
            warnings.AddRange(runtime.Warnings);
            RebuildRuntimeRows(runtime.Quests);

            try
            {
                var definitions = await coordinator.GetPublishedQuestsAsync();
                Catalog = definitions
                    .Where(definition => ActiveQuests.All(active =>
                        !SameQuest(active.Reference, definition.Reference)))
                    .Select(definition => new QuestCatalogRowViewModel(definition))
                    .OrderBy(quest => quest.Title, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                warnings.Add("Quest catalog: " + exception.Message);
            }

            try
            {
                var statuses = await coordinator
                    .GetCommanderQuestStatusesAsync();
                History = statuses
                    .Where(status => status.State != RavenQuestState.active)
                    .Select(status => new QuestHistoryRowViewModel(status))
                    .OrderByDescending(status => status.StateChangedOn)
                    .ToArray();
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                warnings.Add("Commander quest history: " + exception.Message);
            }

            StatusMessage = warnings.Count > 0
                ? string.Join(Environment.NewLine, warnings)
                : $"Quest communications refreshed. {ActiveQuestSummary}.";
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            StatusMessage = "Quest refresh failed: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        Developer.RuntimeChanged -= Developer_RuntimeChanged;
        Developer.Dispose();
    }

    private async Task ToggleEnabledAsync()
    {
        var enabled = !IsEnabled;
        try
        {
            IsBusy = true;
            settingsStore.SaveEnabled(enabled);
            IsEnabled = enabled;
            var result = await coordinator.SetEnabledAsync(enabled);
            ApplyRuntimeResult(result, enabled);
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = enabled
                ? "Quests are enabled and will initialize with the next commander "
                    + "journal session. " + exception.Message
                : "Quests are disabled for the next commander journal session. "
                    + exception.Message;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            StatusMessage = "Quest preference could not be changed: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenMessageAsync(QuestMessageRowViewModel? message)
    {
        if (message is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            SelectedMessage = message;
            if (!message.IsRead)
            {
                await coordinator.MarkMessageReadAsync(
                    message.Quest,
                    message.Id);
                RebuildRuntimeRows(coordinator.Snapshot);
                SelectedMessage = Messages.FirstOrDefault(candidate =>
                    SameQuest(candidate.Quest, message.Quest)
                    && string.Equals(
                        candidate.Id,
                        message.Id,
                        StringComparison.Ordinal));
            }

            StatusMessage = "Message opened.";
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            StatusMessage = "The message could not be opened: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReplyToMessageAsync(string action)
    {
        if (SelectedMessage is not { } message)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await coordinator.ReplyToMessageAsync(
                message.Quest,
                message.Id,
                action);
            RebuildRuntimeRows(coordinator.Snapshot);
            SelectedMessage = Messages.FirstOrDefault(candidate =>
                SameQuest(candidate.Quest, message.Quest)
                && string.Equals(
                    candidate.Id,
                    message.Id,
                    StringComparison.Ordinal));
            StatusMessage = "Response sent to the quest script.";
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            StatusMessage = "The response could not be applied: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PauseSelectedQuestAsync()
    {
        if (SelectedQuest is not { } quest)
        {
            return;
        }

        await RunQuestActionAsync(
            () => coordinator.PauseQuestAsync(quest.Reference),
            "Quest paused.");
    }

    private async Task RemoveSelectedQuestAsync()
    {
        if (SelectedQuest is not { } quest)
        {
            return;
        }

        if (pendingRemoval != quest.Reference)
        {
            pendingRemoval = quest.Reference;
            OnPropertyChanged(nameof(RemoveQuestButtonText));
            StatusMessage = "Choose Confirm removal to permanently remove this "
                + "quest progress. A local development quest is backed up first.";
            return;
        }

        pendingRemoval = null;
        OnPropertyChanged(nameof(RemoveQuestButtonText));
        await RunQuestActionAsync(
            () => coordinator.RemoveQuestAsync(quest.Reference),
            "Quest removed.");
    }

    private async Task ActivateSelectedQuestAsync()
    {
        if (SelectedCatalogQuest is not { } quest)
        {
            return;
        }

        await RunQuestActionAsync(
            () => coordinator.ActivateQuestAsync(
                quest.Reference.Publisher,
                quest.Reference.Id),
            "Quest activated.",
            refreshRemoteLists: true);
    }

    private async Task ResumeSelectedQuestAsync()
    {
        if (SelectedHistoryQuest is not { } quest)
        {
            return;
        }

        await RunQuestActionAsync(
            () => coordinator.ResumeQuestAsync(quest.Reference),
            "Quest resumed.",
            refreshRemoteLists: true);
    }

    private async Task RunQuestActionAsync(
        Func<Task> action,
        string success,
        bool refreshRemoteLists = false)
    {
        try
        {
            IsBusy = true;
            await action();
            RebuildRuntimeRows(coordinator.Snapshot);
            SelectedQuest = null;
            StatusMessage = success;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            StatusMessage = "Quest action failed: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }

        if (refreshRemoteLists)
        {
            await RefreshAsync();
        }
    }

    private void RebuildRuntimeRows(
        IReadOnlyList<QuestRuntimeSnapshot> snapshots)
    {
        appliedRuntimeSnapshots = snapshots;
        var selectedReference = SelectedQuest?.Reference;
        (RavenQuestReference Quest, string Id)? selectedMessageIdentity =
            SelectedMessage is null
            ? null
            : (SelectedMessage.Quest, SelectedMessage.Id);
        ActiveQuests = snapshots.Select(snapshot =>
                new QuestCardViewModel(snapshot))
            .ToArray();
        Developer.ApplyRuntimeSnapshots(snapshots);
        Messages = snapshots.SelectMany(snapshot => snapshot.Messages)
            .OrderByDescending(message => message.Received)
            .Select(message => new QuestMessageRowViewModel(message))
            .ToArray();
        var firstActiveQuest = ActiveQuests.Count > 0
            ? ActiveQuests[0]
            : null;
        SelectedQuest = selectedReference is null
            ? firstActiveQuest
            : ActiveQuests.FirstOrDefault(quest =>
                SameQuest(quest.Reference, selectedReference));
        if (selectedMessageIdentity is { } identity)
        {
            SelectedMessage = Messages.FirstOrDefault(message =>
                SameQuest(message.Quest, identity.Quest)
                && string.Equals(
                    message.Id,
                    identity.Id,
                    StringComparison.Ordinal));
        }

        RaiseCommandStates();
    }

    private void Developer_RuntimeChanged(object? sender, EventArgs eventArgs)
    {
        RebuildRuntimeRows(coordinator.Snapshot);
    }

    private void RaiseCommandStates()
    {
        refreshCommand.RaiseCanExecuteChanged();
        toggleEnabledCommand.RaiseCanExecuteChanged();
        pauseQuestCommand.RaiseCanExecuteChanged();
        removeQuestCommand.RaiseCanExecuteChanged();
        activateQuestCommand.RaiseCanExecuteChanged();
        resumeQuestCommand.RaiseCanExecuteChanged();
        openMessageCommand.RaiseCanExecuteChanged();
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

    private static bool IsRecoverable(Exception exception)
    {
        return exception is not OperationCanceledException;
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

    private sealed class AsyncParameterCommand<T>(
        Func<T?, Task> execute,
        Func<T?, bool> canExecute) : ICommand
        where T : class
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) =>
            canExecute(parameter as T);

        public async void Execute(object? parameter)
        {
            var value = parameter as T;
            if (canExecute(value))
            {
                await execute(value);
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed record QuestCardViewModel
{
    public QuestCardViewModel(QuestRuntimeSnapshot snapshot)
    {
        Reference = snapshot.Reference;
        Title = snapshot.Title;
        Subtitle = snapshot.Subtitle;
        IsDevelopment = snapshot.IsDevelopment;
        StateLabel = snapshot.IsDevelopment ? "DEVELOPMENT" : "ACTIVE";
        UnreadMessageCount = snapshot.UnreadMessageCount;
        Tags = snapshot.Tags.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        Objectives = snapshot.Objectives.Select(pair =>
                QuestObjectiveRowViewModel.Create(
                    pair.Key,
                    snapshot.ObjectiveLabels.GetValueOrDefault(pair.Key)
                        ?? pair.Key,
                    pair.Value))
            .OrderBy(objective => objective.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Locations = snapshot.BodyLocations.Select(pair =>
                new QuestLocationRowViewModel(pair.Key, pair.Value))
            .OrderBy(location => location.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public RavenQuestReference Reference { get; }

    public string Title { get; }

    public string? Subtitle { get; }

    public bool IsDevelopment { get; }

    public string StateLabel { get; }

    public int UnreadMessageCount { get; }

    public IReadOnlyList<string> Tags { get; }

    public IReadOnlyList<QuestObjectiveRowViewModel> Objectives { get; }

    public IReadOnlyList<QuestLocationRowViewModel> Locations { get; }

    public string Identity => Reference.ToString();

    public string TagsLabel => Tags.Count == 0
        ? "No tags"
        : string.Join(" • ", Tags);
}

public sealed record QuestObjectiveRowViewModel(
    string Id,
    string Label,
    string State,
    string Progress)
{
    public static QuestObjectiveRowViewModel Create(
        string id,
        string label,
        string value)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        var state = parts.FirstOrDefault() ?? "unknown";
        var progress = parts.Length >= 3
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var current)
            && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var total)
                ? $"{current:N0} / {total:N0}"
                : string.Empty;
        return new QuestObjectiveRowViewModel(
            id,
            label,
            CultureInfo.InvariantCulture.TextInfo.ToTitleCase(state),
            progress);
    }
}

public sealed record QuestLocationRowViewModel(string Label, string Coordinates);

public sealed record QuestMessageRowViewModel
{
    public QuestMessageRowViewModel(QuestRuntimeMessageSnapshot message)
    {
        Quest = message.Quest;
        Id = message.Id;
        Received = message.Received;
        From = message.From;
        Subject = message.Subject;
        Body = message.Body;
        Actions = message.Actions;
        Tags = message.Tags.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        IsRead = message.Read;
        Replied = message.Replied;
    }

    public RavenQuestReference Quest { get; }

    public string Id { get; }

    public DateTimeOffset Received { get; }

    public string From { get; }

    public string? Subject { get; }

    public string Body { get; }

    public IReadOnlyDictionary<string, string> Actions { get; }

    public IReadOnlyList<string> Tags { get; }

    public bool IsRead { get; }

    public string? Replied { get; }

    public string ReadLabel => IsRead ? "READ" : "UNREAD";

    public string ReceivedLabel => Received == default
        ? string.Empty
        : Received.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);

    public string TagsLabel => Tags.Count == 0
        ? string.Empty
        : string.Join(" • ", Tags);
}

public sealed record QuestMessageActionViewModel(
    string Id,
    string Label,
    ICommand Command);

public sealed record QuestCatalogRowViewModel
{
    public QuestCatalogRowViewModel(RavenQuestDefinition definition)
    {
        Reference = definition.Reference;
        Title = definition.Title;
        Subtitle = definition.Subtitle;
        Description = definition.Description;
        Duration = definition.Duration.ToString();
        Tags = definition.Tags.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public RavenQuestReference Reference { get; }

    public string Title { get; }

    public string? Subtitle { get; }

    public string? Description { get; }

    public string Duration { get; }

    public IReadOnlyList<string> Tags { get; }

    public string Identity => Reference.ToString();

    public string TagsLabel => Tags.Count == 0
        ? "No tags"
        : string.Join(" • ", Tags);

    public string SubtitleOrDescription => Subtitle ?? Description ?? string.Empty;
}

public sealed record QuestHistoryRowViewModel
{
    public QuestHistoryRowViewModel(RavenCommanderQuestStatus status)
    {
        Reference = new RavenQuestReference(
            status.Publisher,
            status.Id,
            status.Version);
        State = status.State;
        StateChangedOn = status.StateChangedOn;
    }

    public RavenQuestReference Reference { get; }

    public RavenQuestState State { get; }

    public DateTimeOffset StateChangedOn { get; }

    public string StateLabel => State.ToString().ToUpperInvariant();

    public string Identity => Reference.ToString();
}
