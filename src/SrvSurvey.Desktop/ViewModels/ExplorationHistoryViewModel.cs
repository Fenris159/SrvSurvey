using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using SrvSurvey.Core.Exploration.History;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Owns one commander's paged history, background indexing, and optional unsold-value presentation.</summary>
public sealed partial class ExplorationHistoryViewModel : INotifyPropertyChanged, IDisposable, IAsyncDisposable
{
    private const string InvalidDateRangeMessage = "The start date must be on or before the end date.";
    private readonly string dataDirectory;
    private readonly CommanderProfileStore profiles;
    private readonly Func<IReadOnlyList<string>> journalDirectories;
    private readonly Func<
        ExplorationHistoryCatalog,
        ExplorationHistoryQuery,
        CancellationToken,
        Task<ExplorationHistoryPage>
    > queryReader;
    private readonly HistoryCommand refreshCommand;
    private readonly HistoryCommand previousCommand;
    private readonly HistoryCommand nextCommand;
    private readonly HistoryCommand clearFiltersCommand;
    private readonly HistoryCommand closeDetailsCommand;
    private readonly HashSet<Task> activeIo = [];
    private CancellationTokenSource contextCancellation = new();
    private ExplorationHistoryCatalog? catalog;
    private string? frontierId;
    private string? commanderName;
    private bool isLive;
    private bool disposed;
    private long generation;
    private long queryGeneration;
    private int pendingOperations;
    private bool autoResetEstimatedValue;
    private bool isBusy;
    private bool isQueryBusy;
    private string searchText = string.Empty;
    private DateTime? fromDate;
    private DateTime? throughDate;
    private ExplorationHistoryStatusOption selectedStatus = StatusOptions[0];
    private int pageOffset;
    private int totalRows;
    private string statusMessage = "Select a commander to catalog exploration history.";
    private IReadOnlyList<ExplorationHistoryRowViewModel> rows = [];
    private ExplorationHistoryRowViewModel? selectedRow;
    private ExplorationHistorySummary summary = new();
    private Func<string, Task>? clipboardWriter;

