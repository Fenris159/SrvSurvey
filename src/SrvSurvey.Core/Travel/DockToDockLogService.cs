using System.Globalization;
using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Travel;

public sealed class DockToDockLogService
{
    private readonly DockToDockCsvWriter writer;
    private readonly TimeProvider timeProvider;
    private CargoSnapshot? cargo;
    private DockToDockTrip? activeTrip;
    private DockedLocation? lastDocked;
    private string? systemName;
    private long? systemAddress;
    private int? bodyId;
    private string? bodyName;
    private double? bodyDistanceLs;
    private string? shipType;
    private string? shipName;
    private double? shipMaximumJump;

    public DockToDockLogService(
        string outputPath,
        TimeProvider? timeProvider = null)
    {
        writer = new DockToDockCsvWriter(outputPath);
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string OutputPath => writer.OutputPath;

    public bool HasActiveTrip => activeTrip is not null;

    public void ClearCargo()
    {
        cargo = null;
    }

    public DockToDockApplyResult Apply(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        CargoSnapshot? currentCargo,
        bool enabled,
        bool isBootstrapRead)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        cargo = currentCargo ?? cargo;
        var completed = new List<DockToDockLogEntry>();
        foreach (var journalEvent in journalEvents)
        {
            ApplyIdentity(journalEvent);
            ApplyLocation(journalEvent);

            if (!isBootstrapRead
                && enabled
                && ApplyTripEvent(journalEvent) is { } entry)
            {
                completed.Add(entry);
            }

            if (journalEvent.EventName == "Docked")
            {
                lastDocked = CreateDockedLocation(journalEvent);
            }
        }

        if (!enabled)
        {
            activeTrip = null;
        }

        if (completed.Count == 0)
        {
            return new DockToDockApplyResult(0, completed, null);
        }

        var written = 0;
        foreach (var entry in completed)
        {
            try
            {
                writer.Append(entry);
                written++;
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException)
            {
                return new DockToDockApplyResult(
                    written,
                    completed,
                    exception.Message);
            }
        }

        return new DockToDockApplyResult(written, completed, null);
    }

    private DockToDockLogEntry? ApplyTripEvent(JournalEventEnvelope journalEvent)
    {
        var root = journalEvent.Payload;
        switch (journalEvent.EventName)
        {
            case "Undocked":
                activeTrip = StartTrip(journalEvent);
                break;

            case "StartJump" when activeTrip is not null:
                activeTrip.EgressEndedAt ??= GetEventTime(journalEvent);
                break;

            case "FSDJump" when activeTrip is not null:
                activeTrip.Jumps++;
                activeTrip.Distance += GetDouble(root, "JumpDist") ?? 0;
                break;

            case "SupercruiseExit" when activeTrip is not null:
                activeTrip.IngressStartedAt = GetEventTime(journalEvent);
                break;

            case "Interdicted" when activeTrip is not null:
                activeTrip.WasInterdicted = true;
                break;

            case "Docked" when activeTrip is not null:
                var completed = CompleteTrip(activeTrip, journalEvent);
                activeTrip = null;
                return completed;
        }

        return null;
    }

    private DockToDockTrip StartTrip(JournalEventEnvelope journalEvent)
    {
        var root = journalEvent.Payload;
        var marketId = GetInt64(root, "MarketID") ?? -1;
        var docked = lastDocked?.MarketId == marketId ? lastDocked : null;
        var cargoCounts = (cargo?.Inventory ?? [])
            .Where(item => item.Count > 0)
            .ToDictionary(
                item => item.Name,
                item => item.Count,
                StringComparer.OrdinalIgnoreCase);
        return new DockToDockTrip(
            GetEventTime(journalEvent),
            new DockToDockStartLocation
            {
                SystemName = docked?.SystemName ?? systemName,
                SystemAddress = docked?.SystemAddress ?? systemAddress,
                BodyId = docked?.BodyId ?? bodyId,
                BodyName = docked?.BodyName ?? bodyName,
                DistanceFromStarLs = docked?.DistanceFromStarLs
                    ?? bodyDistanceLs,
                MarketId = marketId,
                StationName = GetString(root, "StationName")
                    ?? docked?.StationName
                    ?? "?",
                StationType = docked?.StationType ?? "?",
            },
            new DockToDockShipContext(
                shipType ?? "?",
                shipName ?? "?",
                shipMaximumJump,
                cargoCounts));
    }

