using System.Text;

namespace SrvSurvey.Core.Journal;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The monitor is process-scoped and its poll gate may have in-flight waiters.")]
public sealed class JournalDirectoryMonitor
{
    private readonly string journalDirectory;
    private readonly string? targetFrontierId;
    private readonly Dictionary<string, JournalIdentityCacheEntry>
        journalIdentityCache;
    private readonly Func<string, CompanionFileStampReadResult>
        companionFileStampReader;
    private readonly Dictionary<string, string> companionFileStampErrors;
    private readonly SemaphoreSlim pollLock = new(1, 1);
    private string? currentJournalPath;
    private long currentJournalOffset;
    private byte[] pendingJournalBytes = [];
    private string? statusContentHash;
    private string? navRouteContentHash;
    private string? cargoContentHash;
    private string? shipLockerContentHash;
    private string? marketContentHash;
    private CompanionFileStamp? statusFileStamp;
    private CompanionFileStamp? navRouteFileStamp;
    private CompanionFileStamp? cargoFileStamp;
    private CompanionFileStamp? shipLockerFileStamp;
    private CompanionFileStamp? marketFileStamp;
    private bool hasCompletedFirstPoll;

    public JournalDirectoryMonitor(
        string journalDirectory,
        string? targetFrontierId = null)
        : this(
            journalDirectory,
            targetFrontierId,
            ReadCompanionFileStamp)
    {
    }

