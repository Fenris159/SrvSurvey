using System.Text;

namespace SrvSurvey.Core.Journal;

public static class JournalSnapshotReader
{
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

        var latestJournal = directory
            .EnumerateFiles("Journal.*.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .FirstOrDefault();

        if (latestJournal is null)
        {
            throw new FileNotFoundException(
                $"No Journal.*.log files were found in: {journalFolder}");
        }

        await using var stream = new FileStream(
            latestJournal.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);

        return await ReadAsync(reader, latestJournal.FullName, cancellationToken);
    }

    public static async Task<JournalSnapshot> ReadAsync(
        TextReader reader,
        string? sourcePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var state = new JournalSessionState();
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

        return state.CreateSnapshot(sourcePath, malformedLineCount);
    }
}
