using SrvSurvey.Core.Diagnostics;

namespace SrvSurvey.Core.Search;

/// <summary>Keeps provider failures and price availability local to each asynchronous search.</summary>
internal sealed class MiningSearchDiagnostics(Func<Action<string>?> output)
{
    private sealed class State(Action<string>? log)
    {
        public ProviderFailureLog Failures { get; } = new();
        public Action<string>? Log { get; } = log;
        public bool PriceMarksUnavailable { get; set; }
    }

    private sealed class Scope(MiningSearchDiagnostics owner, State state, State? previous) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            owner.Flush(state);
            owner.active.Value = previous;
        }
    }

    private readonly AsyncLocal<State?> active = new();
    private readonly State unscoped = new(null);

    private State Current => active.Value ?? unscoped;

    public bool PriceMarksUnavailable
    {
        get => Current.PriceMarksUnavailable;
        set => Current.PriceMarksUnavailable = value;
    }

    public IDisposable Begin()
    {
        State? previous = active.Value;
        var state = new State(output());
        active.Value = state;
        return new Scope(this, state, previous);
    }

    public void Record(string provider, string route, Exception exception) =>
        Current.Failures.Record(provider, route, exception);

    public void Reset()
    {
        _ = Current.Failures.Drain();
        Current.PriceMarksUnavailable = false;
    }

    public void Flush() => Flush(Current);

    private void Flush(State state)
    {
        Action<string>? log = ReferenceEquals(state, unscoped) ? output() : state.Log;
        foreach (string line in state.Failures.Drain())
        {
            try
            {
                log?.Invoke(line);
            }
            catch (Exception)
            {
                // Diagnostics must never interrupt a search.
            }
        }
    }
}
