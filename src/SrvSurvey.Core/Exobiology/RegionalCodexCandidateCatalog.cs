using System.Globalization;
using System.Text;
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
    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
    };

    public const string LegacyFileName = "codexNotFound.json";

    private const long MaximumFileBytes = 16L * 1024 * 1024;
    private const int MaximumEntries = 100_000;
    private const int MaximumCsvColumns = 64;
    private const int MaximumCsvFieldCharacters = 1_048_576;
    private static readonly string[] PublishedCsvColumns =
    [
        "RegionID",
        "RegionName",
        "EnglishName",
        "Found",
        "NotExpectedToBeFound",
        "EntryID",
        "Name",
        "Varient",
    ];
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
        return JsonSerializer.Serialize(payload, IndentedJson);
    }

    internal static RegionalCodexCandidateCatalog ParsePublishedCsv(
        byte[] bytes,
        ExobiologyReferenceCatalog references)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(references);
        if (bytes.Length == 0)
        {
            throw new InvalidDataException(
                "The published regional Codex candidate CSV is empty.");
        }

        string text;
        try
        {
            text = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "The published regional Codex candidate CSV is not valid UTF-8.",
                exception);
        }

        var rows = ParseCsvRows(text);
        if (rows.Count < 2)
        {
            throw new InvalidDataException(
                "The published regional Codex candidate CSV contains no data rows.");
        }

        var header = rows[0];
        if (header.Count < PublishedCsvColumns.Length
            || !PublishedCsvColumns.Select((column, index) => string.Equals(
                    header[index].TrimStart('\uFEFF'),
                    column,
                    StringComparison.Ordinal))
                .All(matches => matches))
        {
            throw new InvalidDataException(
                "The published regional Codex candidate CSV header is incompatible.");
        }

        var regionsById = GalacticRegionMap.Regions.ToDictionary(
            region => region.Id);
        var candidates = new List<RegionalCodexCandidate>();
        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (row.Count < PublishedCsvColumns.Length)
            {
                throw new InvalidDataException(
                    $"Published regional Codex row {rowIndex + 1:N0} has too few columns.");
            }

            if (!int.TryParse(
                    row[0],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var regionId)
                || !regionsById.TryGetValue(regionId, out var region))
            {
                throw new InvalidDataException(
                    $"Published regional Codex row {rowIndex + 1:N0} has an unknown region.");
            }

            var found = ParseCsvBoolean(row[3], rowIndex, "Found");
            var notExpected = ParseCsvBoolean(
                row[4],
                rowIndex,
                "NotExpectedToBeFound");
            ExobiologyReference? reference = null;
            long entryId;
            if (string.IsNullOrWhiteSpace(row[5]))
            {
                reference = references.FindByDisplayName(row[2].Trim());
                if (reference is null)
                {
                    continue;
                }

                entryId = reference.EntryId;
            }
            else if (!long.TryParse(
                         row[5],
                         NumberStyles.Integer,
                         CultureInfo.InvariantCulture,
                         out entryId)
                     || entryId <= 0)
            {
                throw new InvalidDataException(
                    $"Published regional Codex row {rowIndex + 1:N0} has an invalid entry ID.");
            }

            if (found || notExpected)
            {
                continue;
            }

            reference ??= references.FindByEntryId(entryId);
            var variant = row[7].Trim();
            if (variant.Length == 0)
            {
                variant = reference?.VariantName
                    ?? row[6].Trim();
            }

            if (variant.Length == 0)
            {
                throw new InvalidDataException(
                    $"Published regional Codex row {rowIndex + 1:N0} has no variant.");
            }

            candidates.Add(new RegionalCodexCandidate(
                region.Id,
                region.Name,
                entryId,
                variant));
            if (candidates.Count > MaximumEntries)
            {
                throw new InvalidDataException(
                    "The published regional Codex candidate CSV contains too many candidates.");
            }
        }

        if (candidates.Count == 0)
        {
            throw new InvalidDataException(
                "The published regional Codex candidate CSV contains no candidates.");
        }

        return Create(candidates, null);
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

    private static IReadOnlyList<IReadOnlyList<string>> ParseCsvRows(string text)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var closedQuote = false;

        void AddField()
        {
            row.Add(field.ToString());
            if (row.Count > MaximumCsvColumns)
            {
                throw new InvalidDataException(
                    "The published regional Codex candidate CSV has too many columns.");
            }

            field.Clear();
            closedQuote = false;
        }

        void AddRow()
        {
            AddField();
            if (row.Any(value => value.Length > 0))
            {
                rows.Add(row.ToArray());
                if (rows.Count > MaximumEntries + 1)
                {
                    throw new InvalidDataException(
                        "The published regional Codex candidate CSV has too many rows.");
                }
            }

            row.Clear();
        }

        var index = 0;
        while (index < text.Length)
        {
            var character = text[index];
            if (inQuotes)
            {
                if (character == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                        closedQuote = true;
                    }
                }
                else
                {
                    field.Append(character);
                }
            }
            else if (character == '"')
            {
                if (field.Length > 0 || closedQuote)
                {
                    throw new InvalidDataException(
                        "The published regional Codex candidate CSV contains an invalid quote.");
                }

                inQuotes = true;
            }
            else if (character == ',')
            {
                AddField();
            }
            else if (character is '\r' or '\n')
            {
                AddRow();
                if (character == '\r'
                    && index + 1 < text.Length
                    && text[index + 1] == '\n')
                {
                    index++;
                }
            }
            else
            {
                if (closedQuote)
                {
                    throw new InvalidDataException(
                        "The published regional Codex candidate CSV contains text after a closing quote.");
                }

                field.Append(character);
            }

            if (field.Length > MaximumCsvFieldCharacters)
            {
                throw new InvalidDataException(
                    "The published regional Codex candidate CSV contains an oversized field.");
            }

            index++;
        }

        if (inQuotes)
        {
            throw new InvalidDataException(
                "The published regional Codex candidate CSV ends inside a quoted field.");
        }

        if (field.Length > 0 || row.Count > 0)
        {
            AddRow();
        }

        return rows;
    }

    private static bool ParseCsvBoolean(
        string value,
        int zeroBasedRowIndex,
        string column)
    {
        return value.Trim() switch
        {
            "0" => false,
            "1" => true,
            _ => throw new InvalidDataException(
                $"Published regional Codex row {zeroBasedRowIndex + 1:N0} has an invalid {column} flag."),
        };
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
