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

        var filesResult = TryEnumerateJournalFiles();
        if (filesResult.Error is not null)
        {
            return CommanderCodexJournalImportResult.Failed(filesResult.Error);
        }

        var files = filesResult.Files!;
        var warnings = new List<string>();
        var totals = new ImportTotals();
        for (var index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[index];
            await ImportFileSafelyAsync(
                    file,
                    frontierId,
                    warnings,
                    totals,
                    cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new CommanderCodexJournalImportProgress(
                index + 1,
                files.Length,
                file.Name,
                totals.DiscoveryEvents,
                totals.ChangedEntries));
        }

        return new CommanderCodexJournalImportResult(
            files.Length,
            totals.ParsedEvents,
            totals.MalformedLines,
            totals.DiscoveryEvents,
            totals.ChangedEntries,
            warnings);
    }

    private (FileInfo[]? Files, string? Error) TryEnumerateJournalFiles()
    {
        try
        {
            var files = new DirectoryInfo(journalDirectory)
                .EnumerateFiles("Journal.*.log", SearchOption.TopDirectoryOnly)
                .OrderBy(file => file.Name, StringComparer.Ordinal)
                .ToArray();
            return (files, null);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return (null, exception.Message);
        }
    }

    private async Task ImportFileSafelyAsync(
        FileInfo file,
        string frontierId,
        List<string> warnings,
        ImportTotals totals,
        CancellationToken cancellationToken)
    {
        try
        {
            var fileResult = await ImportFileAsync(
                    file,
                    frontierId,
                    warnings,
                    cancellationToken)
                .ConfigureAwait(false);
            totals.ParsedEvents += fileResult.ParsedEvents;
            totals.MalformedLines += fileResult.MalformedLines;
            totals.DiscoveryEvents += fileResult.DiscoveryEvents;
            totals.ChangedEntries += fileResult.ChangedEntries;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"{file.Name}: {exception.Message}");
        }
    }

    private sealed class ImportTotals
    {
        public int ParsedEvents { get; set; }

        public int MalformedLines { get; set; }

        public int DiscoveryEvents { get; set; }

        public int ChangedEntries { get; set; }
    }

    private async Task<FileImportCounts> ImportFileAsync(
        FileInfo file,
        string frontierId,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var tracker = new CommanderCodexJournalTracker(
            store,
            frontierIdFilter: frontierId);
        var batch = new List<JournalEventEnvelope>(BatchSize);
        var parsedEvents = 0;
        var malformedLines = 0;
        var discoveryEvents = 0;
        var changedEntries = 0;
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
        return new FileImportCounts(
            parsedEvents,
            malformedLines,
            discoveryEvents,
            changedEntries);
    }

    private readonly record struct FileImportCounts(
        int ParsedEvents,
        int MalformedLines,
        int DiscoveryEvents,
        int ChangedEntries);

    private static async Task ApplyBatchAsync(
        CommanderCodexJournalTracker tracker,
        List<JournalEventEnvelope> batch,
        List<string> warnings,
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
