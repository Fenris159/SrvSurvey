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
    private CancellationTokenSource? pending;
    private bool busy;

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

    public void Save(TSnapshot snapshot) => Store?.Save(Workspace, FilterKey(), snapshot);

    /// <summary>Shows the saved result for the current filters, or clears stale results when none was saved.</summary>
    public void RestoreSaved(Action<TSnapshot> restore, Action clear)
    {
        if (IsRestoring || IsBusy || Store is null)
        {
            return;
        }

        if (Store.Load<TSnapshot>(Workspace, FilterKey()) is { } saved)
        {
            restore(saved);
        }
        else
        {
            clear();
        }
    }

    /// <summary>Applies restored filters without treating them as commander edits.</summary>
    public void Restore(Action apply)
    {
        IsRestoring = true;
        try
        {
            apply();
        }
        finally
        {
            IsRestoring = false;
        }
    }

    public async Task RunAsync(
        Func<CancellationToken, Task> search,
        Action<MiningSearchOutcome> report,
        MiningSearchRunOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        options ??= MiningSearchRunOptions.Default;
        CancellationTokenSource? previous = pending;
        using var current = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.Timeout is { } timeout)
        {
            current.CancelAfter(timeout);
        }

        pending = current;
        SetBusy(true);
        report(MiningSearchOutcome.Started);
        provider.ResetDiagnostics();
        CancellationToken token = current.Token;
        try
        {
            if (previous is not null)
            {
                await previous.CancelAsync();
            }

            await search(token);
            if (pending == current)
            {
                report(MiningSearchOutcome.Completed);
            }
        }
        catch (OperationCanceledException)
        {
            if (pending == current)
            {
                report(MiningSearchOutcome.Canceled);
            }
        }
        catch (Exception ex)
            when (MiningProviderFailure.Is(ex) || (options.InvalidOperationFails && ex is InvalidOperationException))
        {
            if (pending == current)
            {
                report(MiningSearchOutcome.Failed);
            }
        }
        catch (ArgumentException ex)
        {
            if (pending == current)
            {
                report(new MiningSearchOutcome(MiningSearchOutcomeKind.Rejected, ex.Message));
            }
        }
        finally
        {
            provider.FlushDiagnostics();
            if (pending == current)
            {
                pending = null;
                SetBusy(false);
            }
        }
    }

    public void Cancel() => pending?.Cancel();

    /// <summary>Cancels the in-flight search so that it can no longer report or release the busy state.</summary>
    public void Abandon()
    {
        pending?.Cancel();
        pending = null;
        SetBusy(false);
    }

    public void Dispose()
    {
        pending?.Cancel();
        pending?.Dispose();
        pending = null;
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
