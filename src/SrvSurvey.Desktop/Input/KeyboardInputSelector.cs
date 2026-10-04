namespace SrvSurvey.Desktop.Input;

/// <summary>Identifies the independent providers of configured keyboard shortcuts.</summary>
internal enum KeyboardInputSource
{
    Desktop,
    NestedDisplay,
    Portal,
}

/// <summary>Remembers one game-proven provider for all shortcuts and merges duplicate cross-provider presses.</summary>
internal sealed class KeyboardInputSelector
{
    private readonly Lock gate = new();
    private KeyboardInputSource? selected;
    private KeyboardInputMode mode;
    private long lastPreferredReport;
    private KeyboardInputSource? recoverySource;
    private long recoveryReport;
    private readonly Dictionary<GlobalInputAction, Selection> recent = [];

    private sealed record Selection(KeyboardInputSource Source, long Timestamp, string Chord);

    /// <summary>Reports one shared detected or explicitly selected source.</summary>
    public KeyboardInputMode? SelectedMode
    {
        get
        {
            lock (gate)
            {
                if (mode != KeyboardInputMode.Automatic)
                {
                    return mode;
                }
                return selected is { } source ? (KeyboardInputMode)((int)source + 1) : null;
            }
        }
    }

    /// <summary>Changes the global override and clears stale detection evidence.</summary>
    public void SetMode(KeyboardInputMode value)
    {
        lock (gate)
        {
            mode = Enum.IsDefined(value) ? value : KeyboardInputMode.Automatic;
            Reset();
        }
    }

    /// <summary>Accepts one activation, promoting a better provider without executing a duplicate press.</summary>
    public bool TryAccept(
        KeyboardInputSource source,
        GlobalInputAction action,
        string chord,
        long timestamp,
        bool learn = true,
        bool canLearn = true
    )
    {
        lock (gate)
        {
            if (mode != KeyboardInputMode.Automatic && (int)mode != (int)source + 1)
            {
                return false;
            }
            if (
                mode == KeyboardInputMode.Automatic
                && learn
                && selected is KeyboardInputSource preferred
                && (source < preferred || (!canLearn && source != preferred))
                && (!canLearn || !CanRecover(source, timestamp))
            )
            {
                return false;
            }
            bool duplicate =
                recent.TryGetValue(action, out Selection? previous)
                && source != previous.Source
                && string.Equals(chord, previous.Chord, StringComparison.OrdinalIgnoreCase)
                && timestamp >= previous.Timestamp
                && System.Diagnostics.Stopwatch.GetElapsedTime(previous.Timestamp, timestamp)
                    < TimeSpan.FromMilliseconds(200);
            if (learn && canLearn)
            {
                selected = source;
                lastPreferredReport = timestamp;
                recoverySource = null;
            }
            recent[action] = new Selection(source, timestamp, chord);
            return !duplicate;
        }
    }

    /// <summary>Requires two separated valid reports after preferred input stops, never treating idle time as failure.</summary>
    private bool CanRecover(KeyboardInputSource source, long timestamp)
    {
        if (
            timestamp < lastPreferredReport
            || System.Diagnostics.Stopwatch.GetElapsedTime(lastPreferredReport, timestamp)
                < TimeSpan.FromMilliseconds(500)
        )
        {
            return false;
        }
        if (
            recoverySource == source
            && timestamp >= recoveryReport
            && System.Diagnostics.Stopwatch.GetElapsedTime(recoveryReport, timestamp) >= TimeSpan.FromMilliseconds(500)
        )
        {
            recoverySource = null;
            return true;
        }
        if (recoverySource != source)
        {
            recoverySource = source;
            recoveryReport = timestamp;
        }
        return false;
    }

    /// <summary>Allows fallback when a provider disappears or its configuration changes.</summary>
    public void Reset(KeyboardInputSource? source = null)
    {
        lock (gate)
        {
            if (source is null || selected == source)
            {
                selected = null;
                lastPreferredReport = 0;
                recoverySource = null;
            }
            foreach (
                GlobalInputAction action in recent
                    .Where(pair => source is null || pair.Value.Source == source)
                    .Select(pair => pair.Key)
                    .ToArray()
            )
            {
                recent.Remove(action);
            }
        }
    }
}
