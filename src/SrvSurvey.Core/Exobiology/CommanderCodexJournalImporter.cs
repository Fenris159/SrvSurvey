using System.Text;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Exobiology;

public sealed class CommanderCodexJournalImporter(
    string journalDirectory,
    CommanderCodexStore store)
{
    private const int BatchSize = 2_048;

    private readonly string journalDirectory = Path.GetFullPath(
        string.IsNullOrWhiteSpace(journalDirectory)
            ? throw new ArgumentException(
                "A journal directory is required.",
                nameof(journalDirectory))
            : journalDirectory);
    private readonly CommanderCodexStore store = store
        ?? throw new ArgumentNullException(nameof(store));

    public async Task<CommanderCodexJournalImportResult> ImportAsync(
        string frontierId,
        IProgress<CommanderCodexJournalImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        if (!Directory.Exists(journalDirectory))
        {
            return CommanderCodexJournalImportResult.Failed(
                $"The journal folder does not exist: {journalDirectory}");
        }

        FileInfo[] files;
        try
        {
            files = new DirectoryInfo(journalDirectory)
                .EnumerateFiles("Journal.*.log", SearchOption.TopDirectoryOnly)
                .OrderBy(file => file.Name, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return CommanderCodexJournalImportResult.Failed(exception.Message);
        }

        var warnings = new List<string>();
        var parsedEvents = 0;
        var malformedLines = 0;
        var discoveryEvents = 0;
        var changedEntries = 0;
        for (var index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[index];
            var tracker = new CommanderCodexJournalTracker(
                store,
                frontierIdFilter: frontierId);
            var batch = new List<JournalEventEnvelope>(BatchSize);
            try
            {
                await using var stream = new FileStream(
                    file.FullName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    16 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var reader = new StreamReader(
                    stream,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true);
                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (!JournalEventEnvelope.TryParse(
                            line,
                            out var journalEvent,
                            out _)
                        || journalEvent is null)
                    {
                        malformedLines++;
                        continue;
                    }

                    parsedEvents++;
                    batch.Add(journalEvent);
                    if (batch.Count >= BatchSize)
                    {
                        await ApplyBatchAsync(
                            tracker,
                            batch,
                            warnings,
                            result =>
                            {
                                discoveryEvents += result.DiscoveryEventCount;
                                changedEntries += result.ChangedEntryCount;
                            },
                            cancellationToken).ConfigureAwait(false);
                    }
                }

                await ApplyBatchAsync(
                    tracker,
                    batch,
                    warnings,
                    result =>
                    {
                        discoveryEvents += result.DiscoveryEventCount;
                        changedEntries += result.ChangedEntryCount;
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"{file.Name}: {exception.Message}");
            }

            progress?.Report(new CommanderCodexJournalImportProgress(
                index + 1,
                files.Length,
                file.Name,
                discoveryEvents,
                changedEntries));
        }

        return new CommanderCodexJournalImportResult(
            files.Length,
            parsedEvents,
            malformedLines,
            discoveryEvents,
            changedEntries,
            warnings);
    }

    private static async Task ApplyBatchAsync(
        CommanderCodexJournalTracker tracker,
        List<JournalEventEnvelope> batch,
        ICollection<string> warnings,
        Action<CommanderCodexJournalTrackResult> applyResult,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        var result = await tracker.ApplyAsync(batch, cancellationToken)
            .ConfigureAwait(false);
        foreach (var warning in result.Warnings)
        {
            warnings.Add(warning);
        }

        applyResult(result);
        batch.Clear();
    }
}

public sealed record CommanderCodexJournalImportProgress(
    int ProcessedFileCount,
    int TotalFileCount,
    string CurrentFile,
    int DiscoveryEventCount,
    int ChangedEntryCount);

public sealed record CommanderCodexJournalImportResult(
    int JournalFileCount,
    int ParsedEventCount,
    int MalformedLineCount,
    int DiscoveryEventCount,
    int ChangedEntryCount,
    IReadOnlyList<string> Warnings)
{
    public bool IsSuccess => Warnings.Count == 0;

    public static CommanderCodexJournalImportResult Failed(string error)
    {
        return new CommanderCodexJournalImportResult(
            0,
            0,
            0,
            0,
            0,
            [error]);
    }
}
