using System.Collections.Concurrent;
using Avalonia;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Input;

/// <summary>Reports synthetic activations through the same sink contract as the real keyboard sources.</summary>
internal sealed class FakeKeyboardSource(KeyboardInputSource kind) : IKeyboardActivationSource
{
    private IKeyboardActivationSink? sink;

    public KeyboardInputSource Kind => kind;
    public KeyboardSourceState State { get; set; } = new(true);
    public Task StartupReady { get; set; } = Task.CompletedTask;
    public int Starts { get; private set; }
    public int Updates { get; private set; }
    public int Resets { get; private set; }
    public int SettingsRequests { get; private set; }
    public bool Disposed { get; private set; }
    public Exception? DisposeException { get; init; }

    private IKeyboardActivationSink Sink => sink ?? throw new InvalidOperationException("The source is not attached.");

    public void Attach(IKeyboardActivationSink sink) => this.sink = sink;

    public void Start(GlobalInputSettings settings) => Starts++;

    public void Update(GlobalInputSettings settings) => Updates++;

    public void ResetDetection() => Resets++;

    public Task OpenDesktopShortcutSettingsAsync()
    {
        SettingsRequests++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return DisposeException is { } failure ? ValueTask.FromException(failure) : ValueTask.CompletedTask;
    }

    /// <summary>Reports a raw chord, or an already-resolved action, with this kind's own focus evidence.</summary>
    public void Report(string chord, GlobalInputAction? action = null) =>
        Sink.Activate(new KeyboardActivation(kind, chord, action, Sink.SampleFocus(Evidence)));

    /// <summary>Publishes a listener status line from this source.</summary>
    public void ReportStatus(string message) => Sink.ReportStatus(message);

    /// <summary>Models a source disconnecting: it stops running and releases any learned selection.</summary>
    public void Disconnect()
    {
        State = new KeyboardSourceState(false);
        Sink.ReleaseSelection(kind);
        Sink.ReportStatus(string.Empty);
    }

    private KeyboardFocusEvidence Evidence =>
        kind switch
        {
            KeyboardInputSource.Portal => KeyboardFocusEvidence.Untracked,
            KeyboardInputSource.NestedDisplay when State.IsGameForeground =>
                KeyboardFocusEvidence.GameDisplayForeground,
            _ => KeyboardFocusEvidence.DesktopWindow,
        };
}

/// <summary>Records what a keyboard source reports and answers focus queries with a controllable clock.</summary>
internal sealed class RecordingKeyboardSink : IKeyboardActivationSink
{
    public Action? BeforeFocus { get; set; }
    public Action? BeforeActivation { get; set; }
    public long Clock { get; set; }
    public bool AllowsShortcuts { get; set; } = true;
    public ConcurrentQueue<KeyboardActivation> Activations { get; } = new();
    public ConcurrentQueue<KeyboardFocusEvidence> Evidence { get; } = new();
    public ConcurrentQueue<string> Statuses { get; } = new();
    public ConcurrentQueue<KeyboardInputSource> Releases { get; } = new();
    public string Status { get; private set; } = string.Empty;

    public IEnumerable<string> Chords => Activations.Select(activation => activation.Chord);

    public KeyboardFocus SampleFocus(KeyboardFocusEvidence evidence)
    {
        BeforeFocus?.Invoke();
        Evidence.Enqueue(evidence);
        return new KeyboardFocus(Clock, false, AllowsShortcuts, AllowsShortcuts);
    }

    public void Activate(KeyboardActivation activation)
    {
        BeforeActivation?.Invoke();
        Activations.Enqueue(activation);
    }

    public void ReportStatus(string message, string? expectedStatus = null)
    {
        Statuses.Enqueue(message);
        if (message.Length > 0 && (expectedStatus is null || expectedStatus == Status))
        {
            Status = message;
        }
    }

    public void ReleaseSelection(KeyboardInputSource source) => Releases.Enqueue(source);
}

/// <summary>Supplies a changeable game window and can hold one focus query open to expose disposal races.</summary>
internal sealed class TestGameWindowTracker : IGameWindowTracker
{
    private readonly ManualResetEventSlim allowSnapshot = new();
    private readonly TaskCompletionSource snapshotEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool blocking;
    private int disposed;

    public static GameWindowSnapshot Foreground { get; } = new(1, 1, new PixelRect(0, 0, 100, 100), true, true);

    public GameWindowSnapshot Snapshot { get; set; } = GameWindowSnapshot.Unavailable;
    public bool Focused
    {
        get => Snapshot.IsForeground;
        set => Snapshot = value ? Foreground : GameWindowSnapshot.Unavailable;
    }
    public Task SnapshotEntered => snapshotEntered.Task;
    public bool IsDisposed => Volatile.Read(ref disposed) != 0;

    /// <summary>Blocks later focus queries until the test releases them once.</summary>
    public void BlockSnapshots() => blocking = true;

    public void AllowSnapshot() => allowSnapshot.Set();

    public Action? BeforeSnapshot { get; set; }

    public GameWindowSnapshot GetSnapshot()
    {
        BeforeSnapshot?.Invoke();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (blocking && !allowSnapshot.IsSet)
        {
            snapshotEntered.TrySetResult();
            if (!allowSnapshot.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The test did not release the blocked tracker snapshot.");
            }
        }
        return Snapshot;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref disposed, 1);
        allowSnapshot.Dispose();
    }
}
