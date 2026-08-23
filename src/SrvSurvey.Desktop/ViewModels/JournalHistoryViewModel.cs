using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using SrvSurvey.Core.Diagnostics.Replay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class JournalHistoryViewModel : INotifyPropertyChanged, IDisposable
{
    private const int BackgroundFilterThreshold = 5_000;
    private readonly string journalDirectory;
    private readonly string sourceVersion;
    private readonly JournalHistoryReader reader;
    private readonly JournalReplayExporter exporter;
    private readonly Func<ReplayPresentationSnapshot?> presentationSnapshotProvider;
    private readonly SynchronizationContext? synchronizationContext;
    private readonly AsyncCommand refreshCommand;
    private IReadOnlyList<JournalHistoryEvent> allEvents = [];
    private IReadOnlyList<JournalHistoryEvent> events = [];
    private int totalEventCount;
    private bool isHistoryWindowed;
    private JournalHistoryEvent? selectedEvent;
    private string searchText = string.Empty;
    private string summary = "Journal history has not been loaded.";
    private string statusMessage = string.Empty;
    private bool isBusy;
    private DateTimeOffset? rangeFrom;
    private DateTimeOffset? rangeTo;
    private string rangeFromText = string.Empty;
    private string rangeToText = string.Empty;
    private bool redactExport = true;
    private CancellationTokenSource? filterCancellation;

    public JournalHistoryViewModel(
        string journalDirectory,
        string sourceVersion,
        JournalHistoryReader? reader = null,
        JournalReplayExporter? exporter = null,
        Func<ReplayPresentationSnapshot?>? presentationSnapshotProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceVersion);
        this.journalDirectory = Path.GetFullPath(journalDirectory);
        this.sourceVersion = sourceVersion.Trim();
        this.reader = reader ?? new JournalHistoryReader();
        this.exporter = exporter ?? new JournalReplayExporter();
        this.presentationSnapshotProvider = presentationSnapshotProvider
            ?? (() => null);
        synchronizationContext = SynchronizationContext.Current;
        refreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy);
        RefreshCommand = refreshCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand RefreshCommand { get; }

    public string JournalDirectory => journalDirectory;

    public IReadOnlyList<JournalHistoryEvent> Events
    {
        get => events;
        private set => SetField(ref events, value);
    }

    public JournalHistoryEvent? SelectedEvent
    {
        get => selectedEvent;
        set => SetField(ref selectedEvent, value);
    }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetField(ref searchText, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    public DateTimeOffset? RangeFrom
    {
        get => rangeFrom;
        set
        {
            var changed = SetField(ref rangeFrom, value);
            var formatted = FormatEditableTimestamp(value);
            if (!string.Equals(rangeFromText, formatted, StringComparison.Ordinal))
            {
                rangeFromText = formatted;
                OnPropertyChanged(nameof(RangeFromText));
                changed = true;
            }

            if (changed)
            {
                OnPropertyChanged(nameof(ExportPreview));
            }
        }
    }

    public DateTimeOffset? RangeTo
    {
        get => rangeTo;
        set
        {
            var changed = SetField(ref rangeTo, value);
            var formatted = FormatEditableTimestamp(value);
            if (!string.Equals(rangeToText, formatted, StringComparison.Ordinal))
            {
                rangeToText = formatted;
                OnPropertyChanged(nameof(RangeToText));
                changed = true;
            }

            if (changed)
            {
                OnPropertyChanged(nameof(ExportPreview));
            }
        }
    }

    public string RangeFromText
    {
        get => rangeFromText;
        set
        {
            if (!SetField(ref rangeFromText, value ?? string.Empty))
            {
                return;
            }

            if (TryParseTimestamp(rangeFromText, out var parsed))
            {
                rangeFrom = parsed;
                OnPropertyChanged(nameof(RangeFrom));
            }

            OnPropertyChanged(nameof(ExportPreview));
        }
    }

    public string RangeToText
    {
        get => rangeToText;
        set
        {
            if (!SetField(ref rangeToText, value ?? string.Empty))
            {
                return;
            }

            if (TryParseTimestamp(rangeToText, out var parsed))
            {
                rangeTo = parsed;
                OnPropertyChanged(nameof(RangeTo));
            }

            OnPropertyChanged(nameof(ExportPreview));
        }
    }

    public bool RedactExport
    {
        get => redactExport;
        set
        {
            if (SetField(ref redactExport, value))
            {
                OnPropertyChanged(nameof(ExportPreview));
            }
        }
    }

    public int TotalEventCount => totalEventCount;

    public bool HasEvents => totalEventCount > 0;

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                refreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Summary
    {
        get => summary;
        private set => SetField(ref summary, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public string ExportPreview
    {
        get
        {
            if (!TryResolveRange(out var from, out var to, out var error))
            {
                return error;
            }

            var selectedCount = allEvents.Count(item =>
                item.Timestamp is { } timestamp
                && (from is null || timestamp >= from)
                && (to is null || timestamp <= to));
            if (!isHistoryWindowed && selectedCount == 0)
            {
                return "No timestamped events are inside the export range.";
            }

            var privacy = RedactExport
                ? "Commander identities, sent and received chat, location names, IDs, coordinates, and screenshot paths will be redacted."
                : "Commander identity and selected event content will remain raw.";
            var selection = isHistoryWindowed
                ? $"The selected range will be scanned during export across all {totalEventCount:N0} indexed events"
                : $"{selectedCount:N0} selected event"
                    + (selectedCount == 1 ? string.Empty : "s");
            return selection
                + "; required header, commander, load, and location bootstrap "
                + "events before the range will be added automatically. "
                + privacy
                + " Credentials and API tokens are always removed.";
        }
    }

    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = string.Empty;
        CancelPendingFilter();
        try
        {
            var snapshot = await Task.Run(() => reader.LoadAsync(
                journalDirectory,
                CancellationToken.None), CancellationToken.None);
            allEvents = snapshot.Events;
            totalEventCount = snapshot.TotalEventCount;
            isHistoryWindowed = snapshot.IsWindowed;
            RangeFrom = snapshot.FirstTimestamp;
            RangeTo = snapshot.LastTimestamp;
            Summary = snapshot.TotalEventCount == 0
                ? $"No journal events were found in {snapshot.JournalDirectory}."
                : $"{snapshot.TotalEventCount:N0} events across "
                    + $"{snapshot.FileCount:N0} journal file(s), "
                    + $"{FormatTimestamp(snapshot.FirstTimestamp)} to "
                    + $"{FormatTimestamp(snapshot.LastTimestamp)}."
                    + (snapshot.IsWindowed
                        ? $" Showing the most recent {snapshot.Events.Count:N0}; export scans the full indexed history."
                        : string.Empty);
            ApplyFilter();
            OnPropertyChanged(nameof(TotalEventCount));
            OnPropertyChanged(nameof(HasEvents));
            OnPropertyChanged(nameof(ExportPreview));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or JsonException)
        {
            allEvents = [];
            totalEventCount = 0;
            isHistoryWindowed = false;
            Events = [];
            SelectedEvent = null;
            Summary = "Journal history could not be loaded.";
            StatusMessage = exception.Message;
            OnPropertyChanged(nameof(TotalEventCount));
            OnPropertyChanged(nameof(HasEvents));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> ExportAsync(
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (IsBusy || !HasEvents)
        {
            return false;
        }

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            if (!TryResolveRange(out var from, out var to, out var error))
            {
                StatusMessage = error;
                return false;
            }

            var request = new JournalReplayExportRequest(
                from,
                to,
                RedactExport
                    ? ReplayPrivacyMode.Redacted
                    : ReplayPrivacyMode.Raw,
                sourceVersion,
                presentationSnapshotProvider());
            var result = await Task.Run(() => exporter.ExportAsync(
                    journalDirectory,
                    destinationPath,
                    request,
                    cancellationToken),
                cancellationToken);
            StatusMessage = $"Exported {result.EventCount:N0} events "
                + $"({result.BootstrapEventCount:N0} bootstrap) to {result.Path}.";
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException)
        {
            StatusMessage = "Replay export failed: " + exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        var filter = SearchText.Trim();
        CancelPendingFilter();
        if (filter.Length == 0 || allEvents.Count < BackgroundFilterThreshold)
        {
            ApplyFilteredEvents(FilterEvents(filter));
            return;
        }

        var cancellation = new CancellationTokenSource();
        filterCancellation = cancellation;
        _ = ApplyFilterInBackgroundAsync(filter, cancellation);
    }

    private IReadOnlyList<JournalHistoryEvent> FilterEvents(string filter)
    {
        return filter.Length == 0
            ? allEvents
            : allEvents.Where(item => Contains(item.EventName, filter)
                || Contains(item.FileName, filter)
                || Contains(item.CommanderName, filter)
                || Contains(item.SystemName, filter)
                || Contains(item.RawJson, filter))
                .ToArray();
    }

    private async Task ApplyFilterInBackgroundAsync(
        string filter,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(150, cancellation.Token);
            var filtered = await Task.Run(
                () => FilterEvents(filter),
                cancellation.Token);
            await InvokeOnCapturedContextAsync(() =>
            {
                if (ReferenceEquals(filterCancellation, cancellation)
                    && string.Equals(
                        SearchText.Trim(),
                        filter,
                        StringComparison.Ordinal))
                {
                    ApplyFilteredEvents(filtered);
                }
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer search superseded this one.
        }
        finally
        {
            if (ReferenceEquals(filterCancellation, cancellation))
            {
                filterCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void ApplyFilteredEvents(IReadOnlyList<JournalHistoryEvent> filtered)
    {
        Events = filtered;
        if (SelectedEvent is not null && !Events.Contains(SelectedEvent))
        {
            SelectedEvent = null;
        }
    }

    private Task InvokeOnCapturedContextAsync(Action action)
    {
        if (synchronizationContext is null
            || ReferenceEquals(SynchronizationContext.Current, synchronizationContext))
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        synchronizationContext.Post(
            _ =>
            {
                try
                {
                    action();
                    completion.SetResult();
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            },
            state: null);
        return completion.Task;
    }

    private void CancelPendingFilter()
    {
        var cancellation = filterCancellation;
        filterCancellation = null;
        cancellation?.Cancel();
    }

    public void Dispose()
    {
        CancelPendingFilter();
    }

    private static bool Contains(string? value, string filter)
    {
        return value?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string FormatTimestamp(DateTimeOffset? timestamp)
    {
        return timestamp?.ToString("u", System.Globalization.CultureInfo.InvariantCulture)
            ?? "unknown time";
    }

    private static string FormatEditableTimestamp(DateTimeOffset? timestamp)
    {
        return timestamp?.ToString(
            "yyyy-MM-dd'T'HH:mm:ss.fffK",
            System.Globalization.CultureInfo.InvariantCulture)
            ?? string.Empty;
    }

    private static bool TryParseTimestamp(
        string text,
        out DateTimeOffset? timestamp)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            timestamp = null;
            return true;
        }

        if (DateTimeOffset.TryParse(
                text,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AllowWhiteSpaces
                    | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            timestamp = parsed;
            return true;
        }

        timestamp = null;
        return false;
    }

    private bool TryResolveRange(
        out DateTimeOffset? from,
        out DateTimeOffset? to,
        out string error)
    {
        if (!TryParseTimestamp(RangeFromText, out from))
        {
            to = null;
            error = "Enter a valid ISO timestamp for the export start.";
            return false;
        }

        if (!TryParseTimestamp(RangeToText, out to))
        {
            error = "Enter a valid ISO timestamp for the export end.";
            return false;
        }

        if (from is not null && to is not null && from > to)
        {
            error = "The export start must not be after its end.";
            return false;
        }

        error = string.Empty;
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
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }

    private sealed class AsyncCommand(
        Func<Task> execute,
        Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute();

        public async void Execute(object? parameter)
        {
            await execute();
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
