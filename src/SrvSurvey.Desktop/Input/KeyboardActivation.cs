namespace SrvSurvey.Desktop.Input;

/// <summary>Describes what a source can observe about Elite's focus when it reports input.</summary>
internal enum KeyboardFocusEvidence
{
    /// <summary>Only the desktop window tracker can confirm Elite focus.</summary>
    DesktopWindow,

    /// <summary>A separate game display reports Elite as its active window.</summary>
    GameDisplayForeground,

    /// <summary>The compositor delivered the shortcut without revealing which window had focus.</summary>
    Untracked,
}

/// <summary>Samples SrvSurvey and Elite focus at the moment a source reports a key or shortcut.</summary>
internal readonly record struct KeyboardFocus(
    long Timestamp,
    bool ApplicationActive,
    bool GameFocused,
    bool AllowsShortcuts
);

/// <summary>A configured-shortcut candidate: a raw chord to route, or an action the compositor already resolved.</summary>
internal readonly record struct KeyboardActivation(
    KeyboardInputSource Source,
    string Chord,
    GlobalInputAction? Action,
    KeyboardFocus Focus
);

/// <summary>Identifies a separate game display and the Elite process it belongs to.</summary>
internal sealed record GameDisplayConnection(string Display, int ProcessId);

/// <summary>Reports one source's connection, game evidence, and optional desktop shortcut settings.</summary>
internal sealed record KeyboardSourceState(bool IsRunning)
{
    /// <summary>False while a connected source carries only some bindings, so it cannot become the shared source.</summary>
    public bool CanServeAllBindings { get; init; } = true;

    /// <summary>Reports that the source's own display shows Elite as its active window.</summary>
    public bool IsGameForeground { get; init; }

    public GameDisplayConnection? GameDisplay { get; init; }

    public DesktopShortcutSettingsState? DesktopShortcutSettings { get; init; }
}

/// <summary>Reports configured-shortcut activations from one keyboard pathway and owns that pathway's lifecycle.</summary>
internal interface IKeyboardActivationSource : IAsyncDisposable
{
    KeyboardInputSource Kind { get; }

    KeyboardSourceState State { get; }

    /// <summary>Completes when silent startup discovery has finished, before startup game focus.</summary>
    Task StartupReady => Task.CompletedTask;

    /// <summary>Connects the source to the module that routes its activations; called once before any other member.</summary>
    void Attach(IKeyboardActivationSink sink);

    /// <summary>Begins listening with the current configuration.</summary>
    void Start(GlobalInputSettings settings);

    /// <summary>Applies keyboard enablement and bindings without restarting the application.</summary>
    void Update(GlobalInputSettings settings);

    /// <summary>Forgets held-key state while retaining bindings and desktop permissions.</summary>
    void ResetDetection();

    /// <summary>Opens desktop shortcut configuration only when explicitly requested from input settings.</summary>
    Task OpenDesktopShortcutSettingsAsync() => Task.CompletedTask;
}

/// <summary>Receives focus queries, activations, and listener status from keyboard sources.</summary>
internal interface IKeyboardActivationSink
{
    /// <summary>Gets the shared listener status so a source can replace it conditionally.</summary>
    string Status { get; }

    /// <summary>Samples focus once per input, letting raw sources ignore keys typed in other applications.</summary>
    KeyboardFocus SampleFocus(KeyboardFocusEvidence evidence);

    /// <summary>Routes, selects, and dispatches one activation.</summary>
    void Activate(KeyboardActivation activation);

    /// <summary>Publishes a listener status line, or only refreshes diagnostics for an empty message.</summary>
    void ReportStatus(string message, string? expectedStatus = null);

    /// <summary>Allows fallback after a source stops or can no longer carry every binding.</summary>
    void ReleaseSelection(KeyboardInputSource source);
}
