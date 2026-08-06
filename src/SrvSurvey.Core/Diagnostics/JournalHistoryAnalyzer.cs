using System.Globalization;
using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Diagnostics;

public sealed class JournalHistoryAnalyzer
{
    public static DateTimeOffset EliteReleaseDate { get; } =
        new(2014, 12, 15, 0, 0, 0, TimeSpan.Zero);

    public static DateTimeOffset TrailblazersReleaseDate { get; } =
        new(2025, 2, 26, 0, 0, 0, TimeSpan.Zero);

    private readonly string journalDirectory;
    private readonly Func<DateTimeOffset> currentTime;
    private readonly GreenGasGiantCriteriaCatalog greenGasGiantCriteria;

    public JournalHistoryAnalyzer(
        string journalDirectory,
        Func<DateTimeOffset>? currentTime = null,
        GreenGasGiantCriteriaCatalog? greenGasGiantCriteria = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        this.journalDirectory = Path.GetFullPath(journalDirectory);
        this.currentTime = currentTime ?? (() => DateTimeOffset.Now);
        this.greenGasGiantCriteria = greenGasGiantCriteria
            ?? GreenGasGiantCriteriaCatalog.LoadEmbedded();
    }

    public async Task<JournalHistoryAnalysisResult> AnalyzeAsync(
        string frontierId,
        DateTimeOffset startTime,
        IProgress<JournalHistoryAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        if (!Directory.Exists(journalDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The journal folder does not exist: {journalDirectory}");
        }

        var warnings = new List<string>();
        var files = EnumerateJournalCandidates(startTime, warnings);
        var totals = new MutableTotals(greenGasGiantCriteria);
        var counters = new AnalysisCounters();
        for (var index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = files[index];
            await ProcessCandidateAsync(
                    candidate,
                    frontierId,
                    totals,
                    counters,
                    warnings,
                    cancellationToken)
                .ConfigureAwait(false);
            Report(progress, index, files, candidate, totals);
        }

        if (totals.GreenGasGiantMatchesWithoutPosition > 0)
        {
            warnings.Add(
                $"Skipped {totals.GreenGasGiantMatchesWithoutPosition:N0} Green Gas Giant candidate(s) because no journal StarPos was available.");
        }

        return new JournalHistoryAnalysisResult(
            files.Length,
            counters.ProcessedFiles,
            counters.SkippedCommanderFiles,
            counters.SkippedRecentActiveFiles,
            counters.ParsedEvents,
            counters.MalformedLines,
            totals.CreateStatistics(),
            totals.CreateTrailblazersComparison(),
            totals.GreenGasGiantMatches.ToArray(),
            warnings);
    }

    private async Task ProcessCandidateAsync(
        JournalFileCandidate candidate,
        string frontierId,
        MutableTotals totals,
        AnalysisCounters counters,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            var read = await ReadFileAsync(candidate.File, cancellationToken)
                .ConfigureAwait(false);
            counters.MalformedLines += read.MalformedLineCount;
            if (!string.Equals(
                    read.FrontierId,
                    frontierId,
                    StringComparison.OrdinalIgnoreCase))
            {
                counters.SkippedCommanderFiles++;
                return;
            }

            if (ShouldSkipRecentActiveFile(candidate, read))
            {
                counters.SkippedRecentActiveFiles++;
                return;
            }

            counters.ProcessedFiles++;
            counters.ParsedEvents += read.Events.Count;
            foreach (var journalEvent in read.Events)
            {
                totals.Apply(journalEvent);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"{candidate.File.Name}: {exception.Message}");
        }
    }

    private bool ShouldSkipRecentActiveFile(
        JournalFileCandidate candidate,
        JournalHistoryFileRead read)
    {
        var recentCutoff = currentTime().AddDays(-2);
        return !read.IsShutdown && candidate.OpenedAt > recentCutoff;
    }