    private DockToDockLogEntry CompleteTrip(
        DockToDockTrip trip,
        JournalEventEnvelope journalEvent)
    {
        var root = journalEvent.Payload;
        var endedAt = GetEventTime(journalEvent);
        return new DockToDockLogEntry
        {
            StartedAt = trip.StartedAt,
            EndedAt = endedAt,
            Duration = endedAt - trip.StartedAt,
            EgressDuration = trip.EgressEndedAt is { } egress
                ? egress - trip.StartedAt
                : TimeSpan.Zero,
            IngressDuration = trip.IngressStartedAt is { } ingress
                ? endedAt - ingress
                : TimeSpan.Zero,
            Jumps = trip.Jumps,
            Distance = trip.Distance,
            StartSystem = trip.StartSystem,
            StartAddress = trip.StartAddress,
            StartBodyId = trip.StartBodyId,
            StartBodyName = trip.StartBodyName,
            StartDistanceFromStarLs = trip.StartDistanceFromStarLs,
            StartMarketId = trip.StartMarketId,
            StartStationName = trip.StartStationName,
            StartStationType = trip.StartStationType,
            WasInterdicted = trip.WasInterdicted,
            EndSystem = GetString(root, "StarSystem") ?? systemName,
            EndAddress = GetInt64(root, "SystemAddress") ?? systemAddress,
            EndBodyId = bodyId,
            EndBodyName = bodyName,
            EndMarketId = GetInt64(root, "MarketID") ?? -1,
            EndStationName = GetString(root, "StationName") ?? "?",
            EndStationType = GetString(root, "StationType") ?? "?",
            EndDistanceFromStarLs = GetDouble(root, "DistFromStarLS")
                ?? bodyDistanceLs,
            ShipType = trip.ShipType,
            ShipName = trip.ShipName,
            ShipMaximumJump = trip.ShipMaximumJump,
            Cargo = trip.Cargo,
        };
    }

    private void ApplyIdentity(JournalEventEnvelope journalEvent)
    {
        var root = journalEvent.Payload;
        switch (journalEvent.EventName)
        {
            case "LoadGame":
            case "Loadout":
                shipType = GetString(root, "Ship") ?? shipType;
                shipName = GetString(root, "ShipName")
                    ?? GetString(root, "ShipIdent")
                    ?? shipName;
                shipMaximumJump = GetDouble(root, "MaxJumpRange")
                    ?? shipMaximumJump;
                break;

            case "ShipyardSwap":
                shipType = GetString(root, "ShipType") ?? shipType;
                shipName = GetString(root, "ShipName")
                    ?? shipName;
                break;
        }
    }

    private void ApplyLocation(JournalEventEnvelope journalEvent)
    {
        var root = journalEvent.Payload;
        switch (journalEvent.EventName)
        {
            case "Location":
            case "FSDJump":
            case "CarrierJump":
            case "SupercruiseExit":
                systemName = GetString(root, "StarSystem") ?? systemName;
                systemAddress = GetInt64(root, "SystemAddress") ?? systemAddress;
                if (GetString(root, "BodyType") == "Planet"
                    || journalEvent.EventName == "SupercruiseExit")
                {
                    bodyId = GetInt32(root, "BodyID") ?? bodyId;
                    bodyName = GetString(root, "Body") ?? bodyName;
                }
                else if (journalEvent.EventName is "FSDJump" or "CarrierJump")
                {
                    bodyId = null;
                    bodyName = null;
                    bodyDistanceLs = null;
                }

                break;

            case "ApproachBody":
                bodyId = GetInt32(root, "BodyID") ?? bodyId;
                bodyName = GetString(root, "Body") ?? bodyName;
                break;

            case "Scan":
                if (GetInt32(root, "BodyID") == bodyId)
                {
                    bodyDistanceLs = GetDouble(root, "DistanceFromArrivalLS")
                        ?? bodyDistanceLs;
                }

                break;
        }
    }

    private DockedLocation CreateDockedLocation(JournalEventEnvelope journalEvent)
    {
        var root = journalEvent.Payload;
        return new DockedLocation
        {
            SystemName = GetString(root, "StarSystem") ?? systemName,
            SystemAddress = GetInt64(root, "SystemAddress") ?? systemAddress,
            BodyId = bodyId,
            BodyName = bodyName,
            DistanceFromStarLs = GetDouble(root, "DistFromStarLS")
                ?? bodyDistanceLs,
            MarketId = GetInt64(root, "MarketID") ?? -1,
            StationName = GetString(root, "StationName") ?? "?",
            StationType = GetString(root, "StationType") ?? "?",
        };
    }

