using System.Text;

namespace SrvSurvey.Core.Journal;

public sealed class JournalDirectoryMonitor
{
    private readonly string journalDirectory;
    private readonly SemaphoreSlim pollLock = new(1, 1);
    private string? currentJournalPath;
    private long currentJournalOffset;
    private byte[] pendingJournalBytes = [];
    private string? statusContentHash;
    private string? navRouteContentHash;
    private string? cargoContentHash;
    private bool hasCompletedFirstPoll;

    public JournalDirectoryMonitor(string journalDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        this.journalDirectory = Path.GetFullPath(journalDirectory);
    }

    public event EventHandler<JournalEventEnvelope>? JournalEventReceived;

    public event EventHandler<EliteStatus>? StatusUpdated;

    public event EventHandler<NavRouteSnapshot>? NavRouteUpdated;

    public event EventHandler<CargoSnapshot>? CargoUpdated;

    public event EventHandler<string>? ReadError;

    public EliteStatus? CurrentStatus { get; private set; }

    public NavRouteSnapshot? CurrentNavRoute { get; private set; }

    public CargoSnapshot? CurrentCargo { get; private set; }

    public string? CurrentJournalPath => currentJournalPath;

    public async Task<JournalMonitorUpdate> PollAsync(
        CancellationToken cancellationToken = default)
    {
        await pollLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        JournalMonitorUpdate update;
        try
        {
            if (!Directory.Exists(journalDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"The journal folder does not exist: {journalDirectory}");
            }

            var events = new List<JournalEventEnvelope>();
            var errors = new List<string>();
            var latestJournal = FindLatestJournal();
            if (latestJournal is not null)
            {
                if (!PathsEqual(latestJournal.FullName, currentJournalPath))
                {
                    FlushPendingLine(events, errors);
                    currentJournalPath = latestJournal.FullName;
                    currentJournalOffset = 0;
                    pendingJournalBytes = [];
                }

                await ReadJournalAppendAsync(events, errors, cancellationToken)
                    .ConfigureAwait(false);
            }

            EliteStatus? status = null;
            var statusPath = Path.Combine(journalDirectory, StatusFileReader.FileName);
            if (File.Exists(statusPath))
            {
                var statusResult = await StatusFileReader.ReadAsync(
                        statusPath,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (statusResult.Status is not null
                    && statusResult.ContentHash is not null
                    && !string.Equals(
                        statusResult.ContentHash,
                        statusContentHash,
                        StringComparison.Ordinal))
                {
                    statusContentHash = statusResult.ContentHash;
                    CurrentStatus = statusResult.Status;
                    status = statusResult.Status;
                }
                else if (statusResult.Error is not null)
                {
                    errors.Add(statusResult.Error);
                }
            }

            NavRouteSnapshot? navRoute = null;
            var navRoutePath = Path.Combine(
                journalDirectory,
                NavRouteFileReader.FileName);
            if (File.Exists(navRoutePath))
            {
                var navRouteResult = await NavRouteFileReader.ReadAsync(
                        navRoutePath,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (navRouteResult.Snapshot is not null
                    && navRouteResult.ContentHash is not null
                    && !string.Equals(
                        navRouteResult.ContentHash,
                        navRouteContentHash,
                        StringComparison.Ordinal))
                {
                    navRouteContentHash = navRouteResult.ContentHash;
                    CurrentNavRoute = navRouteResult.Snapshot;
                    navRoute = navRouteResult.Snapshot;
                }
                else if (navRouteResult.Error is not null)
                {
                    errors.Add(navRouteResult.Error);
                }
            }

            CargoSnapshot? cargo = null;
            var cargoPath = Path.Combine(journalDirectory, CargoFileReader.FileName);
            if (File.Exists(cargoPath))
            {
                var cargoResult = await CargoFileReader.ReadAsync(
                        cargoPath,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (cargoResult.Snapshot is not null
                    && cargoResult.ContentHash is not null
                    && !string.Equals(
                        cargoResult.ContentHash,
                        cargoContentHash,
                        StringComparison.Ordinal))
                {
                    cargoContentHash = cargoResult.ContentHash;
                    CurrentCargo = cargoResult.Snapshot;
                    cargo = cargoResult.Snapshot;
                }
                else if (cargoResult.Error is not null)
                {
                    errors.Add(cargoResult.Error);
                }
            }

            update = new JournalMonitorUpdate(
                currentJournalPath,
                events,
                status,
                navRoute,
                cargo,
                errors,
                IsBootstrapRead: !hasCompletedFirstPoll);
            hasCompletedFirstPoll = true;
        }
        finally
        {
            pollLock.Release();
        }

        foreach (var journalEvent in update.JournalEvents)
        {
            JournalEventReceived?.Invoke(this, journalEvent);
        }

        if (update.Status is not null)
        {
            StatusUpdated?.Invoke(this, update.Status);
        }

        if (update.NavRoute is not null)
        {
            NavRouteUpdated?.Invoke(this, update.NavRoute);
        }

        if (update.Cargo is not null)
        {
            CargoUpdated?.Invoke(this, update.Cargo);
        }

        foreach (var error in update.Errors)
        {
            ReadError?.Invoke(this, error);
        }

        return update;
    }

    public async Task RunAsync(
        TimeSpan? pollingInterval = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var interval = pollingInterval ?? TimeSpan.FromMilliseconds(250);
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollingInterval),
                "The polling interval must be greater than zero.");
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await PollAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException)
            {
                ReadError?.Invoke(this, exception.Message);
            }

            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }
    }

    private FileInfo? FindLatestJournal()
    {
        return new DirectoryInfo(journalDirectory)
            .EnumerateFiles("Journal.*.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private async Task ReadJournalAppendAsync(
        List<JournalEventEnvelope> events,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        if (currentJournalPath is null)
        {
            return;
        }

        await using var stream = new FileStream(
            currentJournalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length < currentJournalOffset)
        {
            currentJournalOffset = 0;
            pendingJournalBytes = [];
        }

        stream.Position = currentJournalOffset;
        using var appendedBytes = new MemoryStream();
        await stream.CopyToAsync(appendedBytes, cancellationToken).ConfigureAwait(false);
        currentJournalOffset = stream.Position;
        if (appendedBytes.Length == 0)
        {
            return;
        }

        var appended = appendedBytes.ToArray();
        var combined = new byte[pendingJournalBytes.Length + appended.Length];
        Buffer.BlockCopy(pendingJournalBytes, 0, combined, 0, pendingJournalBytes.Length);
        Buffer.BlockCopy(appended, 0, combined, pendingJournalBytes.Length, appended.Length);

        var lineStart = 0;
        for (var index = 0; index < combined.Length; index++)
        {
            if (combined[index] != (byte)'\n')
            {
                continue;
            }

            var lineLength = index - lineStart;
            if (lineLength > 0 && combined[index - 1] == (byte)'\r')
            {
                lineLength--;
            }

            ParseLine(combined.AsSpan(lineStart, lineLength), events, errors);
            lineStart = index + 1;
        }

        pendingJournalBytes = combined[lineStart..];
    }

    private void FlushPendingLine(
        List<JournalEventEnvelope> events,
        List<string> errors)
    {
        if (pendingJournalBytes.Length > 0)
        {
            ParseLine(pendingJournalBytes, events, errors);
            pendingJournalBytes = [];
        }
    }

    private static void ParseLine(
        ReadOnlySpan<byte> lineBytes,
        List<JournalEventEnvelope> events,
        List<string> errors)
    {
        if (lineBytes.IsEmpty)
        {
            return;
        }

        string line;
        try
        {
            line = new UTF8Encoding(false, true).GetString(lineBytes);
        }
        catch (DecoderFallbackException exception)
        {
            errors.Add($"A journal line was not valid UTF-8: {exception.Message}");
            return;
        }

        if (JournalEventEnvelope.TryParse(line, out var journalEvent, out var error)
            && journalEvent is not null)
        {
            events.Add(journalEvent);
        }
        else if (error is not null)
        {
            errors.Add($"A journal line could not be parsed: {error}");
        }
    }

    private static bool PathsEqual(string first, string? second)
    {
        return second is not null
            && string.Equals(
                Path.GetFullPath(first),
                Path.GetFullPath(second),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
    }
}

public sealed record JournalMonitorUpdate(
    string? JournalPath,
    IReadOnlyList<JournalEventEnvelope> JournalEvents,
    EliteStatus? Status,
    NavRouteSnapshot? NavRoute,
    CargoSnapshot? Cargo,
    IReadOnlyList<string> Errors,
    bool IsBootstrapRead);