    public ExplorationHistoryViewModel(
        string dataDirectory,
        CommanderProfileStore profiles,
        Func<IReadOnlyList<string>> journalDirectories,
        Func<
            ExplorationHistoryCatalog,
            ExplorationHistoryQuery,
            CancellationToken,
            Task<ExplorationHistoryPage>
        >? queryReader = null
    )
    {
        this.dataDirectory = dataDirectory;
        this.profiles = profiles;
        this.journalDirectories = journalDirectories;
        this.queryReader = queryReader ?? (static (catalog, query, token) => catalog.QueryAsync(query, token));
        refreshCommand = new HistoryCommand(RefreshAsync, () => HasCommander && !IsBusy);
        previousCommand = new HistoryCommand(() => MovePageAsync(-1), () => pageOffset > 0 && !IsQueryBusy);
        nextCommand = new HistoryCommand(
            () => MovePageAsync(1),
            () => pageOffset + PageSize < totalRows && !IsQueryBusy
        );
        clearFiltersCommand = new HistoryCommand(ClearFiltersAsync, () => HasCommander);
        closeDetailsCommand = new HistoryCommand(
            () =>
            {
                SelectedRow = null;
                return Task.CompletedTask;
            },
            () => HasSelection
        );
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? BalanceChanged;
    public static IReadOnlyList<ExplorationHistoryStatusOption> StatusOptions { get; } =
    [
        new("All data", null),
        new("Unsold", ExplorationDataStatus.Unsold),
        new("Sold", ExplorationDataStatus.Sold),
        new("Lost", ExplorationDataStatus.Lost),
    ];
    public const int PageSize = 50;
    public ICommand RefreshCommand => refreshCommand;
    public ICommand PreviousCommand => previousCommand;
    public ICommand NextCommand => nextCommand;
    public ICommand ClearFiltersCommand => clearFiltersCommand;
    public ICommand CloseDetailsCommand => closeDetailsCommand;
    public Task PendingSynchronization { get; private set; } = Task.CompletedTask;
    public Task PendingQuery { get; private set; } = Task.CompletedTask;
    public Task PendingPreferenceSave { get; private set; } = Task.CompletedTask;
    public Task PendingDetails { get; private set; } = Task.CompletedTask;
    public bool HasCommander => frontierId is not null && !disposed;
    public string CommanderLabel => commanderName ?? "No commander selected";
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (Set(ref isBusy, value))
            {
                RaiseCommands();
            }
        }
    }
    public bool IsQueryBusy
    {
        get => isQueryBusy;
        private set
        {
            if (Set(ref isQueryBusy, value))
            {
                RaiseCommands();
            }
        }
    }
    public string StatusMessage
    {
        get => statusMessage;
        private set => Set(ref statusMessage, value);
    }
    public IReadOnlyList<ExplorationHistoryRowViewModel> Rows
    {
        get => rows;
        private set
        {
            if (Set(ref rows, value))
            {
                Changed(nameof(HasRows));
            }
        }
    }
    public bool HasRows => Rows.Count > 0;
    public string PageDescription =>
        totalRows == 0
            ? "No matching systems"
            : $"{pageOffset + 1:N0}–{Math.Min(pageOffset + PageSize, totalRows):N0} of {totalRows:N0} entries";
    public string UnsoldText => $"{summary.UnsoldValue:N0} CR";
    public string SoldText => $"{summary.SoldValue:N0} CR";
    public string LostText => $"{summary.LostValue:N0} CR";
    public string ActualSalesText => $"Recorded sale earnings: {summary.SaleEarnings:N0} CR";
    public string UnsoldCount => $"{summary.UnsoldSystems:N0} unsold entries";
    public string SoldCount => $"{summary.SoldSystems:N0} sold entries";
    public string LostCount => $"{summary.LostSystems:N0} lost entries";
    public long? AutomaticEstimatedValue =>
        AutoResetEstimatedValue && summary.HasCommanderHistory && summary.IsComplete ? summary.UnsoldValue : null;
    public string ValueModeDescription
    {
        get
        {
            if (!AutoResetEstimatedValue)
            {
                return "Survey estimate accumulated since the last manual reset.";
            }
            if (AutomaticEstimatedValue is not null)
            {
                return "Estimated value of unsold cartographic data in this commander's available journals.";
            }
            return IsBusy
                ? "Preparing the journal-derived unsold balance. The saved estimate is shown until history is available."
                : "A balance could not be derived from available journals. The saved estimate is shown.";
        }
    }

    public bool AutoResetEstimatedValue
    {
        get => autoResetEstimatedValue;
        set
        {
            if (Set(ref autoResetEstimatedValue, value))
            {
                PendingPreferenceSave = SavePreferenceAsync(value);
                NotifyBalance();
            }
        }
    }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (Set(ref searchText, value ?? string.Empty))
            {
                pageOffset = 0;
                PendingQuery = QueryAsync();
            }
        }
    }
    public DateTime? FromDate
    {
        get => fromDate;
        set
        {
            if (Set(ref fromDate, value))
            {
                pageOffset = 0;
                PendingQuery = QueryAsync();
            }
        }
    }
    public DateTime? ThroughDate
    {
        get => throughDate;
        set
        {
            if (Set(ref throughDate, value))
            {
                pageOffset = 0;
                PendingQuery = QueryAsync();
            }
        }
    }

    public ExplorationHistoryStatusOption SelectedStatus
    {
        get => selectedStatus;
        set
        {
            if (value is not null && Set(ref selectedStatus, value))
            {
                pageOffset = 0;
                PendingQuery = QueryAsync();
            }
        }
    }
    public ExplorationHistoryRowViewModel? SelectedRow
    {
        get => selectedRow;
        set
        {
            if (Set(ref selectedRow, value))
            {
                PendingDetails = LoadDetailsAsync(value);
                closeDetailsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public void SetCommander(string? id, string? name, bool live, bool autoReset)
    {
        if (disposed)
        {
            return;
        }
        if (string.Equals(frontierId, id, StringComparison.OrdinalIgnoreCase) && isLive == live && catalog is not null)
        {
            commanderName = name;
            Changed(nameof(CommanderLabel));
            return;
        }
        contextCancellation.Cancel();
        contextCancellation.Dispose();
        contextCancellation = new();
        generation++;
        queryGeneration++;
        frontierId = id;
        commanderName = name;
        isLive = live;
        autoResetEstimatedValue = autoReset;
        pageOffset = 0;
        totalRows = 0;
        searchText = string.Empty;
        fromDate = null;
        throughDate = null;
        Changed(nameof(FromDate));
        Changed(nameof(ThroughDate));
        selectedStatus = StatusOptions[0];
        Changed(nameof(SearchText));
        Changed(nameof(SelectedStatus));
        Changed(nameof(PageDescription));
        pendingOperations = 0;
        IsQueryBusy = false;
        IsBusy = false;
        Rows = [];
        SelectedRow = null;
        summary = new();
        catalog = id is null ? null : new ExplorationHistoryCatalog(dataDirectory, id, live);
        Changed(nameof(AutoResetEstimatedValue));
        Changed(nameof(HasCommander));
        Changed(nameof(CommanderLabel));
        NotifyBalance();
        RaiseCommands();
        PendingSynchronization = catalog is null ? Task.CompletedTask : QueueSynchronizationAsync([], full: true);
    }

    public void Observe(IReadOnlyList<JournalEventEnvelope> entries)
    {
        if (entries.Count > 0 && HasCommander && catalog is not null)
        {
            PendingSynchronization = QueueSynchronizationAsync(entries.ToArray(), full: false);
        }
    }

    public Task RefreshAsync()
    {
        if (catalog is null || disposed)
        {
            return Task.CompletedTask;
        }
        PendingSynchronization = QueueSynchronizationAsync([], full: true);
        return PendingSynchronization;
    }

    private async Task QueueSynchronizationAsync(IReadOnlyList<JournalEventEnvelope> entries, bool full)
    {
        Task previous = PendingSynchronization;
        ExplorationHistoryCatalog? current = catalog;
        long version = generation;
        CancellationToken token = contextCancellation.Token;
        pendingOperations++;
        IsBusy = true;
        try
        {
            await previous;
            if (current is null || !Current(version, token))
            {
                return;
            }
            ExplorationHistorySummary candidate;
            if (full)
            {
                await QueryAsync();
                var progress = new Progress<ExplorationHistoryProgress>(value =>
                {
                    if (Current(version, token) && IsBusy)
                    {
                        StatusMessage =
                            $"Cataloging journal {value.ProcessedFiles:N0} of {value.TotalFiles:N0}: {value.CurrentFile}";
                    }
                });
                candidate = await TrackIo(current.SynchronizeAsync(journalDirectories(), progress, token));
            }
            else
            {
                candidate = await TrackIo(current.ObserveAsync(entries, journalDirectories(), token));
            }
            if (!Current(version, token))
            {
                return;
            }
            summary = candidate;
            NotifyBalance();
            StatusMessage = CatalogStatusMessage();
            await QueryAsync();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Switching commanders or closing the workspace cancels retained work normally.
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            if (Current(version, token))
            {
                StatusMessage = "Exploration history could not be updated: " + exception.Message;
            }
        }
        finally
        {
            if (Current(version, token))
            {
                pendingOperations--;
                IsBusy = pendingOperations > 0;
                NotifyBalance();
            }
        }
    }

    private async Task QueryAsync()
    {
        ExplorationHistoryCatalog? current = catalog;
        if (current is null || disposed)
        {
            return;
        }
        long request = ++queryGeneration;
        if (FromDate is { } from && ThroughDate is { } through && from.Date > through.Date)
        {
            IsQueryBusy = false;
            StatusMessage = InvalidDateRangeMessage;
            return;
        }
        long version = generation;
        CancellationToken token = contextCancellation.Token;
        IsQueryBusy = true;
        try
        {
            while (true)
            {
                ExplorationHistoryAnchor? selection = SelectedRow?.Data.Anchor;
                ExplorationHistoryPage page = await TrackIo(queryReader(current, CreateQuery(selection), token));
                if (!IsCurrentQuery(version, request, token))
                {
                    return;
                }
                if (selection != SelectedRow?.Data.Anchor)
                {
                    continue;
                }
                ApplyPage(page, selection);
                return;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Switching commanders or closing the workspace cancels retained work normally.
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            if (IsCurrentQuery(version, request, token))
            {
                StatusMessage = "Exploration history could not be read: " + exception.Message;
            }
        }
        finally
        {
            if (IsCurrentQuery(version, request, token))
            {
                IsQueryBusy = false;
            }
        }
    }

    private bool IsCurrentQuery(long version, long request, CancellationToken token) =>
        Current(version, token) && request == queryGeneration;

    private string CatalogStatusMessage() =>
        summary.Notice ?? $"History cataloged from {summary.SourceFiles:N0} journal files for {CommanderLabel}.";

    private ExplorationHistoryQuery CreateQuery(ExplorationHistoryAnchor? selection) =>
        new(
            SelectedStatus.Status,
            SearchText,
            pageOffset,
            PageSize,
            FromDate is { } start ? new DateTimeOffset(DateTime.SpecifyKind(start.Date, DateTimeKind.Utc)) : null,
            ThroughDate is { } end
                ? new DateTimeOffset(
                    DateTime.SpecifyKind(end.Date.AddTicks(TimeSpan.TicksPerDay - 1), DateTimeKind.Utc)
                )
                : null,
            selection
        );

    private void ApplyPage(ExplorationHistoryPage page, ExplorationHistoryAnchor? requestedSelection)
    {
        bool restoreSelection = requestedSelection is not null && requestedSelection == SelectedRow?.Data.Anchor;
        summary = page.Summary;
        totalRows = page.TotalRows;
        pageOffset = page.Offset;
        Rows = page
            .Rows.Select(row => new ExplorationHistoryRowViewModel(
                row,
                new HistoryCommand(() => CopyNameAsync(row.SystemName), () => clipboardWriter is not null && !disposed)
            ))
            .ToArray();
        SelectedRow = restoreSelection ? Rows.SingleOrDefault(row => row.Data.Id == page.SelectedRowId) : null;
        if (StatusMessage == InvalidDateRangeMessage)
        {
            StatusMessage = CatalogStatusMessage();
        }
        Changed(nameof(PageDescription));
        NotifyBalance();
        RaiseCommands();
    }

    private Task MovePageAsync(int direction)
    {
        pageOffset = Math.Max(0, pageOffset + direction * PageSize);
        PendingQuery = QueryAsync();
        return PendingQuery;
    }

    private Task ClearFiltersAsync()
    {
        searchText = string.Empty;
        fromDate = null;
        throughDate = null;
        Changed(nameof(FromDate));
        Changed(nameof(ThroughDate));
        selectedStatus = StatusOptions[0];
        pageOffset = 0;
        Changed(nameof(SearchText));
        Changed(nameof(SelectedStatus));
        PendingQuery = QueryAsync();
        return PendingQuery;
    }

    private async Task SavePreferenceAsync(bool enabled)
    {
        string? id = frontierId;
        if (id is null || disposed)
        {
            return;
        }
        long version = generation;
        CancellationToken token = contextCancellation.Token;
        try
        {
            await TrackIo(
                profiles.SaveExplorationTrackingAsync(id, commanderName, isLive, enabled, CancellationToken.None)
            );
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            if (Current(version, token))
            {
                StatusMessage =
                    "The tracking choice applies to this session but could not be saved: " + exception.Message;
            }
        }
    }

    public void SetClipboardWriter(Func<string, Task>? writer)
    {
        clipboardWriter = writer;
        foreach (ExplorationHistoryRowViewModel row in Rows)
        {
            if (row.CopySystemNameCommand is HistoryCommand command)
            {
                command.RaiseCanExecuteChanged();
            }
        }
    }

    private async Task CopyNameAsync(string name)
    {
        if (clipboardWriter is null)
        {
            return;
        }
        try
        {
            await clipboardWriter(name);
            StatusMessage = "Copied system name: " + name;
        }
        catch (Exception exception)
            when (IsStorageFailure(exception) || exception is InvalidOperationException or NotSupportedException)
        {
            StatusMessage = "The system name could not be copied: " + exception.Message;
        }
    }

    private bool Current(long version, CancellationToken token) =>
        !disposed && generation == version && !token.IsCancellationRequested;

    private static bool IsStorageFailure(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or SqliteException;

    private void NotifyBalance()
    {
        foreach (
            string property in new[]
            {
                nameof(UnsoldText),
                nameof(SoldText),
                nameof(LostText),
                nameof(ActualSalesText),
                nameof(UnsoldCount),
                nameof(SoldCount),
                nameof(LostCount),
                nameof(AutomaticEstimatedValue),
                nameof(ValueModeDescription),
            }
        )
        {
            Changed(property);
        }
        BalanceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseCommands()
    {
        refreshCommand.RaiseCanExecuteChanged();
        previousCommand.RaiseCanExecuteChanged();
        nextCommand.RaiseCanExecuteChanged();
        clearFiltersCommand.RaiseCanExecuteChanged();
        closeDetailsCommand.RaiseCanExecuteChanged();
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        Changed(name);
        return true;
    }

    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        generation++;
        contextCancellation.Cancel();
        contextCancellation.Dispose();
        clipboardWriter = null;
        BalanceChanged = null;
        RaiseCommands();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        Task[] retained;
        lock (activeIo)
        {
            retained = activeIo.ToArray();
        }
        try
        {
            await Task.WhenAll(retained).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the normal way an indexing worker releases its journal and database handles.
        }
    }

    private TTask TrackIo<TTask>(TTask operation)
        where TTask : Task
    {
        lock (activeIo)
        {
            activeIo.Add(operation);
        }
        _ = operation.ContinueWith(
            completed =>
            {
                lock (activeIo)
                {
                    activeIo.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
        return operation;
    }

    private sealed class HistoryCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
    {
        private bool running;
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => !running && canExecute();

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
            {
                return;
            }
            running = true;
            RaiseCanExecuteChanged();
            try
            {
                await execute();
            }
            finally
            {
                running = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed record ExplorationHistoryStatusOption(string Label, ExplorationDataStatus? Status);

public sealed class ExplorationHistoryRowViewModel(ExplorationHistoryRow data, ICommand copySystemNameCommand)
{
    public ExplorationHistoryRow Data { get; } = data;
    public ICommand CopySystemNameCommand { get; } = copySystemNameCommand;
    public string SystemName => Data.SystemName;
    public string CollectedDate =>
        Data.LastCollected?.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Unknown date";
    public string CollectedTime =>
        Data.LastCollected?.UtcDateTime.ToString("HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? string.Empty;
    public string Estimate => $"{Data.EstimatedValue:N0} CR";
    public string Contents => $"{Data.ScannedBodies:N0} scanned, {Data.MappedBodies:N0} mapped";
    public IReadOnlyList<ExplorationNotableTagViewModel> NotableTags { get; } =
        ExplorationNotableTagViewModel.From(data.Notables);
    public bool HasNotables => NotableTags.Count > 0;
    public string Position =>
        Data.X is { } x && Data.Y is { } y && Data.Z is { } z
            ? $"{x:N1} / {y:N1} / {z:N1} ly"
            : "Coordinates unavailable";
    public bool IsSold => Data.Status == ExplorationDataStatus.Sold;
    public bool IsUnsold => Data.Status == ExplorationDataStatus.Unsold;
    public bool IsLost => Data.Status == ExplorationDataStatus.Lost;
    public string Status => Data.Status.ToString();
    public string SettlementDate =>
        Data.SettledAt?.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? string.Empty;
}
