using System.Text.Json;
using System.Text.Json.Serialization;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Navigation;

public sealed record GalacticBookmark
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string System { get; init; } = "";
    public string Body { get; init; } = "";
    public string Ring { get; init; } = "";
    public GalacticCoordinate? Position { get; init; }
    public string Category { get; init; } = "Mining";
    public IReadOnlyList<string> CategoryAssignments { get; init; } = [];
    public string Notes { get; init; } = "";
    public string LastMined { get; init; } = "";
    public string Hotspot { get; init; } = "";
    public string AverageYield { get; init; } = "";
    public List<string> Screenshots { get; init; } = [];
    public int Rating { get; init; }
    public string Minerals { get; init; } = "";
    public string RingType { get; init; } = "";
    public string Reserve { get; init; } = "";
    public string Overlaps { get; init; } = "";
    public string ResourceExtractionSites { get; init; } = "";
    public MineMapSurvey? SurfaceMiningMap { get; init; }
    public DateTimeOffset Updated { get; init; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public bool IsSurfaceMiningMap => SurfaceMiningMap is not null;

    [JsonIgnore]
    public IReadOnlyList<string> EffectiveCategoryAssignments =>
        BookmarkCategoryCatalog.Normalize(CategoryAssignments, Category);

    [JsonIgnore]
    public string CategoryDisplay => string.Join(", ", EffectiveCategoryAssignments);

    [JsonIgnore]
    public string DisplayBody => TrimSystemPrefix(
        System,
        SplitBodyAndRing(Body, Ring).Body);

    [JsonIgnore]
    public string DisplayRing => SplitBodyAndRing(Body, Ring).Ring;

    [JsonIgnore]
    public string CombinedBodyAndRing
    {
        get
        {
            var location = SplitBodyAndRing(Body, Ring);
            return string.IsNullOrWhiteSpace(location.Ring)
                ? location.Body
                : $"{location.Body} {location.Ring}";
        }
    }

    public bool HasCategory(string category) => EffectiveCategoryAssignments.Contains(
        category,
        StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public string DisplayDetails => SurfaceMiningMap is { } map
        ? $"Signal {map.LocationSignal} · {map.MineralAmount} amount · {map.Density} density"
        : Minerals;

    public static (string Body, string Ring) SplitBodyAndRing(
        string? body,
        string? ring = null)
    {
        var normalizedBody = body?.Trim() ?? string.Empty;
        var normalizedRing = ring?.Trim() ?? string.Empty;
        if (normalizedRing.Length > 0)
        {
            return (normalizedBody, normalizedRing);
        }

        var suffix = normalizedBody.LastIndexOf(" Ring", StringComparison.OrdinalIgnoreCase);
        if (suffix != normalizedBody.Length - 5 || suffix <= 0)
        {
            return (normalizedBody, string.Empty);
        }

        var ringTokenStart = normalizedBody.LastIndexOf(' ', suffix - 1);
        if (ringTokenStart < 0)
        {
            return (normalizedBody, string.Empty);
        }

        var ringToken = normalizedBody[(ringTokenStart + 1)..suffix];
        if (ringToken.Length != 1 || !char.IsLetter(ringToken[0]))
        {
            return (normalizedBody, string.Empty);
        }

        return (
            normalizedBody[..ringTokenStart].TrimEnd(),
            $"{ringToken} Ring");
    }

    public static string TrimSystemPrefix(string? system, string? body)
    {
        var normalizedSystem = system?.Trim() ?? string.Empty;
        var normalizedBody = body?.Trim() ?? string.Empty;
        if (normalizedSystem.Length == 0
            || normalizedBody.Length <= normalizedSystem.Length
            || !normalizedBody.StartsWith(normalizedSystem, StringComparison.OrdinalIgnoreCase)
            || !char.IsWhiteSpace(normalizedBody[normalizedSystem.Length]))
        {
            return normalizedBody;
        }

        return normalizedBody[normalizedSystem.Length..].TrimStart();
    }
}

/// <summary>One location catalog shared by navigation and mining; importing never silently replaces an existing bookmark.</summary>
public sealed class BookmarkCatalog
{
    private readonly string path;
    private List<GalacticBookmark> items;
    public BookmarkCatalog(string directory)
    {
        path = Path.Combine(directory, "bookmarks.json");
        items = File.Exists(path) ? Parse(File.ReadAllText(path)) : [];
    }
    public IReadOnlyList<GalacticBookmark> Items => items;
    public event EventHandler? Changed;
    private IReadOnlyList<string>? categories;
    public IReadOnlyList<string> Categories => categories ??= ReadCategories();
    private string[] ReadCategories() => [.. BookmarkCategoryCatalog.All];
    public IReadOnlyList<GalacticBookmark> Filter(string? category, string? query) => items
        .Where(b => (string.IsNullOrEmpty(category) || category == "All" || b.HasCategory(category))
            && (string.IsNullOrWhiteSpace(query) || $"{b.System} {b.DisplayBody} {b.DisplayRing} {b.Notes} {b.Minerals} {b.DisplayDetails}".Contains(query, StringComparison.OrdinalIgnoreCase)))
        .OrderBy(b => b.System).ThenBy(b => b.DisplayBody).ThenBy(b => b.DisplayRing).ToArray();

    public void Save(GalacticBookmark bookmark)
    {
        Validate(bookmark);
        var categories = BookmarkCategoryCatalog.Normalize(
            bookmark.CategoryAssignments,
            bookmark.Category);
        var location = GalacticBookmark.SplitBodyAndRing(bookmark.Body, bookmark.Ring);
        var next = items.Where(b => b.Id != bookmark.Id).Append(bookmark with
        {
            System = bookmark.System.Trim(),
            Body = location.Body,
            Ring = location.Ring,
            Category = categories[0],
            CategoryAssignments = categories,
        }).ToList();
        Persist(next);
    }
    public void Delete(Guid id) => Persist(items.Where(b => b.Id != id).ToList());
    public string Export() => JsonSerializer.Serialize(items, JsonOptions);
    public static void ValidateImport(string json) => Parse(json);
    public void Restore(string json)
    {
        var restored = Parse(json);
        if (File.Exists(path)) File.Copy(path, path + ".before-restore", true);
        Persist(restored);
    }
    public void Import(string json)
    {
        var incoming = Parse(json);
        var next = items.ToList();
        foreach (var bookmark in incoming.Where(bookmark => !next.Any(
            existing => IsSameBookmark(existing, bookmark))))
            next.Add(bookmark);
        Persist(next);
    }

    private void Persist(List<GalacticBookmark> next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(next, JsonOptions));
            File.Move(temporary, path, true);
            items = next;
            categories = null;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
    public static List<GalacticBookmark> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Expected a bookmark list.");
        var parsed = new List<GalacticBookmark>();
        foreach (var row in document.RootElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) throw new JsonException("Expected a bookmark object.");
            if (row.TryGetProperty("system", out _))
            {
                parsed.Add(ReadLegacyBookmark(row));
            }
            else parsed.Add(row.Deserialize<GalacticBookmark>(JsonOptions)
                ?? throw new JsonException("Empty bookmark."));
        }
        foreach (var bookmark in parsed) Validate(bookmark);
        return parsed;
    }
    private static GalacticBookmark ReadLegacyBookmark(JsonElement row)
    {
        string Text(string key) => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        var rating = row.TryGetProperty("rating", out var r) && int.TryParse(r.ToString(), out var n) ? n : 0;
        var location = GalacticBookmark.SplitBodyAndRing(Text("body"));
        return new GalacticBookmark
        {
            System = Text("system"),
            Body = location.Body,
            Ring = location.Ring,
            Category = "Mining",
            Minerals = Text("materials"),
            Notes = Text("notes"),
            Rating = Math.Clamp(rating, 0, 5),
            Hotspot = Text("hotspot"),
            AverageYield = Text("avg_yield"),
            LastMined = Text("last_mined"),
            Overlaps = $"{Text("target_material")} {Text("overlap_type")}".Trim(),
            ResourceExtractionSites = $"{Text("res_material")} {Text("res_site")}".Trim()
        };
    }
    private static void Validate(GalacticBookmark bookmark)
    {
        if (bookmark is null || string.IsNullOrWhiteSpace(bookmark.System)
            || bookmark.Rating is < 0 or > 5 || bookmark.Body is null || bookmark.Ring is null || bookmark.Notes is null || bookmark.Minerals is null || bookmark.Overlaps is null || bookmark.ResourceExtractionSites is null || bookmark.Screenshots is null || bookmark.Screenshots.Any(string.IsNullOrWhiteSpace) || bookmark.LastMined is null || bookmark.Hotspot is null || bookmark.AverageYield is null)
            throw new JsonException("Each bookmark needs a system, category and rating between 0 and 5.");
        _ = BookmarkCategoryCatalog.Normalize(
            bookmark.CategoryAssignments,
            bookmark.Category);
        ValidateSurfaceMiningMap(bookmark);
    }

    private static bool IsSameBookmark(
        GalacticBookmark existing,
        GalacticBookmark incoming)
    {
        if (existing.Id == incoming.Id)
        {
            return true;
        }

        var existingMap = existing.SurfaceMiningMap;
        var incomingMap = incoming.SurfaceMiningMap;
        if (existingMap is not null || incomingMap is not null)
        {
            return existingMap is not null
                && incomingMap is not null
                && existing.System.Equals(
                    incoming.System,
                    StringComparison.OrdinalIgnoreCase)
                && existing.CombinedBodyAndRing.Equals(
                    incoming.CombinedBodyAndRing,
                    StringComparison.OrdinalIgnoreCase)
                && existingMap.LocationSignal == incomingMap.LocationSignal
                && existingMap.Center == incomingMap.Center;
        }

        return existing.System.Equals(
                incoming.System,
                StringComparison.OrdinalIgnoreCase)
            && existing.CombinedBodyAndRing.Equals(
                incoming.CombinedBodyAndRing,
                StringComparison.OrdinalIgnoreCase)
            && existing.EffectiveCategoryAssignments.Intersect(
                incoming.EffectiveCategoryAssignments,
                StringComparer.OrdinalIgnoreCase).Any();
    }

    private static void ValidateSurfaceMiningMap(GalacticBookmark bookmark)
    {
        if (bookmark.SurfaceMiningMap is not { } map)
        {
            return;
        }

        if (map.Id == Guid.Empty
            || map.Id != bookmark.Id
            || string.IsNullOrWhiteSpace(map.FrontierId)
            || string.IsNullOrWhiteSpace(map.SystemName)
            || map.SystemAddress <= 0
            || map.BodyId < 0
            || string.IsNullOrWhiteSpace(map.BodyName)
            || string.IsNullOrWhiteSpace(map.BodyType)
            || !double.IsFinite(map.ArrivalDistanceLs)
            || map.ArrivalDistanceLs < 0
            || map.LocationSignal <= 0
            || !Enum.IsDefined(map.MineralAmount)
            || !Enum.IsDefined(map.Density)
            || !double.IsFinite(map.PlanetRadiusMeters)
            || map.PlanetRadiusMeters <= 0
            || map.Markers is null
            || map.Markers.Any(marker => marker is null
                || marker.Id == Guid.Empty
                || string.IsNullOrWhiteSpace(marker.Material)))
        {
            throw new JsonException(
                "A Surface Mining bookmark contains incomplete or invalid map data.");
        }
    }
}

public static class BookmarkCategoryCatalog
{
    public const string Mining = "Mining";
    public const string SurfaceMining = "Surface Mining";
    public const string Location = "Location";
    public const string Poi = "POI";
    public const string Other = "Other";

    public static IReadOnlyList<string> All { get; } =
        [Mining, SurfaceMining, Location, Poi, Other];

    public static IReadOnlyList<string> Normalize(
        IEnumerable<string>? assignments,
        string? legacyCategory)
    {
        var normalized = (assignments ?? [])
            .Select(Resolve)
            .Where(category => category is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => Array.FindIndex(
                [.. All],
                candidate => candidate.Equals(category, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (normalized.Length > 0)
        {
            return normalized;
        }

        return [Resolve(legacyCategory) ?? Other];
    }

    private static string? Resolve(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        return All.FirstOrDefault(candidate => candidate.Equals(
            category.Trim(),
            StringComparison.OrdinalIgnoreCase)) ?? Other;
    }
}
