using Microsoft.Data.Sqlite;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Exploration.History;

/// <summary>Indexes exploration ownership and journal references without copying the original journal payloads.</summary>
public sealed partial class ExplorationHistoryCatalog
{
    private readonly string frontierId;
    private readonly bool isLive;

    public ExplorationHistoryCatalog(string dataDirectory, string frontierId, bool isLive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        if (
            frontierId is "." or ".."
            || frontierId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || frontierId.Contains('/')
            || frontierId.Contains('\\')
        )
        {
            throw new ArgumentException(
                "A commander profile identifier must be a single filename segment.",
                nameof(frontierId)
            );
        }
        this.frontierId = frontierId.ToUpperInvariant();
        this.isLive = isLive;
        FilePath = Path.Combine(
            Path.GetFullPath(dataDirectory),
            "commanders",
            this.frontierId,
            "exploration",
            isLive ? "history-live.sqlite" : "history-legacy.sqlite"
        );
    }

    public string FilePath { get; }

    public Task<ExplorationHistorySummary> SynchronizeAsync(
        IReadOnlyList<string> journalDirectories,
        IProgress<ExplorationHistoryProgress>? progress = null,
        CancellationToken cancellationToken = default
    ) => Task.Run(() => Synchronize(journalDirectories, progress, cancellationToken), cancellationToken);

    /// <summary>Replays restored sources after the user acknowledges an older retry whose missing inputs cannot be proven.</summary>
    public Task<ExplorationHistorySummary> ConfirmRecoveryAsync(
        IReadOnlyList<string> journalDirectories,
        CancellationToken cancellationToken = default
    ) =>
        Task.Run(
            () =>
                IndexSources(
                    () => DiscoverSources(journalDirectories, cancellationToken),
                    journalDirectories,
                    null,
                    true,
                    cancellationToken,
                    confirmRecovery: true
                ),
            cancellationToken
        );

    public Task<ExplorationHistorySummary> ObserveAsync(
        IReadOnlyList<JournalEventEnvelope> events,
        IReadOnlyList<string> journalDirectories,
        CancellationToken cancellationToken = default
    ) => Task.Run(() => Observe(events, journalDirectories, cancellationToken), cancellationToken);

    /// <summary>Reads commander-wide balance eligibility independently of the history page's display filters.</summary>
    public Task<ExplorationHistorySummary> ReadSummaryAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadCatalogSummary(cancellationToken), cancellationToken);

    private ExplorationHistorySummary ReadCatalogSummary(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(FilePath))
        {
            return new();
        }
        using SqliteConnection connection = Open(initialize: false);
        using SqliteTransaction snapshot = connection.BeginTransaction(deferred: true);
        ExplorationHistorySummary result = ReadSummary(connection);
        snapshot.Commit();
        return result;
    }

    public Task<ExplorationHistoryPage> QueryAsync(
        ExplorationHistoryQuery query,
        CancellationToken cancellationToken = default
    ) => Task.Run(() => Query(query, cancellationToken), cancellationToken);

    public Task<ExplorationHistoryDetails> ReadDetailsAsync(
        long rowId,
        CancellationToken cancellationToken = default
    ) => Task.Run(() => ReadDetails(rowId, null, cancellationToken), cancellationToken);

    /// <summary>Follows the retained observation so replayed database IDs cannot redirect a selected row's records.</summary>
    public Task<ExplorationHistoryDetails> ReadDetailsAsync(
        ExplorationHistoryRow row,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(row);
        return Task.Run(
            () =>
                row.Anchor is { } anchor
                    ? ReadDetails(row.Id, anchor, cancellationToken)
                    : new ExplorationHistoryDetails(row.Id, [], UnavailableHistoryMessage),
            cancellationToken
        );
    }
}