    private DateTimeOffset GetEventTime(JournalEventEnvelope journalEvent)
    {
        return (journalEvent.Timestamp ?? timeProvider.GetUtcNow()).ToLocalTime();
    }

    private static string? GetString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static long? GetInt64(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var result)
                ? result
                : null;
    }

    private static int? GetInt32(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var result)
                ? result
                : null;
    }

    private static double? GetDouble(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var result)
            && double.IsFinite(result)
                ? result
                : null;
    }

    private sealed class DockedLocation
    {
        public string? SystemName { get; init; }

        public long? SystemAddress { get; init; }

        public int? BodyId { get; init; }

        public string? BodyName { get; init; }

        public double? DistanceFromStarLs { get; init; }

        public long MarketId { get; init; }

        public required string StationName { get; init; }

        public required string StationType { get; init; }
    }

    private sealed class DockToDockStartLocation
    {
        public string? SystemName { get; init; }

        public long? SystemAddress { get; init; }

        public int? BodyId { get; init; }

        public string? BodyName { get; init; }

        public double? DistanceFromStarLs { get; init; }

        public long MarketId { get; init; }

        public required string StationName { get; init; }

        public required string StationType { get; init; }
    }

    private sealed record DockToDockShipContext(
        string ShipType,
        string ShipName,
        double? ShipMaximumJump,
        IReadOnlyDictionary<string, int> Cargo);

    private sealed class DockToDockTrip(
        DateTimeOffset startedAt,
        DockToDockStartLocation start,
        DockToDockShipContext ship)
    {
        public DateTimeOffset StartedAt { get; } = startedAt;
        public string? StartSystem { get; } = start.SystemName;
        public long? StartAddress { get; } = start.SystemAddress;
        public int? StartBodyId { get; } = start.BodyId;
        public string? StartBodyName { get; } = start.BodyName;
        public double? StartDistanceFromStarLs { get; } = start.DistanceFromStarLs;
        public long StartMarketId { get; } = start.MarketId;
        public string StartStationName { get; } = start.StationName;
        public string StartStationType { get; } = start.StationType;
        public string ShipType { get; } = ship.ShipType;
        public string ShipName { get; } = ship.ShipName;
        public double? ShipMaximumJump { get; } = ship.ShipMaximumJump;
        public IReadOnlyDictionary<string, int> Cargo { get; } = ship.Cargo;
        public DateTimeOffset? EgressEndedAt { get; set; }
        public DateTimeOffset? IngressStartedAt { get; set; }
        public int Jumps { get; set; }
        public double Distance { get; set; }
        public bool WasInterdicted { get; set; }
    }
}

public sealed class DockToDockCsvWriter
{
    private const string HighPrecisionNumberFormat = "0.################";

    public const string FileName = "SrvSurvey-dock-to-dock-times.csv";

    private static readonly string[] Columns =
    [
        "startDate", "startTime", "endDate", "endTime", "duration",
        "durationEgress", "durationIngress", "jumps", "distance",
        "startSystem", "startAddress", "startBodyNum", "startBodyName",
        "startDistFromStartLs", "startMarketId", "startStationName",
        "startStationType", "interdicted", "endSystem", "endAddress",
        "endBodyNum", "endBodyName", "endMarketId", "endStationName",
        "endStationType", "endDistFromStartLs", "shipType", "shipName",
        "shipMaxJump", "cargo",
    ];

    public DockToDockCsvWriter(string outputPath)
    {
        OutputPath = Path.GetFullPath(
            string.IsNullOrWhiteSpace(outputPath)
                ? throw new ArgumentException(
                    "A dock-to-dock CSV path is required.",
                    nameof(outputPath))
                : outputPath);
    }

    public string OutputPath { get; }

