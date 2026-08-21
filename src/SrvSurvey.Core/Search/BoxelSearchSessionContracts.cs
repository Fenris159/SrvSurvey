using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Search;

public interface IBoxelSearchSession : IAsyncDisposable
{
    BoxelSearchSessionSnapshot Current { get; }

    event EventHandler<BoxelSearchSessionChangedEventArgs>? Changed;

    Task<BoxelSearchOutcome> SwitchProfileAsync(
        BoxelSearchProfile profile,
        CancellationToken cancellationToken = default);

    Task<BoxelSearchOutcome> ClearProfileAsync(
        BoxelSearchMessageCode reason = BoxelSearchMessageCode.ProfileUnavailable,
        CancellationToken cancellationToken = default);

    Task<BoxelSearchOutcome> ApplyAsync(
        BoxelSearchUpdate update,
        CancellationToken cancellationToken = default);

    Task<BoxelSearchOutcome> ExecuteAsync(
        BoxelSearchAction action,
        CancellationToken cancellationToken = default);

    Task<BoxelSearchLibrarySnapshot> GetLibraryAsync(
        CancellationToken cancellationToken = default);
}

public interface IBoxelSearchProfileStore
{
    Task SaveBoxelSearchAsync(
        string frontierId,
        string? commanderName,
        bool isOdyssey,
        BoxelSearchSnapshot boxelSearch,
        CancellationToken cancellationToken = default);
}

public interface IBoxelSearchLibraryStore
{
    Task<IReadOnlyList<SavedBoxelSearchCatalogEntry>> ListAsync(
        string frontierId,
        CancellationToken cancellationToken = default);

    Task<SavedBoxelSearchDocument> CreateAsync(
        string frontierId,
        string name,
        string? notes,
        BoxelSearchSnapshot search,
        CancellationToken cancellationToken = default);

    Task<SavedBoxelSearchDocument> LoadAsync(
        string frontierId,
        string fileName,
        CancellationToken cancellationToken = default);

    Task<SavedBoxelSearchDocument> SaveProgressAsync(
        string frontierId,
        string fileName,
        BoxelSearchSnapshot search,
        CancellationToken cancellationToken = default);

    Task<SavedBoxelSearchDocument> RenameAsync(
        string frontierId,
        string fileName,
        string name,
        CancellationToken cancellationToken = default);

    Task<SavedBoxelSearchDocument> SaveNotesAsync(
        string frontierId,
        string fileName,
        string? notes,
        CancellationToken cancellationToken = default);

    Task<SavedBoxelSearchDocument> SetFavoriteAsync(
        string frontierId,
        string fileName,
        bool isFavorite,
        CancellationToken cancellationToken = default);

    Task<string> DeleteAsync(
        string frontierId,
        string fileName,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        string frontierId,
        string fileName,
        CancellationToken cancellationToken = default);
}

public interface IBoxelLocalSystemReader
{
    Task<LegacySystemDataReadResult> ReadAsync(
        string frontierId,
        BoxelAddress boxel,
        CancellationToken cancellationToken = default);

    Task<LegacySystemDataReadResult> ReadAllAsync(
        string frontierId,
        CancellationToken cancellationToken = default);
}

public interface IBoxelEmptyStore
{
    Task<IReadOnlySet<string>> LoadGroupAsync(
        BoxelAddress boxel,
        CancellationToken cancellationToken = default);
}

public interface IBoxelClipboard
{
    bool IsReady { get; }

    Task WriteTextAsync(string text, CancellationToken cancellationToken = default);
}

public interface IBoxelSearchDiagnosticSink
{
    void Report(BoxelSearchDiagnostic diagnostic);
}

public sealed record BoxelSearchProfile(
    string FrontierId,
    string? CommanderName,
    bool IsOdyssey,
    BoxelSearchSnapshot Search);

public sealed record BoxelSearchUpdate
{
    public bool HasCurrentSystem { get; init; }

    public string? CurrentSystemName { get; init; }

    public GalacticCoordinate? CurrentPosition { get; init; }

    public long? CurrentSystemAddress { get; init; }

    public bool HasRoute { get; init; }

    public NavRouteSnapshot? Route { get; init; }

    public IReadOnlyList<JournalEventEnvelope> JournalEvents { get; init; } = [];

