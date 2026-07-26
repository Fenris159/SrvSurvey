using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Exobiology;

public sealed record RegionalCodexCandidate(
    int RegionId,
    string RegionName,
    long EntryId,
    string Variant);

public sealed class RegionalCodexCandidateCatalog
{
    public const string LegacyFileName = "codexNotFound.json";

    private const long MaximumFileBytes = 16L * 1024 * 1024;
    private const int MaximumEntries = 100_000;
    private readonly IReadOnlyDictionary<int, IReadOnlySet<long>> entryIdsByRegion;

    private RegionalCodexCandidateCatalog(
        IReadOnlyList<RegionalCodexCandidate> entries,
        string? sourcePath,
        IReadOnlyList<string> warnings)
    {
        Entries = entries;
        SourcePath = sourcePath;
        Warnings = warnings;
        entryIdsByRegion = entries
            .GroupBy(entry => entry.RegionId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlySet<long>)group
                    .Select(entry => entry.EntryId)
                    .ToHashSet());
    }

    public static RegionalCodexCandidateCatalog Empty { get; } = new(
        [],
        null,
        []);

    public IReadOnlyList<RegionalCodexCandidate> Entries { get; }

    public string? SourcePath { get; }

    public IReadOnlyList<string> Warnings { get; }

    public int Count => Entries.Count;

    public bool HasData => Count > 0;

    public bool IsCandidate(int? regionId, long entryId)
    {
        return regionId is > 0
            && entryId > 0
            && entryIdsByRegion.TryGetValue(regionId.Value, out var entries)
            && entries.Contains(entryId);
    }

    public static RegionalCodexCandidateCatalog Load(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var path = Path.Combine(
            Path.GetFullPath(dataDirectory),
            LegacyFileName);
        if (!File.Exists(path))
        {
            return Empty;
        }

        try
        {
            var info = new FileInfo(path);
            if (info.Length is <= 0 or > MaximumFileBytes)
            {
                throw new InvalidDataException(
                    $"The regional Codex candidate catalog size is invalid: {info.Length:N0} bytes.");
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return Load(stream, path);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidDataException
            or FormatException
            or OverflowException)
        {
            return new RegionalCodexCandidateCatalog(
                [],
                path,
                [$"Imported {LegacyFileName} was preserved but ignored safely: {exception.Message}"]);
        }
    }

    public static RegionalCodexCandidateCatalog FromEntries(
        IEnumerable<RegionalCodexCandidate> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return Create(entries, null);
    }

    internal static RegionalCodexCandidateCatalog Load(
        Stream stream,
        string? sourcePath)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "The regional Codex candidate catalog is not a JSON object.");
        }

        var regionsByName = GalacticRegionMap.Regions.ToDictionary(
            region => region.Name,
            StringComparer.OrdinalIgnoreCase);
        var candidates = new List<RegionalCodexCandidate>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!regionsByName.TryGetValue(property.Name, out var region))
            {
                throw new InvalidDataException(
                    $"The regional Codex candidate catalog contains an unknown region: {property.Name}.");
            }

            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    $"The regional Codex candidates for {property.Name} are not an array.");
            }

            foreach (var item in property.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String
                    || !TryParseLegacyEntry(
                        item.GetString(),
                        out var entryId,
                        out var variant))
                {
                    throw new InvalidDataException(
                        $"The regional Codex candidates for {property.Name} contain an invalid entry.");
                }

                candidates.Add(new RegionalCodexCandidate(
                    region.Id,
                    region.Name,
                    entryId,
                    variant));
                if (candidates.Count > MaximumEntries)
                {
                    throw new InvalidDataException(
                        "The regional Codex candidate catalog contains too many entries.");
                }
            }
        }

        return Create(candidates, sourcePath);
    }

    internal string SerializeLegacy()
    {
        var payload = Entries
            .GroupBy(entry => new { entry.RegionId, entry.RegionName })
            .OrderBy(group => group.Key.RegionId)
            .ToDictionary(
                group => group.Key.RegionName,
                group => group
                    .OrderBy(entry => entry.EntryId)
                    .ThenBy(entry => entry.Variant, StringComparer.Ordinal)
                    .Select(entry => entry.EntryId.ToString(
                            CultureInfo.InvariantCulture)
                        + "_"
                        + entry.Variant)
                    .ToArray(),
                StringComparer.Ordinal);
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }

    private static RegionalCodexCandidateCatalog Create(
        IEnumerable<RegionalCodexCandidate> entries,
        string? sourcePath)
    {
        var regionsById = GalacticRegionMap.Regions.ToDictionary(
            region => region.Id);
        var normalized = new List<RegionalCodexCandidate>();
        foreach (var entry in entries)
        {
            if (!regionsById.TryGetValue(entry.RegionId, out var region)
                || entry.EntryId <= 0
                || string.IsNullOrWhiteSpace(entry.Variant))
            {
                throw new InvalidDataException(
                    "A regional Codex candidate has invalid region, entry, or variant data.");
            }

            normalized.Add(new RegionalCodexCandidate(
                region.Id,
                region.Name,
                entry.EntryId,
                entry.Variant.Trim()));
        }

        var distinct = normalized
            .DistinctBy(entry => (entry.RegionId, entry.EntryId))
            .OrderBy(entry => entry.RegionId)
            .ThenBy(entry => entry.EntryId)
            .ToArray();
        if (distinct.Length > MaximumEntries)
        {
            throw new InvalidDataException(
                "The regional Codex candidate catalog contains too many entries.");
        }

        return new RegionalCodexCandidateCatalog(distinct, sourcePath, []);
    }

    private static bool TryParseLegacyEntry(
        string? value,
        out long entryId,
        out string variant)
    {
        entryId = 0;
        variant = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separator = value.IndexOf('_');
        if (separator <= 0
            || separator == value.Length - 1
            || !long.TryParse(
                value.AsSpan(0, separator),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out entryId)
            || entryId <= 0)
        {
            return false;
        }

        variant = value[(separator + 1)..].Trim();
        return variant.Length > 0;
    }
}
