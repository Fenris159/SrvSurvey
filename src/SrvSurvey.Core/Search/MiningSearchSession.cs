using System.Text.Json;

namespace SrvSurvey.Core.Search;

public enum MiningSearchOutcomeKind
{
    Started,
    Completed,
    Canceled,
    Failed,
    Rejected,
}

/// <summary>A search lifecycle event reported while the search is still the current one.</summary>
public sealed record MiningSearchOutcome(MiningSearchOutcomeKind Kind, string Message = "")
{
    public static MiningSearchOutcome Started { get; } = new(MiningSearchOutcomeKind.Started);
    public static MiningSearchOutcome Completed { get; } = new(MiningSearchOutcomeKind.Completed);
    public static MiningSearchOutcome Canceled { get; } = new(MiningSearchOutcomeKind.Canceled);
    public static MiningSearchOutcome Failed { get; } = new(MiningSearchOutcomeKind.Failed);
}

public static class MiningProviderFailure
{
    /// <summary>Whether a provider request failed in a way the commander can retry.</summary>
    public static bool Is(Exception ex) =>
        ex is HttpRequestException or JsonException or IOException or InvalidDataException;
}

/// <summary>A completed readout and the search context that produced it.</summary>
public sealed record AcceptedMiningSearch<TSnapshot>(TSnapshot Snapshot, string FilterKey, string Reference);

public sealed record MiningSearchRunOptions(TimeSpan? Timeout = null, bool InvalidOperationFails = false)
{
    public static MiningSearchRunOptions Default { get; } = new();
}