    public static bool TryGetJournalTimestamp(
        string fileName,
        out DateTimeOffset timestamp)
    {
        timestamp = default;
        var parts = fileName.Split('.');
        if (parts.Length < 3
            || !string.Equals(parts[0], "Journal", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var format = parts[1].Contains('-', StringComparison.Ordinal)
            ? "yyyy-MM-ddTHHmmss"
            : "yyMMddHHmmss";
        return DateTimeOffset.TryParseExact(
            parts[1],
            format,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out timestamp);
    }

    private JournalFileCandidate[] EnumerateJournalCandidates(
        DateTimeOffset startTime,
        List<string> warnings)
    {
        return new DirectoryInfo(journalDirectory)
            .EnumerateFiles("Journal.*.log", SearchOption.TopDirectoryOnly)
            .Select(file => new JournalFileCandidate(
                file,
                TryGetJournalTimestamp(file.Name, out var timestamp)
                    ? timestamp
                    : null))
            .Where(candidate =>
            {
                if (candidate.OpenedAt is null)
                {
                    warnings.Add(
                        $"Ignored {candidate.File.Name} because its journal timestamp is invalid.");
                    return false;
                }

                return candidate.OpenedAt > startTime
                    && candidate.File.LastWriteTimeUtc >= startTime.UtcDateTime;
            })
            .OrderBy(candidate => candidate.OpenedAt)
            .ThenBy(candidate => candidate.File.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<JournalHistoryFileRead> ReadFileAsync(
        FileInfo file,
        CancellationToken cancellationToken)
    {
        var events = new List<JournalEventEnvelope>();
        var malformed = 0;
        string? frontierId = null;
        var isShutdown = false;
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
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
               is { } line)
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
                malformed++;
                continue;
            }

            events.Add(journalEvent);
            if (journalEvent.EventName is "Commander" or "LoadGame")
            {
                frontierId = GetString(journalEvent.Payload, "FID")
                    ?? frontierId;
            }
            else if (journalEvent.EventName == "Shutdown")
            {
                isShutdown = true;
            }
        }

        return new JournalHistoryFileRead(
            frontierId,
            isShutdown,
            events,
            malformed);
    }

    private static void Report(
        IProgress<JournalHistoryAnalysisProgress>? progress,
        int index,
        IReadOnlyList<JournalFileCandidate> files,
        JournalFileCandidate candidate,
        MutableTotals totals)
    {
        progress?.Report(new JournalHistoryAnalysisProgress(
            index + 1,
            files.Count,
            candidate.File.Name,
            totals.JumpCount,
            totals.OrganismCount));
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private sealed record JournalFileCandidate(
        FileInfo File,
        DateTimeOffset? OpenedAt);

    private sealed record JournalHistoryFileRead(
        string? FrontierId,
        bool IsShutdown,
        IReadOnlyList<JournalEventEnvelope> Events,
        int MalformedLineCount);

    private sealed class AnalysisCounters
    {
        public int ProcessedFiles { get; set; }

        public int SkippedCommanderFiles { get; set; }

        public int SkippedRecentActiveFiles { get; set; }

        public int MalformedLines { get; set; }

        public int ParsedEvents { get; set; }
    }

    private sealed class MutableTotals(
        GreenGasGiantCriteriaCatalog greenGasGiantCriteria)
    {
        private DateTimeOffset? firstEvent;
        private DateTimeOffset? lastEvent;
        private long cargoBought;
        private long cargoSold;
        private long cargoTransferred;
        private long cargoCollected;
        private long cargoContributed;
        private long docked;
        private long touchdown;
        private long died;
        private long bodyCount;
        private double jumpDistance;
        private GalacticCoordinate? currentStarPosition;
        private readonly List<HistoricalGreenGasGiantMatch>
            greenGasGiantMatches = [];
        private readonly MutableCargoTransactions beforeTrailblazers = new();
        private readonly MutableCargoTransactions afterTrailblazers = new();

        public long JumpCount { get; private set; }

        public long OrganismCount { get; private set; }

        public IReadOnlyList<HistoricalGreenGasGiantMatch>
            GreenGasGiantMatches => greenGasGiantMatches;

        public int GreenGasGiantMatchesWithoutPosition { get; private set; }

        public void Apply(JournalEventEnvelope journalEvent)
        {
            var timestamp = journalEvent.Timestamp;
            if (timestamp is not null)
            {
                firstEvent = firstEvent is null || timestamp < firstEvent
                    ? timestamp
                    : firstEvent;
                lastEvent = lastEvent is null || timestamp > lastEvent
                    ? timestamp
                    : lastEvent;
            }

            var root = journalEvent.Payload;
            if (journalEvent.EventName is "LoadGame"
                or "Location"
                or "FSDJump"
                or "CarrierJump")
            {
                currentStarPosition = TryGetCoordinate(root, "StarPos");
            }

            var trailblazers = timestamp < TrailblazersReleaseDate
                ? beforeTrailblazers
                : afterTrailblazers;
            switch (journalEvent.EventName)
            {
                case "FSDJump":
                    JumpCount++;
                    jumpDistance += GetDouble(root, "JumpDist") ?? 0;
                    break;
                case "Scan":
                    var tag = greenGasGiantCriteria.Match(
                        GetString(root, "PlanetClass"),
                        GetDouble(root, "SurfaceTemperature")
                            ?? double.NaN);
                    if (tag is null)
                    {
                        break;
                    }

                    if (currentStarPosition is not { } starPosition)
                    {
                        GreenGasGiantMatchesWithoutPosition++;
                        break;
                    }

                    greenGasGiantMatches.Add(
                        new HistoricalGreenGasGiantMatch(
                            tag,
                            starPosition,
                            journalEvent.RawJson,
                            journalEvent.Timestamp));
                    break;
                case "ApproachBody":
                    bodyCount++;
                    break;
                case "ScanOrganic" when string.Equals(
                    GetString(root, "ScanType"),
                    "Analyse",
                    StringComparison.OrdinalIgnoreCase):
                    OrganismCount++;
                    break;
                case "MarketBuy":
                    var bought = GetInt64(root, "Count") ?? 0;
                    cargoBought += bought;
                    trailblazers.Bought += bought;
                    break;
                case "MarketSell":
                    var sold = GetInt64(root, "Count") ?? 0;
                    cargoSold += sold;
                    trailblazers.Sold += sold;
                    break;
                case "CargoTransfer":
                    var transferred = SumArray(root, "Transfers", "Count");
                    cargoTransferred += transferred;
                    trailblazers.Transferred += transferred;
                    break;
                case "CollectCargo":
                    cargoCollected++;
                    break;
                case "ColonisationContribution":
                    cargoContributed += SumArray(
                        root,
                        "Contributions",
                        "Amount");
                    break;
                case "Docked":
                    docked++;
                    break;
                case "Touchdown":
                    touchdown++;
                    break;
                case "Died":
                    died++;
                    break;
            }
        }

        public JournalHistoryStatistics CreateStatistics()
        {
            return new JournalHistoryStatistics(
                firstEvent,
                lastEvent,
                JumpCount,
                jumpDistance,
                bodyCount,
                OrganismCount,
                cargoBought,
                cargoSold,
                cargoTransferred,
                cargoCollected,
                cargoContributed,
                docked,
                touchdown,
                died);
        }

        public TrailblazersCargoComparison CreateTrailblazersComparison()
        {
            return new TrailblazersCargoComparison(
                beforeTrailblazers.Create(),
                afterTrailblazers.Create());
        }

        private static long SumArray(
            JsonElement root,
            string arrayName,
            string valueName)
        {
            if (!root.TryGetProperty(arrayName, out var array)
                || array.ValueKind != JsonValueKind.Array)
            {
                return 0;
            }

            return array.EnumerateArray()
                .Sum(item => GetInt64(item, valueName) ?? 0);
        }

        private static long? GetInt64(JsonElement root, string propertyName)
        {
            return root.TryGetProperty(propertyName, out var value)
                && value.TryGetInt64(out var result)
                    ? result
                    : null;
        }

        private static double? GetDouble(
            JsonElement root,
            string propertyName)
        {
            return root.TryGetProperty(propertyName, out var value)
                && value.TryGetDouble(out var result)
                && double.IsFinite(result)
                    ? result
                    : null;
        }

        private static GalacticCoordinate? TryGetCoordinate(
            JsonElement root,
            string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var value)
                || value.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var components = value.EnumerateArray().ToArray();
            return components.Length == 3
                && components.All(component =>
                    component.ValueKind == JsonValueKind.Number
                    && component.TryGetDouble(out var number)
                    && double.IsFinite(number))
                ? new GalacticCoordinate(
                    components[0].GetDouble(),
                    components[1].GetDouble(),
                    components[2].GetDouble())
                : null;
        }
    }

    private sealed class MutableCargoTransactions
    {
        public long Bought { get; set; }

        public long Sold { get; set; }

        public long Transferred { get; set; }

        public CargoTransactionStatistics Create() =>
            new(Bought, Sold, Transferred);
    }
}

public sealed record JournalHistoryAnalysisProgress(
    int ProcessedFileCount,
    int TotalFileCount,
    string CurrentFile,
    long JumpCount,
    long OrganismCount);

public sealed record JournalHistoryAnalysisResult(
    int CandidateFileCount,
    int ProcessedFileCount,
    int SkippedCommanderFileCount,
    int SkippedRecentActiveFileCount,
    int ParsedEventCount,
    int MalformedLineCount,
    JournalHistoryStatistics Statistics,
    TrailblazersCargoComparison Trailblazers,
    IReadOnlyList<HistoricalGreenGasGiantMatch> GreenGasGiantMatches,
    IReadOnlyList<string> Warnings);

public sealed record HistoricalGreenGasGiantMatch(
    string Tag,
    GalacticCoordinate StarPosition,
    string RawJournalJson,
    DateTimeOffset? Timestamp);

public sealed record JournalHistoryStatistics(
    DateTimeOffset? FirstEvent,
    DateTimeOffset? LastEvent,
    long JumpCount,
    double JumpDistanceLy,
    long BodyApproachCount,
    long OrganismAnalysisCount,
    long CargoBought,
    long CargoSold,
    long CargoTransferred,
    long CargoCollected,
    long CargoContributed,
    long DockedCount,
    long TouchdownCount,
    long DeathCount);

public sealed record TrailblazersCargoComparison(
    CargoTransactionStatistics Before,
    CargoTransactionStatistics After);

public sealed record CargoTransactionStatistics(
    long Bought,
    long Sold,
    long Transferred);