    public bool HasStatus { get; init; }

    public EliteStatus? Status { get; init; }

    public string? MusicTrack { get; init; }

    public bool IsGalaxyMapOpen { get; init; }

    public bool AllowAutoCopy { get; init; } = true;
}

public abstract record BoxelSearchAction;

public sealed record ActivateBoxelSearch(BoxelSearchActivationRequest Request)
    : BoxelSearchAction;

public sealed record StopBoxelSearch : BoxelSearchAction
{
    public static StopBoxelSearch Instance { get; } = new();

    public bool PreservesProgress => true;
}

public sealed record SetBoxelAutoCopy(bool Enabled) : BoxelSearchAction;

public sealed record SetBoxelSortDirection(bool Descending) : BoxelSearchAction;

public sealed record RefreshCurrentBoxel : BoxelSearchAction;

public sealed record NavigateToBoxel(BoxelAddress Boxel) : BoxelSearchAction;

public sealed record SetExpectedSystemCount(int Count) : BoxelSearchAction;

public sealed record MarkNextBoxelSystemEmpty : BoxelSearchAction;

public sealed record CompleteBoxelSystem(string SystemName) : BoxelSearchAction;

public sealed record ReopenBoxelSystem(string SystemName) : BoxelSearchAction;

public sealed record DeferBoxelSystem(string SystemName) : BoxelSearchAction;

public sealed record StartBoxelSurveyAt(string SystemName) : BoxelSearchAction;

public sealed record AuditAllBoxels : BoxelSearchAction;

public sealed record CancelBoxelAudit : BoxelSearchAction;

public sealed record CopyNextBoxelSystem(bool Automatic = false) : BoxelSearchAction;

public sealed record SaveBoxelSearchToLibrary(string? Name, string? Notes)
    : BoxelSearchAction;

public sealed record ResumeSavedBoxelSearch(string FileName) : BoxelSearchAction;

public sealed record RenameSavedBoxelSearch(string FileName, string Name)
    : BoxelSearchAction;

public sealed record UpdateSavedBoxelSearchNotes(string FileName, string? Notes)
    : BoxelSearchAction;

public sealed record SetSavedBoxelSearchFavorite(string FileName, bool IsFavorite)
    : BoxelSearchAction;

public sealed record DeleteSavedBoxelSearch(string FileName) : BoxelSearchAction;

public enum BoxelSearchOutcomeKind
{
    Success,
    NoChange,
    Rejected,
    AppliedWithWarnings,
    AppliedNotPersisted,
    Cancelled,
}

public enum BoxelSearchMessageCode
{
    None,
    ProfileLoaded,
    ProfileUnavailable,
    SearchNotConfigured,
    SearchLoadedInactive,
    SearchActivated,
    SearchStopped,
    SearchInvalid,
    SearchSavedToLibrary,
    SearchAlreadySavedToLibrary,
    LibraryDetailsRequired,
    LibraryUnavailable,
    SavedSearchResumed,
    SavedSearchRenamed,
    SavedSearchNotesUpdated,
    SavedSearchFavoriteUpdated,
    SavedSearchDeleted,
    RefreshStarted,
    RefreshCompleted,
    RefreshFailed,
    AuditStarted,
    AuditCompleted,
    AuditCancelled,
    AuditFailed,
    NavigationChanged,
    ExpectedSystemCountChanged,
    SystemCompleted,
    SystemReopened,
    SystemDeferred,
    SurveyStartChanged,
    NextSystemMarkedEmpty,
    NextSystemCopied,
    ClipboardNotReady,
    ClipboardFailed,
    AutoCopyChanged,
    SortDirectionChanged,
    SynchronizationDegraded,
    SynchronizationRestored,
    Superseded,
}

public sealed record BoxelSearchWarning(
    BoxelSearchHealthSubsystem Subsystem,
    BoxelSearchMessageCode Code,
    string? Detail = null);

public sealed record BoxelSearchOutcome(
    BoxelSearchOutcomeKind Kind,
    BoxelSearchMessageCode Code,
    long SessionVersion,
    long SearchVersion,
    long ContextVersion,
    long ActivityVersion,
    long HealthVersion,
    long LibraryRevision,
    string? PrimaryValue = null,
    string? SecondaryValue = null,
    int Count = 0,
    int Total = 0,
    SavedBoxelSearchDocument? SavedSearch = null,
    IReadOnlyList<BoxelSearchWarning>? Warnings = null);