/// <summary>
/// Runs one mining search workspace's provider searches: a new search supersedes the previous one,
/// completed results are remembered by their complete filter key, and a restored search keeps its
/// reference system until the commander edits it.
/// </summary>
public sealed class MiningSearchSession<TSnapshot> : IMiningSearchRestoreState, IDisposable
    where TSnapshot : class
{
    private readonly IMiningSearchProvider provider;
    private readonly Func<object> filters;
    private PendingSearch? pending;
    private bool busy;
    private Action? restoreAfterSearch;

    public MiningSearchSession(IMiningSearchProvider provider, string workspace, Func<object> filters)
    {
        this.provider = provider;
        this.filters = filters;
        Workspace = workspace;
        Reference = new MiningSearchReference(this);
    }

    public event Action? BusyChanged;

    public bool IsBusy => busy;

    public bool IsRestoring { get; private set; }

    public AcceptedMiningSearch<TSnapshot>? AcceptedResult { get; private set; }

    public bool HasIncompleteResult { get; private set; }

    public MiningSearchOutcomeKind? LastOutcome { get; private set; }

    public MiningSearchReference Reference { get; }

    public IMiningSearchResultStore? Store { get; private set; }

    public string Workspace { get; private set; }

    public void UseStore(IMiningSearchResultStore store, string workspace)
    {
        Store = store;
        Workspace = workspace;
    }

    public string FilterKey() => JsonSerializer.Serialize(filters());

    public TSnapshot? LoadLast() => Store?.LoadLast<TSnapshot>(Workspace);

    public void Save(TSnapshot snapshot)
    {
        string key = FilterKey();
        if (pending is { } current && (current.Cancellation.IsCancellationRequested || key != current.FilterKey))
        {
            return;
        }

        Store?.Save(Workspace, key, snapshot);
        AcceptedResult = new(snapshot, key, Reference.Value);
    }

    /// <summary>Applies presentation progress or results only while their search is still current.</summary>
    public void Publish(Action apply, CancellationToken token)
    {
        if (pending is { Cancellation.IsCancellationRequested: false } current && current.Cancellation.Token == token)
        {
            apply();
        }
    }

    /// <summary>Shows the saved result for the current filters, or clears stale results when none was saved.</summary>
    public void RestoreSaved(Action<TSnapshot> restore, Action clear)
    {
        if (IsRestoring || Store is null)
        {
            return;
        }
        if (IsBusy)
        {
            restoreAfterSearch = () => RestoreSaved(restore, clear);
            return;
        }

        if (Store.Load<TSnapshot>(Workspace, FilterKey()) is { } saved)
        {
            RestoreResult(saved, () => restore(saved), pinReference: true);
        }
        else
        {
            AcceptedResult = null;
            clear();
        }
    }

    /// <summary>Restores one accepted readout without interpreting its saved filters as user edits.</summary>
    public void RestoreResult(TSnapshot snapshot, Action apply, bool pinReference)
    {
        Abandon();
        try
        {
            Restore(apply);
            AcceptedResult = new(snapshot, FilterKey(), Reference.Value);
            HasIncompleteResult = false;
            LastOutcome = MiningSearchOutcomeKind.Completed;
        }
        finally
        {
            if (pinReference)
            {
                Reference.Pin();
            }
        }
    }

    /// <summary>Clears accepted and pending work, then restores current-location following.</summary>
    public void Reset(Action apply)
    {
        Abandon();
        AcceptedResult = null;
        HasIncompleteResult = false;
        LastOutcome = null;
        Reference.Unpin();
        Reference.Follow();
        Restore(apply);
    }

    /// <summary>Records a provider failure or partial result without depending on presentation text.</summary>
    public void MarkIncomplete(CancellationToken token)
    {
        if (pending is { } current && current.Cancellation.Token == token)
        {
            current.Incomplete = true;
            HasIncompleteResult = true;
        }
    }

    /// <summary>Applies restored filters without treating them as commander edits.</summary>
    public void Restore(Action apply)
    {
        bool previous = IsRestoring;
        IsRestoring = true;
        try
        {
            apply();
        }
        finally
        {
            IsRestoring = previous;
        }
    }

    public Task RunAsync(
        Func<CancellationToken, Task> search,
        Action<MiningSearchOutcome> report,
        MiningSearchRunOptions? options = null,
        CancellationToken cancellationToken = default
    ) => RunCoreAsync(search, report, null, options, cancellationToken);

    public Task RunAndCaptureAsync(
        Func<CancellationToken, Task> search,
        Action<MiningSearchOutcome> report,
        Func<TSnapshot> capture,
        MiningSearchRunOptions? options = null,
        CancellationToken cancellationToken = default
    ) => RunCoreAsync(search, report, capture, options, cancellationToken);

    private async Task RunCoreAsync(
        Func<CancellationToken, Task> search,
        Action<MiningSearchOutcome> report,
        Func<TSnapshot>? capture,
        MiningSearchRunOptions? options,
        CancellationToken cancellationToken
    )
    {
        options ??= MiningSearchRunOptions.Default;
        if (!string.IsNullOrWhiteSpace(Reference.Value))
        {
            Reference.Pin();
        }
        string key = FilterKey();
        PendingSearch? previous = pending;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var current = new PendingSearch(cancellation, key);
        if (options.Timeout is { } timeout)
        {
            cancellation.CancelAfter(timeout);
        }

        pending = current;
        HasIncompleteResult = false;
        SetBusy(true);
        Report(MiningSearchOutcome.Started, report);
        using IDisposable diagnosticScope = provider.BeginDiagnostics();
        CancellationToken token = cancellation.Token;
        try
        {
            if (previous is not null)
            {
                await previous.Cancellation.CancelAsync();
            }

            token.ThrowIfCancellationRequested();
            await search(token);
            token.ThrowIfCancellationRequested();
            CompleteSearch(current, capture, report);
        }
        catch (OperationCanceledException)
        {
            if (pending == current)
            {
                Report(MiningSearchOutcome.Canceled, report);
            }
        }
        catch (Exception ex)
            when (MiningProviderFailure.Is(ex) || (options.InvalidOperationFails && ex is InvalidOperationException))
        {
            if (pending == current)
            {
                Report(MiningSearchOutcome.Failed, report);
            }
        }
        catch (ArgumentException ex)
        {
            if (pending == current)
            {
                Report(new MiningSearchOutcome(MiningSearchOutcomeKind.Rejected, ex.Message), report);
            }
        }
        finally
        {
            FinishSearch(current);
        }
    }

    private void FinishSearch(PendingSearch current)
    {
        if (pending != current)
        {
            return;
        }
        pending = null;
        bool filtersChanged = current.FilterKey != FilterKey();
        Action? restore = restoreAfterSearch;
        restoreAfterSearch = null;
        SetBusy(false);
        if (filtersChanged && !IsBusy)
        {
            restore?.Invoke();
        }
    }

    private void CompleteSearch(PendingSearch current, Func<TSnapshot>? capture, Action<MiningSearchOutcome> report)
    {
        if (pending != current)
        {
            return;
        }
        if (!current.Incomplete && current.FilterKey == FilterKey() && capture is not null)
        {
            Save(capture());
        }
        Report(MiningSearchOutcome.Completed, report);
    }

    public void Cancel() => pending?.Cancellation.Cancel();

    /// <summary>Cancels the in-flight search so that it can no longer report or release the busy state.</summary>
    public void Abandon()
    {
        PendingSearch? abandoned = pending;
        pending = null;
        restoreAfterSearch = null;
        abandoned?.Cancellation.Cancel();
        SetBusy(false);
    }

    public void Dispose() => Abandon();

    // The session only tracks the active run. RunAsync owns the cancellation source
    // and disposes it after the search work finishes.
    private sealed class PendingSearch(CancellationTokenSource cancellation, string filterKey)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;

        public string FilterKey { get; } = filterKey;
        public bool Incomplete { get; set; }
    }

    private void Report(MiningSearchOutcome outcome, Action<MiningSearchOutcome> report)
    {
        LastOutcome = outcome.Kind;
        report(outcome);
    }

    private void SetBusy(bool value)
    {
        if (busy == value)
        {
            return;
        }

        busy = value;
        BusyChanged?.Invoke();
    }
}