    public static string GetDefaultPath()
    {
        var documents = Environment.GetFolderPath(
            Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
        {
            documents = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
        }

        return Path.Combine(documents, FileName);
    }

    public void Append(DockToDockLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var directory = Path.GetDirectoryName(OutputPath)
            ?? throw new InvalidOperationException(
                "The dock-to-dock CSV path has no directory.");
        Directory.CreateDirectory(directory);
        ValidateExistingFile();
        var includeHeader = !File.Exists(OutputPath)
            || new FileInfo(OutputPath).Length == 0;
        var text = (includeHeader
                ? string.Join(',', Columns) + "\r\n"
                : string.Empty)
            + string.Join(',', CreateValues(entry).Select(Escape))
            + "\r\n";
        var bytes = new UTF8Encoding(false).GetBytes(text);
        using var stream = new FileStream(
            OutputPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read);
        stream.Write(bytes);
        stream.Flush(true);
    }

    private void ValidateExistingFile()
    {
        if (!File.Exists(OutputPath)
            || new FileInfo(OutputPath).Length == 0)
        {
            return;
        }

        using var stream = new FileStream(
            OutputPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: true);
        var header = reader.ReadLine();
        if (!string.Equals(
                header,
                string.Join(',', Columns),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The existing dock-to-dock CSV header is not compatible; it was left unchanged.");
        }

        stream.Seek(-1, SeekOrigin.End);
        if (stream.ReadByte() is not ('\n' or '\r'))
        {
            throw new InvalidDataException(
                "The existing dock-to-dock CSV ends with an incomplete row; it was left unchanged.");
        }
    }

    private static IReadOnlyList<string> CreateValues(DockToDockLogEntry entry)
    {
        return
        [
            entry.StartedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            entry.StartedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            entry.EndedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            entry.EndedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            FormatDuration(entry.Duration),
            FormatDuration(entry.EgressDuration),
            FormatDuration(entry.IngressDuration),
            entry.Jumps.ToString(CultureInfo.InvariantCulture),
            entry.Distance.ToString(HighPrecisionNumberFormat, CultureInfo.InvariantCulture),
            entry.StartSystem ?? "?",
            entry.StartAddress?.ToString(CultureInfo.InvariantCulture) ?? "-1",
            entry.StartBodyId?.ToString(CultureInfo.InvariantCulture) ?? "-1",
            entry.StartBodyName ?? "?",
            entry.StartDistanceFromStarLs?.ToString(
                HighPrecisionNumberFormat,
                CultureInfo.InvariantCulture) ?? "-1",
            entry.StartMarketId.ToString(CultureInfo.InvariantCulture),
            entry.StartStationName,
            entry.StartStationType,
            entry.WasInterdicted.ToString(),
            entry.EndSystem ?? "?",
            entry.EndAddress?.ToString(CultureInfo.InvariantCulture) ?? "-1",
            entry.EndBodyId?.ToString(CultureInfo.InvariantCulture) ?? "-1",
            entry.EndBodyName ?? "?",
            entry.EndMarketId.ToString(CultureInfo.InvariantCulture),
            entry.EndStationName,
            entry.EndStationType,
            entry.EndDistanceFromStarLs?.ToString(
                HighPrecisionNumberFormat,
                CultureInfo.InvariantCulture) ?? "-1",
            entry.ShipType,
            entry.ShipName,
            entry.ShipMaximumJump?.ToString(
                HighPrecisionNumberFormat,
                CultureInfo.InvariantCulture) ?? "-1",
            JsonSerializer.Serialize(entry.Cargo),
        ];
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var safe = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        return safe.ToString("hh\\:mm\\:ss", CultureInfo.InvariantCulture);
    }

    private static string Escape(string value)
    {
        return value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : '"' + value.Replace("\"", "\"\"") + '"';
    }
}

public sealed class DockToDockLogEntry
{
    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset EndedAt { get; init; }

    public TimeSpan Duration { get; init; }

    public TimeSpan EgressDuration { get; init; }

    public TimeSpan IngressDuration { get; init; }

    public int Jumps { get; init; }

    public double Distance { get; init; }

    public string? StartSystem { get; init; }

    public long? StartAddress { get; init; }

    public int? StartBodyId { get; init; }

    public string? StartBodyName { get; init; }

    public double? StartDistanceFromStarLs { get; init; }

    public long StartMarketId { get; init; }

    public required string StartStationName { get; init; }

    public required string StartStationType { get; init; }

    public bool WasInterdicted { get; init; }

    public string? EndSystem { get; init; }

    public long? EndAddress { get; init; }

    public int? EndBodyId { get; init; }

    public string? EndBodyName { get; init; }

    public long EndMarketId { get; init; }

    public required string EndStationName { get; init; }

    public required string EndStationType { get; init; }

    public double? EndDistanceFromStarLs { get; init; }

    public required string ShipType { get; init; }

    public required string ShipName { get; init; }

    public double? ShipMaximumJump { get; init; }

    public required IReadOnlyDictionary<string, int> Cargo { get; init; }
}

public sealed record DockToDockApplyResult(
    int WrittenCount,
    IReadOnlyList<DockToDockLogEntry> Entries,
    string? Error)
{
    public bool Written => WrittenCount > 0;
}