    internal JournalDirectoryMonitor(
        string journalDirectory,
        string? targetFrontierId,
        Func<string, CompanionFileStampReadResult> companionFileStampReader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        ArgumentNullException.ThrowIfNull(companionFileStampReader);
        this.journalDirectory = Path.GetFullPath(journalDirectory);
        this.targetFrontierId = string.IsNullOrWhiteSpace(targetFrontierId)
            ? null
            : targetFrontierId.Trim();
        this.companionFileStampReader = companionFileStampReader;
        journalIdentityCache = new Dictionary<string, JournalIdentityCacheEntry>(
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
        companionFileStampErrors = new Dictionary<string, string>(
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
    }

    public event EventHandler<JournalEventEnvelope>? JournalEventReceived;

    public event EventHandler<EliteStatus>? StatusUpdated;

    public event EventHandler<NavRouteSnapshot>? NavRouteUpdated;

    public event EventHandler<CargoSnapshot>? CargoUpdated;

    public event EventHandler<MarketSnapshot>? MarketUpdated;

    public event EventHandler<string>? ReadError;

    public EliteStatus? CurrentStatus { get; private set; }

    public NavRouteSnapshot? CurrentNavRoute { get; private set; }

    public CargoSnapshot? CurrentCargo { get; private set; }

    public ShipLockerSnapshot? CurrentShipLocker { get; private set; }

    public MarketSnapshot? CurrentMarket { get; private set; }

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
            var latestJournal = await FindLatestJournalAsync(cancellationToken)
                .ConfigureAwait(false);
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
            var statusStampState = GetCompanionFileStamp(
                statusPath,
                errors,
                out var nextStatusFileStamp);
            if (statusStampState == CompanionFileStampState.Available
                && nextStatusFileStamp != statusFileStamp)
            {
                var statusResult = await StatusFileReader.ReadAsync(
                        statusPath,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (statusResult.Status is not null
                    && statusResult.ContentHash is not null)
                {
                    statusFileStamp = nextStatusFileStamp;
                    if (!string.Equals(
                            statusResult.ContentHash,
                            statusContentHash,
                            StringComparison.Ordinal))
                    {
                        statusContentHash = statusResult.ContentHash;
                        CurrentStatus = statusResult.Status;
                        status = statusResult.Status;
                    }
                }
                else if (statusResult.Error is not null)
                {
                    errors.Add(statusResult.Error);
                }
            }
            else if (statusStampState == CompanionFileStampState.Missing)
            {
                statusFileStamp = null;
            }

            NavRouteSnapshot? navRoute = null;
            var navRoutePath = Path.Combine(
                journalDirectory,
                NavRouteFileReader.FileName);
            var navRouteStampState = GetCompanionFileStamp(
                navRoutePath,
                errors,
                out var nextNavRouteFileStamp);
            if (navRouteStampState == CompanionFileStampState.Available
                && nextNavRouteFileStamp != navRouteFileStamp)
            {
                var navRouteResult = await NavRouteFileReader.ReadAsync(
                        navRoutePath,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (navRouteResult.Snapshot is not null
                    && navRouteResult.ContentHash is not null)
                {
                    navRouteFileStamp = nextNavRouteFileStamp;
                    if (!string.Equals(
                            navRouteResult.ContentHash,
                            navRouteContentHash,
                            StringComparison.Ordinal))
                    {
                        navRouteContentHash = navRouteResult.ContentHash;
                        CurrentNavRoute = navRouteResult.Snapshot;
                        navRoute = navRouteResult.Snapshot;
                    }
                }
                else if (navRouteResult.Error is not null)
                {
                    errors.Add(navRouteResult.Error);
                }
            }
            else if (navRouteStampState == CompanionFileStampState.Missing)
            {
                navRouteFileStamp = null;
            }

            CargoSnapshot? cargo = null;
            var cargoPath = Path.Combine(journalDirectory, CargoFileReader.FileName);
            var cargoStampState = GetCompanionFileStamp(
                cargoPath,
                errors,
                out var nextCargoFileStamp);
            if (cargoStampState == CompanionFileStampState.Available
                && nextCargoFileStamp != cargoFileStamp)
            {
                var cargoResult = await CargoFileReader.ReadAsync(
                        cargoPath,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (cargoResult.Snapshot is not null
                    && cargoResult.ContentHash is not null)
                {
                    cargoFileStamp = nextCargoFileStamp;
                    if (!string.Equals(
                            cargoResult.ContentHash,
                            cargoContentHash,
                            StringComparison.Ordinal))
                    {
                        cargoContentHash = cargoResult.ContentHash;
                        CurrentCargo = cargoResult.Snapshot;
                        cargo = cargoResult.Snapshot;
                    }
                }
                else if (cargoResult.Error is not null)
                {
                    errors.Add(cargoResult.Error);
                }
            }
            else if (cargoStampState == CompanionFileStampState.Missing)
            {
                cargoFileStamp = null;
            }

            ShipLockerSnapshot? shipLocker = null;
            var shipLockerPath = Path.Combine(
                journalDirectory,
                ShipLockerFileReader.FileName);
            var shipLockerStampState = GetCompanionFileStamp(
                shipLockerPath,
                errors,
                out var nextShipLockerFileStamp);
            if (shipLockerStampState == CompanionFileStampState.Available
                && nextShipLockerFileStamp != shipLockerFileStamp)
            {
                var shipLockerResult = await ShipLockerFileReader.ReadAsync(
                        shipLockerPath,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (shipLockerResult.Snapshot is not null
                    && shipLockerResult.ContentHash is not null)
                {
                    shipLockerFileStamp = nextShipLockerFileStamp;
                    if (!string.Equals(
                            shipLockerResult.ContentHash,
                            shipLockerContentHash,
                            StringComparison.Ordinal))
                    {
                        shipLockerContentHash = shipLockerResult.ContentHash;
                        CurrentShipLocker = shipLockerResult.Snapshot;
                        shipLocker = shipLockerResult.Snapshot;
                    }
                }
                else if (shipLockerResult.Error is not null)
                {
                    errors.Add(shipLockerResult.Error);
                }
            }
            else if (shipLockerStampState == CompanionFileStampState.Missing)
            {
                shipLockerFileStamp = null;
            }

            MarketSnapshot? market = null;
            var marketPath = Path.Combine(
                journalDirectory,
                MarketFileReader.FileName);
            var marketStampState = GetCompanionFileStamp(
                marketPath,
                errors,
                out var nextMarketFileStamp);
            if (marketStampState == CompanionFileStampState.Available
                && nextMarketFileStamp != marketFileStamp)
            {
                var marketResult = await MarketFileReader.ReadAsync(
                        marketPath,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (marketResult.Snapshot is not null
                    && marketResult.ContentHash is not null)
                {
                    marketFileStamp = nextMarketFileStamp;
                    if (!string.Equals(
                            marketResult.ContentHash,
                            marketContentHash,
                            StringComparison.Ordinal))
                    {
                        marketContentHash = marketResult.ContentHash;
                        CurrentMarket = marketResult.Snapshot;
                        market = marketResult.Snapshot;
                    }
                }
                else if (marketResult.Error is not null)
                {
                    errors.Add(marketResult.Error);
                }
            }
            else if (marketStampState == CompanionFileStampState.Missing)
            {
                marketFileStamp = null;
            }

            update = new JournalMonitorUpdate(
                currentJournalPath,
                events,
                status,
                navRoute,
                cargo,
                market,
                errors,
                IsBootstrapRead: !hasCompletedFirstPoll,
                ShipLocker: shipLocker);
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

        if (update.Market is not null)
        {
            MarketUpdated?.Invoke(this, update.Market);
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

    private async Task<FileInfo?> FindLatestJournalAsync(
        CancellationToken cancellationToken)
    {
        var journals = new DirectoryInfo(journalDirectory)
            .EnumerateFiles("Journal.*.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .ToArray();
        if (targetFrontierId is null)
        {
            return journals.FirstOrDefault();
        }

        foreach (var journal in journals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frontierId = await ReadFrontierIdAsync(journal, cancellationToken)
                .ConfigureAwait(false);
            if (string.Equals(
                    frontierId,
                    targetFrontierId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return journal;
            }
        }

        return null;
    }

    private async Task<string?> ReadFrontierIdAsync(
        FileInfo journal,
        CancellationToken cancellationToken)
    {
        journal.Refresh();
        if (journalIdentityCache.TryGetValue(journal.FullName, out var cached)
            && cached.Length == journal.Length
            && cached.LastWriteTimeUtc == journal.LastWriteTimeUtc)
        {
            return cached.FrontierId;
        }

        string? frontierId = null;
        try
        {
            await using var stream = new FileStream(
                journal.FullName,
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
                if (!JournalEventEnvelope.TryParse(
                        line,
                        out var journalEvent,
                        out _)
                    || journalEvent?.EventName != "Commander"
                    || !journalEvent.Payload.TryGetProperty("FID", out var value)
                    || value.ValueKind != System.Text.Json.JsonValueKind.String)
                {
                    continue;
                }

                frontierId = value.GetString();
                break;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        journal.Refresh();
        journalIdentityCache[journal.FullName] = new JournalIdentityCacheEntry(
            journal.Length,
            journal.LastWriteTimeUtc,
            frontierId);
        return frontierId;
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

    private sealed record JournalIdentityCacheEntry(
        long Length,
        DateTime LastWriteTimeUtc,
        string? FrontierId);

    private CompanionFileStampState GetCompanionFileStamp(
        string path,
        List<string> errors,
        out CompanionFileStamp stamp)
    {
        var result = companionFileStampReader(path);
        if (result.Error is not null)
        {
            stamp = default;
            if (!companionFileStampErrors.TryGetValue(path, out var previousError)
                || !string.Equals(
                    previousError,
                    result.Error,
                    StringComparison.Ordinal))
            {
                companionFileStampErrors[path] = result.Error;
                errors.Add(result.Error);
            }

            return CompanionFileStampState.Error;
        }

        companionFileStampErrors.Remove(path);
        if (result.Stamp is not { } availableStamp)
        {
            stamp = default;
            return CompanionFileStampState.Missing;
        }

        stamp = availableStamp;
        return CompanionFileStampState.Available;
    }

    private static CompanionFileStampReadResult ReadCompanionFileStamp(
        string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                return default;
            }

            return new CompanionFileStampReadResult(
                new CompanionFileStamp(
                    file.Length,
                    file.LastWriteTimeUtc,
                    file.CreationTimeUtc),
                Error: null);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new CompanionFileStampReadResult(
                Stamp: null,
                $"The metadata for {Path.GetFileName(path)} could not be read: "
                    + exception.Message);
        }
    }

    private enum CompanionFileStampState
    {
        Missing,
        Available,
        Error,
    }

    internal readonly record struct CompanionFileStamp(
        long Length,
        DateTime LastWriteTimeUtc,
        DateTime CreationTimeUtc);

    internal readonly record struct CompanionFileStampReadResult(
        CompanionFileStamp? Stamp,
        string? Error);
}

public sealed record JournalMonitorUpdate(
    string? JournalPath,
    IReadOnlyList<JournalEventEnvelope> JournalEvents,
    EliteStatus? Status,
    NavRouteSnapshot? NavRoute,
    CargoSnapshot? Cargo,
    MarketSnapshot? Market,
    IReadOnlyList<string> Errors,
    bool IsBootstrapRead,
    ShipLockerSnapshot? ShipLocker = null)
{
    public bool HasChanges => IsBootstrapRead
        || JournalEvents.Count > 0
        || Status is not null
        || NavRoute is not null
        || Cargo is not null
        || ShipLocker is not null
        || Market is not null
        || Errors.Count > 0;
}