public sealed record BoxelSearchSessionChangedEventArgs(
    BoxelSearchSessionSnapshot Previous,
    BoxelSearchSessionSnapshot Current);

public sealed record BoxelSearchSessionSnapshot(
    long Version,
    BoxelSearchSessionSearchSnapshot Search,
    BoxelSearchContextSnapshot Context,
    BoxelSearchActivitySnapshot Activity,
    BoxelSearchHealthSnapshot Health,
    long LibraryRevision)
{
    public static BoxelSearchSessionSnapshot Empty { get; } = new(
        0,
        BoxelSearchSessionSearchSnapshot.Empty,
        BoxelSearchContextSnapshot.Empty,
        BoxelSearchActivitySnapshot.Empty,
        BoxelSearchHealthSnapshot.Empty,
        0);
}

public sealed record BoxelSearchSessionSearchSnapshot
{
    public required long Version { get; init; }

    public required BoxelSearchSnapshot Persistence { get; init; }

    public required string? NextSystem { get; init; }

    public required string? NextSystemAscending { get; init; }

    public required string? NextSystemDescending { get; init; }

    public required bool CurrentIsEmpty { get; init; }

    public required int CurrentMinimumSystemNumber { get; init; }

    public required int CurrentMaximumSystemNumber { get; init; }

    public required int CompletedSystemCount { get; init; }

    public required int TotalCompletedSystemCount { get; init; }

    public required int CompletedBoxelCount { get; init; }

    public required int TotalBoxelCount { get; init; }

    public required bool CurrentSystemsComplete { get; init; }

    public required IReadOnlyList<BoxelSystemState> Systems { get; init; }

    public required IReadOnlyList<BoxelAddress> Boxels { get; init; }

    public required IReadOnlySet<string> EmptyBoxelPrefixes { get; init; }

    public bool IsActive => Persistence.Active;

    public BoxelAddress? TopBoxel => Persistence.TopBoxel;

    public BoxelAddress? CurrentBoxel => Persistence.Current;

    public int CurrentCount => Persistence.CurrentCount;

    public bool AutoCopy => Persistence.AutoCopy;

    public bool SortDescending => Persistence.SortDescending;

    public char LowMassCode => Persistence.LowMassCode;

    public BoxelCompletionMode CompletionMode => Persistence.CompletionMode;

    public string? SavedSearchFileName => Persistence.SavedSearchFileName;

    public IReadOnlyList<string> EmptySystems => Persistence.EmptySystems;

    public IReadOnlyList<string> DeferredSystems => Persistence.DeferredSystems;

    public BoxelProgress GetProgress(BoxelAddress boxel)
    {
        ArgumentNullException.ThrowIfNull(boxel);
        if (!Persistence.ProgressByPrefix.TryGetValue(
                boxel.Prefix,
                out var expectedSystemCount))
        {
            return BoxelProgress.Unknown;
        }

        var expected = Math.Max(0, expectedSystemCount);
        var complete = Persistence.CompletedPrefixes.Contains(
            boxel.Prefix,
            StringComparer.Ordinal);
        var completed = complete
            ? expected
            : Persistence.CompletedSystems.Count(name => IsInBoxel(name, boxel.Prefix))
                + Persistence.EmptySystems.Count(name => IsInBoxel(name, boxel.Prefix));
        return new BoxelProgress(
            expected,
            Math.Min(completed, expected),
            complete,
            expectedSystemCount < 0);
    }

    public bool IsSystemDeferred(string prefix, int systemNumber)
    {
        var generatedName = BoxelAddress.Parse(prefix + systemNumber).GeneratedName;
        if (Persistence.DeferredSystems.Contains(generatedName, StringComparer.Ordinal))
        {
            return true;
        }

        return Persistence.DeferredRanges.Any(range =>
            string.Equals(range.Prefix, prefix, StringComparison.Ordinal)
            && range.Contains(systemNumber));
    }