/// <summary>Keeps a search reference on the commander's system until it is edited or pinned by a restored search.</summary>
public sealed class MiningSearchReference
{
    private readonly IMiningSearchRestoreState session;
    private bool tracksCommander;
    private bool pinned;

    internal MiningSearchReference(IMiningSearchRestoreState session) => this.session = session;

    public string CurrentSystem { get; private set; } = "";

    internal string Value { get; private set; } = "";

    /// <summary>Normalizes an edited reference; an emptied field falls back to the commander's system.</summary>
    public string Edit(string? value, out bool refilled)
    {
        if (!session.IsRestoring)
        {
            pinned = false;
        }

        string next = value ?? "";
        if (next.Length == 0 && CurrentSystem.Length > 0)
        {
            next = CurrentSystem;
        }

        refilled = string.IsNullOrEmpty(value) && next.Length > 0;
        tracksCommander = next.Equals(CurrentSystem, StringComparison.OrdinalIgnoreCase);
        Value = next;
        return next;
    }

    /// <summary>Records the commander's new system and returns whether the reference should follow it.</summary>
    public bool Move(string? system, string reference, out string next)
    {
        next = system ?? "";
        string previous = CurrentSystem;
        CurrentSystem = next;
        if (next.Length == 0 || pinned)
        {
            return false;
        }

        if (tracksCommander || reference.Length == 0 || reference.Equals(previous, StringComparison.OrdinalIgnoreCase))
        {
            tracksCommander = true;
            return true;
        }

        return false;
    }

    public void Follow() => tracksCommander = true;

    public void Pin()
    {
        pinned = true;
        tracksCommander = false;
    }

    public void Unpin() => pinned = false;
}

internal interface IMiningSearchRestoreState
{
    bool IsRestoring { get; }
}
