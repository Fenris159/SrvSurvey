using System.Text;

namespace SrvSurvey.Core.Journal;

public static class JournalSnapshotReader
{
    private const int MaximumBootstrapJournalCount = 8;

    public static async Task<JournalSnapshot> ReadLatestAsync(
        string journalFolder,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalFolder);

        var directory = new DirectoryInfo(journalFolder);
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException(
                $"The journal folder does not exist: {journalFolder}");
        }

        var journals = directory
            .EnumerateFiles("Journal.*.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .Take(MaximumBootstrapJournalCount)
            .ToArray();

        if (journals.Length == 0)
        {
            throw new FileNotFoundException(
                $"No Journal.*.log files were found in: {journalFolder}");
        }

        var latestOnly = await ReadFileAsync(
                journals[0],
                cancellationToken)
            .ConfigureAwait(false);
        if (HasBootstrapIdentity(latestOnly))
        {
            return latestOnly;
        }

        var state = new JournalSessionState();
        var malformedLineCount = 0;
        foreach (var journal in journals.Reverse())
        {
            await using var stream = new FileStream(
                journal.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            malformedLineCount += await ApplyAsync(
                    reader,
                    state,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return state.CreateSnapshot(
            journals[0].FullName,
            malformedLineCount);
    }

    public static async Task<JournalSnapshot> ReadAsync(
        TextReader reader,
        string? sourcePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var state = new JournalSessionState();
        var malformedLineCount = await ApplyAsync(
                reader,
                state,
                cancellationToken)
            .ConfigureAwait(false);

        return state.CreateSnapshot(sourcePath, malformedLineCount);
    }

    private static async Task<int> ApplyAsync(
        TextReader reader,
        JournalSessionState state,
        CancellationToken cancellationToken)
    {
        var malformedLineCount = 0;

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
                malformedLineCount++;
                continue;
            }

            state.Apply(journalEvent);
        }

        return malformedLineCount;
    }

    private static async Task<JournalSnapshot> ReadFileAsync(
        FileInfo journal,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            journal.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        return await ReadAsync(
                reader,
                journal.FullName,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool HasBootstrapIdentity(JournalSnapshot snapshot)
    {
        return !string.IsNullOrWhiteSpace(snapshot.GameVersion)
            && snapshot.IsOdyssey is not null
            && !string.IsNullOrWhiteSpace(snapshot.CommanderName)
            && !string.IsNullOrWhiteSpace(snapshot.FrontierId)
            && !string.IsNullOrWhiteSpace(snapshot.SystemName)
            && snapshot.SystemAddress is not null;
    }
}
