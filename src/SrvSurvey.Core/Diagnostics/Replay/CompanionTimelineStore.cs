using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Diagnostics.Replay;

public enum ReplayInputKind
{
    Journal,
    Status,
    Cargo,
    ShipLocker,
    NavRoute,
    Market,
}

public sealed record CompanionTimelineEntry(
    DateTimeOffset Timestamp,
    ReplayInputKind Kind,
    string RawJson)
{
    public string FileName => Kind switch
    {
        ReplayInputKind.Status => StatusFileReader.FileName,
        ReplayInputKind.Cargo => CargoFileReader.FileName,
        ReplayInputKind.ShipLocker => ShipLockerFileReader.FileName,
        ReplayInputKind.NavRoute => NavRouteFileReader.FileName,
        ReplayInputKind.Market => MarketFileReader.FileName,
        _ => throw new InvalidOperationException(
            $"{Kind} is not a companion-file input."),
    };
}

public sealed class CompanionTimelineStore : IDisposable
{
    public const string DirectoryName = "diagnostic-companion-history";
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);
    private readonly string directory;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<ReplayInputKind, string> lastPayloadHashes = [];
    private DateTimeOffset lastCleanup = DateTimeOffset.MinValue;
    private bool disposed;

    public CompanionTimelineStore(
        string directory,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = Path.GetFullPath(directory);
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public static TimeSpan Retention { get; } = TimeSpan.FromHours(24);

    public string DirectoryPath => directory;

    public static string ResolveDirectory(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        return Path.Combine(Path.GetFullPath(dataDirectory), DirectoryName);
    }

    public async Task AppendAsync(
        JournalMonitorUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ObjectDisposedException.ThrowIf(disposed, this);
        var observedAt = timeProvider.GetUtcNow();
        var entries = CompanionTimelineCodec.Encode(update, observedAt)
            .OrderBy(entry => entry.Timestamp)
            .ThenBy(entry => entry.Kind)
            .ToArray();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(directory);
            var cutoff = observedAt - Retention;
            foreach (var entry in entries)
            {
                if (entry.Timestamp < cutoff || IsDuplicate(entry))
                {
                    continue;
                }

                var path = ResolveSegmentPath(entry.Timestamp);
                var line = CompanionTimelineCodec.SerializeEntry(entry);
                await using var stream = new FileStream(
                    path,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 16 * 1024,
                    useAsync: true);
                var bytes = Encoding.UTF8.GetBytes(line + "\n");
                await stream.WriteAsync(bytes, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (observedAt - lastCleanup >= CleanupInterval)
            {
                await PruneAsync(cutoff, cancellationToken).ConfigureAwait(false);
                lastCleanup = observedAt;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            var now = timeProvider.GetUtcNow();
            await PruneAsync(now - Retention, cancellationToken)
                .ConfigureAwait(false);
            lastCleanup = now;
        }
        finally
        {
            gate.Release();
        }
    }

    private bool IsDuplicate(CompanionTimelineEntry entry)
    {
        var hash = CompanionTimelineCodec.ComputePayloadStateHash(entry.RawJson);
        if (lastPayloadHashes.TryGetValue(entry.Kind, out var previous)
            && string.Equals(previous, hash, StringComparison.Ordinal))
        {
            return true;
        }

        lastPayloadHashes[entry.Kind] = hash;
        return false;
    }

    private string ResolveSegmentPath(DateTimeOffset timestamp)
    {
        var name = timestamp.UtcDateTime.ToString(
            "yyyyMMddHH'.jsonl'",
            CultureInfo.InvariantCulture);
        return Path.Combine(directory, name);
    }

    private async Task PruneAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        foreach (var path in Directory.EnumerateFiles(
                     directory,
                     "*.jsonl",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileNameWithoutExtension(path);
            if (!DateTimeOffset.TryParseExact(
                    name,
                    "yyyyMMddHH",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal
                        | DateTimeStyles.AdjustToUniversal,
                    out var segmentStart))
            {
                continue;
            }

            if (segmentStart.AddHours(1) <= cutoff)
            {
                File.Delete(path);
                continue;
            }

            if (segmentStart <= cutoff && cutoff < segmentStart.AddHours(1))
            {
                await RewriteBoundarySegmentAsync(
                    path,
                    cutoff,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task RewriteBoundarySegmentAsync(
        string path,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                useAsync: true);
            await foreach (var entry in StreamFileAsync(path, cancellationToken))
            {
                if (entry.Timestamp < cutoff)
                {
                    continue;
                }

                var bytes = Encoding.UTF8.GetBytes(
                    CompanionTimelineCodec.SerializeEntry(entry) + "\n");
                await output.WriteAsync(bytes, cancellationToken)
                    .ConfigureAwait(false);
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public static async IAsyncEnumerable<CompanionTimelineEntry> StreamAsync(
        string directory,
        DateTimeOffset? from,
        DateTimeOffset? to,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var fullDirectory = Path.GetFullPath(directory);
        if (!Directory.Exists(fullDirectory))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(
                     fullDirectory,
                     "*.jsonl",
                     SearchOption.TopDirectoryOnly)
                 .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal))
        {
            await foreach (var entry in StreamFileAsync(path, cancellationToken))
            {
                if ((from is null || entry.Timestamp >= from)
                    && (to is null || entry.Timestamp <= to))
                {
                    yield return entry;
                }
            }
        }
    }

    internal static async IAsyncEnumerable<CompanionTimelineEntry> StreamFileAsync(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var line = await reader.ReadLineAsync(cancellationToken)
            .ConfigureAwait(false);
        while (line is not null)
        {
            var nextLine = await reader.ReadLineAsync(cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                line = nextLine;
                continue;
            }

            CompanionTimelineEntry entry;
            try
            {
                entry = CompanionTimelineCodec.DeserializeEntry(line);
            }
            catch (JsonException) when (nextLine is null)
            {
                yield break;
            }

            yield return entry;
            line = nextLine;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        gate.Dispose();
    }
}

internal static class CompanionTimelineCodec
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new JournalTimestampConverter(),
        },
    };
    private static readonly JsonSerializerOptions PayloadJson = new()
    {
        Converters = { new JournalTimestampConverter() },
    };

    public static IReadOnlyList<CompanionTimelineEntry> Encode(
        JournalMonitorUpdate update,
        DateTimeOffset observedAt)
    {
        var entries = new List<CompanionTimelineEntry>(5);
        Add(entries, ReplayInputKind.Status, update.Status, observedAt);
        Add(entries, ReplayInputKind.Cargo, update.Cargo, observedAt);
        Add(entries, ReplayInputKind.ShipLocker, update.ShipLocker, observedAt);
        Add(entries, ReplayInputKind.NavRoute, update.NavRoute, observedAt);
        Add(entries, ReplayInputKind.Market, update.Market, observedAt);
        return entries;
    }

    private static void Add<T>(
        ICollection<CompanionTimelineEntry> entries,
        ReplayInputKind kind,
        T? snapshot,
        DateTimeOffset observedAt)
        where T : class
    {
        if (snapshot is null)
        {
            return;
        }

        var timestamp = GetTimestamp(snapshot);
        if (timestamp == default)
        {
            timestamp = observedAt;
        }

        entries.Add(new CompanionTimelineEntry(
            timestamp.ToUniversalTime(),
            kind,
            EncodePayload(kind, snapshot, timestamp)));
    }

    private static DateTimeOffset GetTimestamp(object snapshot) => snapshot switch
    {
        EliteStatus status => status.Timestamp,
        CargoSnapshot cargo => cargo.Timestamp,
        ShipLockerSnapshot locker => locker.Timestamp,
        NavRouteSnapshot route => route.Timestamp,
        MarketSnapshot market => market.Timestamp,
        _ => default,
    };

    private static string EncodePayload<T>(
        ReplayInputKind kind,
        T snapshot,
        DateTimeOffset timestamp)
        where T : class
    {
        JsonObject payload = kind switch
        {
            ReplayInputKind.Status => JsonSerializer.SerializeToNode(
                    ((EliteStatus)(object)snapshot) with { Timestamp = timestamp },
                    PayloadJson)!.AsObject(),
            ReplayInputKind.Cargo => EncodeCargo((CargoSnapshot)(object)snapshot, timestamp),
            ReplayInputKind.ShipLocker => EncodeShipLocker(
                (ShipLockerSnapshot)(object)snapshot,
                timestamp),
            ReplayInputKind.NavRoute => EncodeNavRoute(
                (NavRouteSnapshot)(object)snapshot,
                timestamp),
            ReplayInputKind.Market => EncodeMarket(
                (MarketSnapshot)(object)snapshot,
                timestamp),
            _ => throw new InvalidOperationException(
                $"{kind} is not a companion-file input."),
        };
        return payload.ToJsonString(Json);
    }

    public static string SerializeEntry(CompanionTimelineEntry entry)
    {
        var wrapper = new JsonObject
        {
            ["timestamp"] = JsonSerializer.SerializeToNode(entry.Timestamp, Json),
            ["kind"] = JsonSerializer.SerializeToNode(entry.Kind, Json),
            ["payload"] = JsonNode.Parse(entry.RawJson),
        };
        return wrapper.ToJsonString(Json);
    }

    public static CompanionTimelineEntry DeserializeEntry(string line)
    {
        using var document = JsonDocument.Parse(line, new JsonDocumentOptions
        {
            MaxDepth = 64,
        });
        var root = document.RootElement;
        var timestamp = root.GetProperty("timestamp")
            .Deserialize<DateTimeOffset>(Json);
        var kind = root.GetProperty("kind").Deserialize<ReplayInputKind>(Json);
        if (kind == ReplayInputKind.Journal)
        {
            throw new JsonException("A companion timeline cannot contain journal inputs.");
        }

        var payload = root.GetProperty("payload");
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A companion timeline payload must be an object.");
        }

        return new CompanionTimelineEntry(timestamp, kind, payload.GetRawText());
    }

    public static string ComputePayloadStateHash(string rawJson)
    {
        var root = JsonNode.Parse(rawJson)?.AsObject()
            ?? throw new JsonException("A companion payload must be an object.");
        _ = root.Remove("timestamp");
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(root.ToJsonString(Json))));
    }

    public static CompanionTimelineEntry Redact(CompanionTimelineEntry entry)
    {
        var payload = JsonNode.Parse(entry.RawJson)?.AsObject()
            ?? throw new JsonException("A companion payload must be an object.");
        switch (entry.Kind)
        {
            case ReplayInputKind.Status:
                Zero(payload, "Latitude");
                Zero(payload, "Longitude");
                Replace(payload, "BodyName", "Replay Body");
                if (payload["Destination"] is JsonObject destination)
                {
                    Zero(destination, "System");
                    Zero(destination, "Body");
                    Replace(destination, "Name", "Replay Destination");
                    Replace(
                        destination,
                        "Name_Localised",
                        "Replay Destination");
                }
                break;
            case ReplayInputKind.NavRoute:
                if (payload["Route"] is JsonArray route)
                {
                    var index = 0;
                    foreach (var item in route.OfType<JsonObject>())
                    {
                        index++;
                        Replace(item, "StarSystem", $"Replay Route {index:000}");
                        item["SystemAddress"] = 9_100_000_000_000_000L + index;
                        if (item["StarPos"] is JsonArray position)
                        {
                            item["StarPos"] = new JsonArray(
                                position.Select(_ => (JsonNode?)JsonValue.Create(0d))
                                    .ToArray());
                        }
                    }
                }
                break;
            case ReplayInputKind.Market:
                Replace(payload, "StationName", "Replay Station");
                Replace(payload, "StarSystem", "Replay System");
                payload["MarketId"] = 9_200_000_000_000_000L;
                break;
            case ReplayInputKind.Cargo:
            case ReplayInputKind.ShipLocker:
                break;
            default:
                throw new InvalidOperationException(
                    $"{entry.Kind} is not a companion-file input.");
        }

        return entry with { RawJson = payload.ToJsonString(Json) };
    }

    private static void Zero(JsonObject payload, string propertyName)
    {
        if (payload.ContainsKey(propertyName))
        {
            payload[propertyName] = 0;
        }
    }

    private static void Replace(
        JsonObject payload,
        string propertyName,
        string replacement)
    {
        if (payload[propertyName] is not null)
        {
            payload[propertyName] = replacement;
        }
    }

    private static JsonObject EncodeCargo(
        CargoSnapshot snapshot,
        DateTimeOffset timestamp) => new()
        {
            ["timestamp"] = JsonSerializer.SerializeToNode(timestamp, Json),
            ["event"] = snapshot.EventName,
            ["Vessel"] = snapshot.Vessel,
            ["Inventory"] = new JsonArray(snapshot.Inventory.Select(item =>
                (JsonNode)new JsonObject
                {
                    ["Name"] = item.Name,
                    ["Name_Localised"] = item.LocalizedName,
                    ["Count"] = item.Count,
                    ["Stolen"] = item.Stolen,
                }).ToArray()),
        };

    private static JsonObject EncodeShipLocker(
        ShipLockerSnapshot snapshot,
        DateTimeOffset timestamp)
    {
        var payload = new JsonObject
        {
            ["timestamp"] = JsonSerializer.SerializeToNode(timestamp, Json),
            ["event"] = snapshot.EventName,
        };
        foreach (var category in new[]
                 {
                     "Items", "Components", "Consumables", "Data",
                 })
        {
            payload[category] = new JsonArray(snapshot.Items
                .Where(item => string.Equals(
                    item.Category,
                    category,
                    StringComparison.OrdinalIgnoreCase))
                .Select(item => (JsonNode)new JsonObject
                {
                    ["Name"] = item.Name,
                    ["Name_Localised"] = item.LocalizedName,
                    ["Count"] = item.Count,
                }).ToArray());
        }

        return payload;
    }

    private static JsonObject EncodeNavRoute(
        NavRouteSnapshot snapshot,
        DateTimeOffset timestamp) => new()
        {
            ["timestamp"] = JsonSerializer.SerializeToNode(timestamp, Json),
            ["event"] = snapshot.EventName,
            ["Route"] = new JsonArray(snapshot.Route.Select(item =>
                (JsonNode)new JsonObject
                {
                    ["StarSystem"] = item.StarSystem,
                    ["SystemAddress"] = item.SystemAddress,
                    ["StarPos"] = item.Position is not { } position
                        ? null
                        : new JsonArray(
                            position.X,
                            position.Y,
                            position.Z),
                    ["StarClass"] = item.StarClass,
                }).ToArray()),
        };

    private static JsonObject EncodeMarket(
        MarketSnapshot snapshot,
        DateTimeOffset timestamp) => new()
        {
            ["timestamp"] = JsonSerializer.SerializeToNode(timestamp, Json),
            ["event"] = snapshot.EventName,
            ["MarketId"] = snapshot.MarketId,
            ["StationName"] = snapshot.StationName,
            ["StationType"] = snapshot.StationType,
            ["CarrierDockingAccess"] = snapshot.CarrierDockingAccess,
            ["StarSystem"] = snapshot.StarSystem,
            ["Items"] = new JsonArray(snapshot.Items.Select(item =>
                (JsonNode)new JsonObject
                {
                    ["id"] = item.Id,
                    ["Name"] = item.Name,
                    ["Name_Localised"] = item.LocalizedName,
                    ["Category"] = item.Category,
                    ["Category_Localised"] = item.LocalizedCategory,
                    ["BuyPrice"] = item.BuyPrice,
                    ["SellPrice"] = item.SellPrice,
                    ["MeanPrice"] = item.MeanPrice,
                    ["StockBracket"] = item.StockBracket,
                    ["DemandBracket"] = item.DemandBracket,
                    ["Stock"] = item.Stock,
                    ["Demand"] = item.Demand,
                    ["Producer"] = item.Producer,
                    ["Consumer"] = item.Consumer,
                    ["Rare"] = item.Rare,
                }).ToArray()),
        };

    private sealed class JournalTimestampConverter : JsonConverter<DateTimeOffset>
    {
        private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

        public override DateTimeOffset Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            return DateTimeOffset.Parse(
                reader.GetString()
                    ?? throw new JsonException("A replay timestamp is empty."),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }

        public override void Write(
            Utf8JsonWriter writer,
            DateTimeOffset value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.UtcDateTime.ToString(
                Format,
                CultureInfo.InvariantCulture));
        }
    }
}