    public static BoxelSearchSessionSearchSnapshot Empty { get; } = new()
    {
        Version = 0,
        Persistence = BoxelSearchSnapshot.Empty,
        NextSystem = null,
        NextSystemAscending = null,
        NextSystemDescending = null,
        CurrentIsEmpty = false,
        CurrentMinimumSystemNumber = -1,
        CurrentMaximumSystemNumber = 0,
        CompletedSystemCount = 0,
        TotalCompletedSystemCount = 0,
        CompletedBoxelCount = 0,
        TotalBoxelCount = 0,
        CurrentSystemsComplete = false,
        Systems = [],
        Boxels = [],
        EmptyBoxelPrefixes = new HashSet<string>(StringComparer.Ordinal),
    };

    private static bool IsInBoxel(string systemName, string prefix)
    {
        return BoxelAddress.TryParse(systemName, out var address)
            && address is not null
            && string.Equals(address.Prefix, prefix, StringComparison.Ordinal);
    }
}

public sealed record BoxelSearchContextSnapshot(
    long Version,
    BoxelSearchProfileIdentity? Profile,
    string? CurrentSystemName,
    GalacticCoordinate? CurrentPosition,
    long? CurrentSystemAddress,
    NavRouteSnapshot? Route,
    EliteStatus? Status,
    string? MusicTrack,
    bool IsGalaxyMapOpen,
    string? LastCopiedSystemName)
{
    public static BoxelSearchContextSnapshot Empty { get; } = new(
        0,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        false,
        null);
}

public sealed record BoxelSearchProfileIdentity(
    long Generation,
    string FrontierId,
    string? CommanderName,
    bool IsOdyssey);

public enum BoxelSearchActivityKind
{
    Idle,
    Refreshing,
    Auditing,
    CancellingAudit,
}

public sealed record BoxelSearchActivitySnapshot(
    long Version,
    BoxelSearchActivityKind Kind,
    int Processed,
    int Total,
    string? Prefix)
{
    public static BoxelSearchActivitySnapshot Empty { get; } = new(
        0,
        BoxelSearchActivityKind.Idle,
        0,
        0,
        null);
}

public enum BoxelSearchHealthSubsystem
{
    ProfilePersistence,
    LibraryPersistence,
    Resolver,
    Clipboard,
    LocalData,
}

public enum BoxelSearchHealthSeverity
{
    Healthy,
    Warning,
    Error,
}

public sealed record BoxelSearchHealthIssue(
    BoxelSearchHealthSubsystem Subsystem,
    BoxelSearchHealthSeverity Severity,
    BoxelSearchMessageCode Code,
    DateTimeOffset OccurredAt,
    string? SafeDetail = null);

public sealed record BoxelSearchHealthSnapshot(
    long Version,
    IReadOnlyDictionary<BoxelSearchHealthSubsystem, BoxelSearchHealthIssue> Issues)
{
    public bool IsHealthy => Issues.Count == 0;

    public BoxelSearchHealthSeverity Severity => Issues.Count == 0
        ? BoxelSearchHealthSeverity.Healthy
        : Issues.Values.Max(issue => issue.Severity);

    public static BoxelSearchHealthSnapshot Empty { get; } = new(
        0,
        new Dictionary<BoxelSearchHealthSubsystem, BoxelSearchHealthIssue>());
}

public sealed record BoxelSearchLibrarySnapshot(
    long Revision,
    IReadOnlyList<SavedBoxelSearchCatalogEntry> Entries);

public sealed record BoxelSearchDiagnostic(
    BoxelSearchHealthSubsystem Subsystem,
    BoxelSearchMessageCode Code,
    Exception? Exception,
    DateTimeOffset OccurredAt,
    string? Context = null);

public sealed record BoxelSearchSessionOptions
{
    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaximumRetryDelay { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed record BoxelSearchSessionServices
{
    public IBoxelClipboard? Clipboard { get; init; }

    public IBoxelSearchDiagnosticSink? Diagnostics { get; init; }

    public TimeProvider? TimeProvider { get; init; }

    public BoxelSearchSessionOptions? Options { get; init; }
}

public sealed class NullBoxelSearchDiagnosticSink : IBoxelSearchDiagnosticSink
{
    public static NullBoxelSearchDiagnosticSink Instance { get; } = new();

    private NullBoxelSearchDiagnosticSink()
    {
    }

    public void Report(BoxelSearchDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
    }
}
