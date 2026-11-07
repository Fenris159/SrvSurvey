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

    public Task<ExplorationHistorySummary> ObserveAsync(
        IReadOnlyList<JournalEventEnvelope> events,
        IReadOnlyList<string> journalDirectories,
        CancellationToken cancellationToken = default
    ) => Task.Run(() => Observe(events, journalDirectories, cancellationToken), cancellationToken);

    public Task<ExplorationHistoryPage> QueryAsync(
        ExplorationHistoryQuery query,
        CancellationToken cancellationToken = default
    ) => Task.Run(() => Query(query, cancellationToken), cancellationToken);

    public Task<ExplorationHistoryDetails> ReadDetailsAsync(
        long rowId,
        CancellationToken cancellationToken = default
    ) => Task.Run(() => ReadDetails(rowId, cancellationToken), cancellationToken);
}
